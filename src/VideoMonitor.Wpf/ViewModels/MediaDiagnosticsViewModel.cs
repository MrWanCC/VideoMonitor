using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MediaDiagnosticsViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IMediaDiagnosticsApiClient apiClient;
    private readonly IDeviceCatalogReadModel catalog;
    private readonly Func<Uri?> baseUriProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly ObservableCollection<MediaDiagnosticsStreamRowViewModel> streams = [];
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource disposalCancellation = new();
    private readonly CancellationToken disposalToken;
    private readonly object lifecycleGate = new();
    private CancellationTokenSource? pollingCancellation;
    private Task? startTask;
    private Task? pollingTask;
    private Task? stopTask;
    private Task? disposalTask;
    private bool disposed;
    private MediaServerHealth serverHealth = MediaServerHealth.Unconfigured;
    private int activeStreamCount;
    private int viewerCount;
    private int faultCount;
    private bool isBusy;
    private bool isUnavailable;
    private string statusText = "尚未加载媒体诊断";

    public MediaDiagnosticsViewModel(
        IMediaDiagnosticsApiClient apiClient,
        IDeviceCatalogReadModel catalog,
        Func<Uri?> baseUriProvider,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.baseUriProvider = baseUriProvider
            ?? throw new ArgumentNullException(nameof(baseUriProvider));
        this.delayAsync = delayAsync ?? Task.Delay;
        disposalToken = disposalCancellation.Token;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RetryCommand = new AsyncRelayCommand<MediaDiagnosticsStreamRowViewModel>(
            RetryAsync,
            CanRetry);
    }

    public MediaServerHealth ServerHealth
    {
        get => serverHealth;
        private set => SetProperty(ref serverHealth, value);
    }

    public int ActiveStreamCount
    {
        get => activeStreamCount;
        private set => SetProperty(ref activeStreamCount, value);
    }

    public int ViewerCount
    {
        get => viewerCount;
        private set => SetProperty(ref viewerCount, value);
    }

    public int FaultCount
    {
        get => faultCount;
        private set => SetProperty(ref faultCount, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                ((AsyncRelayCommand<MediaDiagnosticsStreamRowViewModel>)RetryCommand)
                    .NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsUnavailable
    {
        get => isUnavailable;
        private set => SetProperty(ref isUnavailable, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public IReadOnlyList<MediaDiagnosticsStreamRowViewModel> Streams => streams;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<MediaDiagnosticsStreamRowViewModel> RetryCommand { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (startTask is not null)
            {
                return startTask;
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

            stopTask = StopCoreAsync(cancellationToken);
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
            await operationGate.WaitAsync(cancellation.Token);
            try
            {
                IsBusy = true;
                await FetchSnapshotAsync(cancellation.Token);
            }
            finally
            {
                IsBusy = false;
                operationGate.Release();
            }

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
                await delayAsync(PollInterval, cancellationToken);
                if (!await operationGate.WaitAsync(0, cancellationToken))
                {
                    continue;
                }

                try
                {
                    IsBusy = true;
                    await FetchSnapshotAsync(cancellationToken);
                }
                finally
                {
                    IsBusy = false;
                    operationGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAsync()
    {
        var cancellationToken = GetLifetimeToken();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            var endpoint = TryGetEndpoint();
            if (endpoint is null)
            {
                return;
            }

            try
            {
                await apiClient.RequestRefreshAsync(endpoint, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                MarkUnavailable();
                return;
            }

            await FetchSnapshotAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            operationGate.Release();
        }
    }

    private async Task RetryAsync(MediaDiagnosticsStreamRowViewModel? row)
    {
        if (row is null || !CanRetry(row))
        {
            return;
        }

        var cancellationToken = GetLifetimeToken();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            var endpoint = TryGetEndpoint();
            if (endpoint is null)
            {
                return;
            }

            try
            {
                await apiClient.RetryFaultedAsync(
                    endpoint,
                    row.DeviceId,
                    row.ChannelId,
                    row.StreamType,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                MarkUnavailable();
                return;
            }

            await FetchSnapshotAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
            operationGate.Release();
        }
    }

    private bool CanRetry(MediaDiagnosticsStreamRowViewModel? row) =>
        row is not null && row.CanRetry && !IsBusy;

    private async Task FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var endpoint = TryGetEndpoint();
        if (endpoint is null)
        {
            return;
        }

        try
        {
            var snapshot = await apiClient.GetDiagnosticsAsync(
                endpoint,
                cancellationToken);
            Apply(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            MarkUnavailable();
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
                MarkUnavailable();
                return null;
            }

            return endpoint;
        }
        catch
        {
            MarkUnavailable();
            return null;
        }
    }

    private void Apply(MediaDiagnosticsSnapshotDto snapshot)
    {
        ServerHealth = snapshot.ServerHealth;
        ActiveStreamCount = snapshot.ActiveStreamCount;
        ViewerCount = snapshot.ViewerCount;
        FaultCount = snapshot.FaultCount;
        IsUnavailable = snapshot.ServerHealth == MediaServerHealth.Unavailable;
        StatusText = IsUnavailable
            ? "中央服务器暂不可用。"
            : "媒体诊断已更新。";

        var desiredKeys = snapshot.Streams
            .Select(stream => new MediaStreamKey(
                stream.DeviceId,
                stream.ChannelId,
                stream.StreamType))
            .ToHashSet();

        for (var index = streams.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(streams[index].Key))
            {
                streams.RemoveAt(index);
            }
        }

        for (var index = 0; index < snapshot.Streams.Count; index++)
        {
            var stream = snapshot.Streams[index];
            var key = new MediaStreamKey(
                stream.DeviceId,
                stream.ChannelId,
                stream.StreamType);
            var row = streams.FirstOrDefault(candidate => candidate.Key == key);
            if (row is null)
            {
                row = new MediaDiagnosticsStreamRowViewModel(key);
                streams.Insert(Math.Min(index, streams.Count), row);
            }
            else
            {
                var currentIndex = streams.IndexOf(row);
                if (currentIndex != index)
                {
                    streams.Move(currentIndex, Math.Min(index, streams.Count - 1));
                }
            }

            var device = catalog.GetDevice(stream.DeviceId);
            var channel = device?.Channels
                .SingleOrDefault(candidate => candidate.Id == stream.ChannelId);
            row.Apply(
                stream,
                device?.Name ?? "未知设备",
                channel?.ChannelName ?? "未知通道",
                channel?.ChannelNo ?? 0);
        }

        ((AsyncRelayCommand<MediaDiagnosticsStreamRowViewModel>)RetryCommand)
            .NotifyCanExecuteChanged();
    }

    private void MarkUnavailable()
    {
        ServerHealth = MediaServerHealth.Unavailable;
        IsUnavailable = true;
        StatusText = "中央服务器暂不可用。";
    }

    private CancellationToken GetLifetimeToken()
    {
        lock (lifecycleGate)
        {
            return pollingCancellation?.Token ?? disposalToken;
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cancellation;
        Task? start;
        Task? polling;
        lock (lifecycleGate)
        {
            cancellation = pollingCancellation;
            start = startTask;
            polling = pollingTask;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (start is not null)
            {
                await start.WaitAsync(cancellationToken);
            }

            if (polling is not null)
            {
                await polling.WaitAsync(cancellationToken);
            }
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

        await StopAsync();

        await operationGate.WaitAsync();
        operationGate.Release();
        operationGate.Dispose();
        disposalCancellation.Dispose();
    }
}
