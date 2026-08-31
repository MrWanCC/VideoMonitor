using VideoMonitor.Core.Catalog;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Wpf.Catalog;

public sealed class ServerConnectionCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan[] ReconnectBaseDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(15)
    ];

    private static readonly TimeSpan PeriodicRefreshBaseDelay =
        TimeSpan.FromSeconds(30);

    private readonly IClientSettingsStore settingsStore;
    private readonly ICatalogConnectionClient apiClient;
    private readonly ClientCatalogCache cache;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IClientConnectionClock clock;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly SemaphoreSlim wakeSignal = new(0, 1);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object lifecycleLock = new();
    private Task? runTask;
    private Uri? configuredBaseUri;
    private long endpointGeneration;
    private bool endpointConnected;
    private DateTimeOffset? lastSuccessfulSyncUtc;
    private bool hasSuccessfulSync;
    private int activeOperations;
    private TaskCompletionSource<object?>? operationsDrained;
    private TaskCompletionSource<object?>? disposalCompleted;
    private bool disposed;

    public ServerConnectionCoordinator(
        IClientSettingsStore settingsStore,
        ICatalogConnectionClient apiClient,
        ClientCatalogCache cache,
        IUiDispatcher uiDispatcher,
        IClientConnectionClock clock)
    {
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.apiClient = apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));
        this.cache = cache
            ?? throw new ArgumentNullException(nameof(cache));
        this.uiDispatcher = uiDispatcher
            ?? throw new ArgumentNullException(nameof(uiDispatcher));
        this.clock = clock
            ?? throw new ArgumentNullException(nameof(clock));
        Status = new(
            null,
            ServerConnectionState.Unconfigured,
            null,
            false);
    }

    public ServerConnectionStatus Status { get; private set; }

    public event EventHandler? StatusChanged;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleLock)
        {
            ThrowIfDisposed();
            return runTask ??= RunLoopAsync(cancellationToken);
        }
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        EnterOperation();
        var gateEntered = false;
        try
        {
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    shutdown.Token);
            var operationToken = operationCancellation.Token;
            var endpointState = await GetEndpointStateAsync()
                .ConfigureAwait(false);
            if (endpointState.Endpoint is null)
            {
                return;
            }

            if (!await refreshGate
                    .WaitAsync(0, operationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            gateEntered = true;
            try
            {
                operationToken.ThrowIfCancellationRequested();
                var snapshot = await GetCatalogSnapshotAsync(
                        endpointState.Endpoint,
                        operationToken)
                    .ConfigureAwait(false);
                operationToken.ThrowIfCancellationRequested();
                _ = await CommitConnectedAsync(
                        endpointState.Endpoint,
                        snapshot,
                        endpointState.Generation,
                        operationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested
                    && !shutdown.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
                when (shutdown.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                if (!shutdown.IsCancellationRequested
                    && await PublishUnavailableAsync(
                            endpointState.Endpoint,
                            endpointState.Generation)
                        .ConfigureAwait(false)
                    && !shutdown.IsCancellationRequested)
                {
                    SignalWake();
                }
            }
        }
        finally
        {
            if (gateEntered)
            {
                refreshGate.Release();
            }

            ExitOperation();
        }
    }

    public async Task ProbeAsync(
        Uri candidate,
        CancellationToken cancellationToken = default)
    {
        EnterOperation();
        try
        {
            ValidateBaseUri(candidate);
            _ = await ProbeAndPrepareAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task SwitchServerAsync(
        Uri candidate,
        Func<bool> hasUnsavedDraft,
        CancellationToken cancellationToken = default)
    {
        EnterOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(hasUnsavedDraft);
            ValidateBaseUri(candidate);

            var preparedSnapshot = await ProbeAndPrepareAsync(
                    candidate,
                    cancellationToken)
                .ConfigureAwait(false);

            if (hasUnsavedDraft())
            {
                throw new InvalidOperationException(
                    "Unsaved Catalog edits block a Server switch.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await settingsStore.SaveAsync(
                    new ClientSettings(
                        new ClientServerSettings(candidate.ToString())),
                    cancellationToken)
                .ConfigureAwait(false);

            await stateGate.WaitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                endpointGeneration++;
                await uiDispatcher.InvokeAsync(
                        () =>
                        {
                            configuredBaseUri = candidate;
                            endpointConnected = true;
                            lastSuccessfulSyncUtc = clock.UtcNow;
                            hasSuccessfulSync = true;
                            Status = new ServerConnectionStatus(
                                candidate,
                                ServerConnectionState.Connected,
                                lastSuccessfulSyncUtc,
                                false);
                            cache.ApplyPreparedSnapshotOnUiThread(preparedSnapshot);
                            StatusChanged?.Invoke(this, EventArgs.Empty);
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                stateGate.Release();
            }
            SignalWake();
        }
        finally
        {
            ExitOperation();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? activeRun;
        Task? activeOperationsTask;
        TaskCompletionSource<object?> disposalCompletion;
        var ownsDisposal = false;
        lock (lifecycleLock)
        {
            if (disposed)
            {
                disposalCompletion = disposalCompleted!;
                activeRun = null;
                activeOperationsTask = null;
            }
            else
            {
                disposed = true;
                disposalCompletion = disposalCompleted =
                    new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                activeRun = runTask;
                activeOperationsTask = null;
                if (activeOperations > 0)
                {
                    operationsDrained = new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    activeOperationsTask = operationsDrained.Task;
                }

                ownsDisposal = true;
            }
        }

        if (!ownsDisposal)
        {
            await disposalCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            shutdown.Cancel();
            if (activeRun is not null)
            {
                await activeRun.ConfigureAwait(false);
            }

            if (activeOperationsTask is not null)
            {
                await activeOperationsTask.ConfigureAwait(false);
            }

            refreshGate.Dispose();
            stateGate.Dispose();
            wakeSignal.Dispose();
            shutdown.Dispose();
            disposalCompletion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            disposalCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            shutdown.Token);
        var token = linkedCancellation.Token;

        try
        {
            var configuredValue = settingsStore.Load().Server.BaseUrl;
            var waitsForEndpoint = false;
            Uri? initialEndpoint = null;
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                await PublishUnconfiguredAsync().ConfigureAwait(false);
                waitsForEndpoint = true;
            }
            else if (!TryParseBaseUri(configuredValue, out initialEndpoint))
            {
                var state = await GetEndpointStateAsync().ConfigureAwait(false);
                await PublishUnavailableAsync(
                        null,
                        state.Generation)
                    .ConfigureAwait(false);
                waitsForEndpoint = true;
            }

            if (initialEndpoint is not null)
            {
                await SetInitialEndpointAsync(initialEndpoint).ConfigureAwait(false);
            }

            if (waitsForEndpoint)
            {
                var state = await GetEndpointStateAsync().ConfigureAwait(false);
                if (state.Endpoint is null)
                {
                    await wakeSignal.WaitAsync(token).ConfigureAwait(false);
                }
            }

            var retryIndex = 0;
            var initialConnectionPending = true;
            var reconnectPending = false;

            while (!token.IsCancellationRequested)
            {
                var endpointState = await GetEndpointStateAsync()
                    .ConfigureAwait(false);
                if (endpointState.Endpoint is null)
                {
                    await PublishUnconfiguredAsync().ConfigureAwait(false);
                    await wakeSignal.WaitAsync(token).ConfigureAwait(false);
                    continue;
                }

                if (reconnectPending)
                {
                    if (endpointState.Connected && !initialConnectionPending)
                    {
                        reconnectPending = false;
                    }
                    else
                    {
                        await DelayWithJitterAsync(
                                ReconnectBaseDelays[retryIndex],
                                token)
                            .ConfigureAwait(false);
                        retryIndex = Math.Min(
                            retryIndex + 1,
                            ReconnectBaseDelays.Length - 1);
                        reconnectPending = false;
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        endpointState = await GetEndpointStateAsync()
                            .ConfigureAwait(false);
                        if (endpointState.Endpoint is null)
                        {
                            continue;
                        }
                    }
                }

                if ((initialConnectionPending || !endpointState.Connected)
                    && !await TryConnectAsync(
                            endpointState.Endpoint,
                            endpointState.Generation,
                            token)
                        .ConfigureAwait(false))
                {
                    reconnectPending = true;
                    continue;
                }

                initialConnectionPending = false;
                reconnectPending = false;

                retryIndex = 0;
                while (!token.IsCancellationRequested)
                {
                    var woken = await WaitForDelayOrWakeAsync(
                            PeriodicRefreshBaseDelay,
                            token)
                        .ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (woken)
                    {
                        endpointState = await GetEndpointStateAsync()
                            .ConfigureAwait(false);
                        if (!endpointState.Connected)
                        {
                            reconnectPending = true;
                            break;
                        }

                        continue;
                    }

                    endpointState = await GetEndpointStateAsync()
                        .ConfigureAwait(false);
                    if (endpointState.Endpoint is null)
                    {
                        await PublishUnconfiguredAsync().ConfigureAwait(false);
                        break;
                    }

                    if (!endpointState.Connected)
                    {
                        break;
                    }

                    try
                    {
                        if (!await RunRefreshAndCommitAsync(
                                    endpointState.Endpoint,
                                    endpointState.Generation,
                                    token)
                                .ConfigureAwait(false))
                        {
                            break;
                        }

                        retryIndex = 0;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        _ = await PublishUnavailableAsync(
                                endpointState.Endpoint,
                                endpointState.Generation)
                            .ConfigureAwait(false);
                        reconnectPending = true;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> TryConnectAsync(
        Uri endpoint,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        await PublishConnectingAsync(endpoint, expectedGeneration)
            .ConfigureAwait(false);
        try
        {
            await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await apiClient.CheckReadyAsync(endpoint, cancellationToken)
                    .ConfigureAwait(false);
                var snapshot = await apiClient.GetCatalogAsync(
                        endpoint,
                        cancellationToken)
                    .ConfigureAwait(false);
                var prepared = cache.PrepareSnapshot(snapshot);
                _ = await CommitConnectedAsync(
                        endpoint,
                        prepared,
                        expectedGeneration,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                refreshGate.Release();
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await PublishUnavailableAsync(endpoint, expectedGeneration)
                .ConfigureAwait(false);
            return false;
        }
    }

    private async Task<bool> RunRefreshAndCommitAsync(
        Uri endpoint,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await GetCatalogSnapshotAsync(endpoint, cancellationToken)
                .ConfigureAwait(false);
            return await CommitConnectedAsync(
                    endpoint,
                    snapshot,
                    expectedGeneration,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task<CatalogSnapshotDto> ProbeAndPrepareAsync(
        Uri candidate,
        CancellationToken cancellationToken)
    {
        await apiClient.CheckReadyAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await apiClient.GetCatalogAsync(
                candidate,
                cancellationToken)
            .ConfigureAwait(false);
        return cache.PrepareSnapshot(snapshot);
    }

    private async Task<CatalogSnapshotDto> GetCatalogSnapshotAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        var snapshot = await apiClient.GetCatalogAsync(
                endpoint,
                cancellationToken)
            .ConfigureAwait(false);
        return cache.PrepareSnapshot(snapshot);
    }

    private async Task<bool> CommitConnectedAsync(
        Uri endpoint,
        CatalogSnapshotDto snapshot,
        long expectedGeneration,
        CancellationToken dispatcherCancellationToken)
    {
        await stateGate.WaitAsync(dispatcherCancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (shutdown.IsCancellationRequested
                || expectedGeneration != endpointGeneration
                || !Equals(configuredBaseUri, endpoint))
            {
                return false;
            }

            await uiDispatcher.InvokeAsync(
                    () =>
                    {
                        lastSuccessfulSyncUtc = clock.UtcNow;
                        hasSuccessfulSync = true;
                        endpointConnected = true;
                        Status = new ServerConnectionStatus(
                            endpoint,
                            ServerConnectionState.Connected,
                            lastSuccessfulSyncUtc,
                            false);
                        cache.ApplyPreparedSnapshotOnUiThread(snapshot);
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                    },
                    dispatcherCancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task PublishConnectingAsync(
        Uri endpoint,
        long expectedGeneration)
    {
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (shutdown.IsCancellationRequested
                || expectedGeneration != endpointGeneration
                || !Equals(configuredBaseUri, endpoint))
            {
                return;
            }

            await uiDispatcher.InvokeAsync(
                    () =>
                    {
                        endpointConnected = false;
                        Status = new ServerConnectionStatus(
                            endpoint,
                            ServerConnectionState.Connecting,
                            lastSuccessfulSyncUtc,
                            hasSuccessfulSync);
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task<bool> PublishUnavailableAsync(
        Uri? endpoint,
        long? expectedGeneration = null)
    {
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (shutdown.IsCancellationRequested
                || (expectedGeneration.HasValue
                    && expectedGeneration.Value != endpointGeneration)
                || (endpoint is null && configuredBaseUri is not null)
                || (endpoint is not null
                    && !Equals(configuredBaseUri, endpoint)))
            {
                return false;
            }

            var published = false;
            await uiDispatcher.InvokeAsync(
                    () =>
                    {
                        endpointConnected = false;
                        Status = new ServerConnectionStatus(
                            endpoint,
                            ServerConnectionState.Unavailable,
                            lastSuccessfulSyncUtc,
                            hasSuccessfulSync);
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                        published = true;
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            return published;
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task PublishUnconfiguredAsync()
    {
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (shutdown.IsCancellationRequested
                || configuredBaseUri is not null)
            {
                return;
            }

            await uiDispatcher.InvokeAsync(
                    () =>
                    {
                        endpointConnected = false;
                        Status = new ServerConnectionStatus(
                            null,
                            ServerConnectionState.Unconfigured,
                            null,
                            false);
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task<EndpointState> GetEndpointStateAsync()
    {
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            return new EndpointState(
                configuredBaseUri,
                endpointGeneration,
                endpointConnected);
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task SetInitialEndpointAsync(Uri endpoint)
    {
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (configuredBaseUri is null)
            {
                configuredBaseUri = endpoint;
                endpointConnected = false;
            }
        }
        finally
        {
            stateGate.Release();
        }
    }

    private void SignalWake()
    {
        try
        {
            wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<bool> WaitForDelayOrWakeAsync(
        TimeSpan baseDelay,
        CancellationToken cancellationToken)
    {
        var jitteredDelay = GetJitteredDelay(baseDelay);
        using var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = clock.DelayAsync(
            jitteredDelay,
            waitCancellation.Token);
        var wakeTask = wakeSignal.WaitAsync(waitCancellation.Token);

        try
        {
            var completed = await Task.WhenAny(delayTask, wakeTask)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, wakeTask))
            {
                await wakeTask.ConfigureAwait(false);
                return true;
            }

            await delayTask.ConfigureAwait(false);
            return false;
        }
        finally
        {
            waitCancellation.Cancel();
            await ObserveTaskAsync(delayTask).ConfigureAwait(false);
            await ObserveTaskAsync(wakeTask).ConfigureAwait(false);
        }
    }

    private async Task DelayWithJitterAsync(
        TimeSpan baseDelay,
        CancellationToken cancellationToken)
    {
        await clock.DelayAsync(
                GetJitteredDelay(baseDelay),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private TimeSpan GetJitteredDelay(TimeSpan baseDelay)
    {
        var jitterUnit = clock.NextJitterUnit();
        if (jitterUnit is < 0.0 or >= 1.0)
        {
            throw new InvalidOperationException(
                "Connection clock returned an invalid jitter value.");
        }

        var factor = 0.8 + (0.4 * jitterUnit);
        var jitteredDelay = TimeSpan.FromTicks(
            (long)(baseDelay.Ticks * factor));
        return jitteredDelay;
    }

    private static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void EnterOperation()
    {
        lock (lifecycleLock)
        {
            ThrowIfDisposed();
            activeOperations++;
        }
    }

    private void ExitOperation()
    {
        lock (lifecycleLock)
        {
            activeOperations--;
            if (disposed && activeOperations == 0)
            {
                operationsDrained?.TrySetResult(null);
            }
        }
    }

    private static void ValidateBaseUri(Uri candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!TryParseBaseUri(candidate.ToString(), out _))
        {
            throw new ArgumentException(
                "Server BaseUrl must be an absolute HTTP or HTTPS URI.",
                nameof(candidate));
        }
    }

    private static bool TryParseBaseUri(
        string value,
        out Uri? endpoint)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp
                || parsed.Scheme == Uri.UriSchemeHttps))
        {
            endpoint = parsed;
            return true;
        }

        endpoint = null;
        return false;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(ServerConnectionCoordinator));
        }
    }

    private readonly record struct EndpointState(
        Uri? Endpoint,
        long Generation,
        bool Connected);
}
