using VideoMonitor.Core.Media;

namespace VideoMonitor.Wpf.Catalog;

public sealed class MediaRuntimeStatusCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly IMediaDiagnosticsApiClient apiClient;
    private readonly MediaRuntimeStatusStore store;
    private readonly Func<Uri?> baseUriProvider;
    private readonly IUiDispatcher dispatcher;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly TimeSpan pollInterval;
    private readonly CancellationTokenSource disposalCancellation = new();
    private readonly CancellationToken disposalToken;
    private readonly object lifecycleGate = new();
    private CancellationTokenSource? pollingCancellation;
    private Task? startTask;
    private Task? pollingTask;
    private Task? stopTask;
    private Task? disposalTask;
    private bool disposed;

    public MediaRuntimeStatusCoordinator(
        IMediaDiagnosticsApiClient apiClient,
        MediaRuntimeStatusStore store,
        Func<Uri?> baseUriProvider,
        IUiDispatcher dispatcher,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? pollInterval = null)
    {
        this.apiClient = apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));
        this.store = store
            ?? throw new ArgumentNullException(nameof(store));
        this.baseUriProvider = baseUriProvider
            ?? throw new ArgumentNullException(nameof(baseUriProvider));
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        this.delayAsync = delayAsync ?? Task.Delay;
        this.pollInterval = pollInterval ?? DefaultPollInterval;
        disposalToken = disposalCancellation.Token;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pollingCancellation is not null)
            {
                return startTask ?? Task.CompletedTask;
            }

            pollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                disposalToken);
            startTask = StartCoreAsync(pollingCancellation);
            return startTask;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycleGate)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            if (pollingCancellation is null)
            {
                return Task.CompletedTask;
            }

            stopTask = StopCoreAsync(pollingCancellation, cancellationToken);
            return stopTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new ValueTask(disposalTask);
        }
    }

    private async Task StartCoreAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await FetchSnapshotAsync(cancellation.Token).ConfigureAwait(false);

            lock (lifecycleGate)
            {
                if (!cancellation.IsCancellationRequested
                    && ReferenceEquals(pollingCancellation, cancellation))
                {
                    pollingTask = PollAsync(cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await delayAsync(pollInterval, cancellationToken)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await FetchSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var endpoint = TryGetEndpoint();
        if (endpoint is null)
        {
            await PublishUnavailableAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var snapshot = await apiClient
                .GetDiagnosticsAsync(endpoint, cancellationToken)
                .ConfigureAwait(false);
            await dispatcher
                .InvokeAsync(() => store.Apply(snapshot), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await PublishUnavailableAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Uri? TryGetEndpoint()
    {
        try
        {
            var endpoint = baseUriProvider();
            if (endpoint is null
                || !endpoint.IsAbsoluteUri
                || (endpoint.Scheme != Uri.UriSchemeHttp
                    && endpoint.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(endpoint.Host)
                || !string.IsNullOrEmpty(endpoint.UserInfo))
            {
                return null;
            }

            return endpoint;
        }
        catch
        {
            return null;
        }
    }

    private async Task PublishUnavailableAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await dispatcher
                .InvokeAsync(store.MarkUnavailable, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    private async Task StopCoreAsync(
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        cancellation.Cancel();
        try
        {
            Task? start;
            lock (lifecycleGate)
            {
                start = startTask;
            }

            if (start is not null)
            {
                await start.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            Task? polling;
            lock (lifecycleGate)
            {
                polling = pollingTask;
            }

            if (polling is not null)
            {
                await polling.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            await dispatcher
                .InvokeAsync(store.ClearEvidence, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (lifecycleGate)
            {
                if (ReferenceEquals(pollingCancellation, cancellation))
                {
                    pollingCancellation = null;
                    startTask = null;
                    pollingTask = null;
                    cancellation.Dispose();
                }

                stopTask = null;
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        lock (lifecycleGate)
        {
            disposed = true;
            disposalCancellation.Cancel();
        }

        await StopAsync().ConfigureAwait(false);
        disposalCancellation.Dispose();
    }
}
