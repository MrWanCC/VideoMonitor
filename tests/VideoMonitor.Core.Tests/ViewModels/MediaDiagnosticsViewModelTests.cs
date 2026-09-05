using System.Collections.Concurrent;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MediaDiagnosticsViewModelTests
{
    private static readonly Uri ServerUri = new("https://server.example/");

    [Fact]
    public async Task MediaDiagnosticsViewModelInitialRefresh()
    {
        var fixture = CreateFixture();

        await fixture.ViewModel.StartAsync();

        Assert.Equal(1, fixture.Api.GetCalls);
        Assert.Equal(2, fixture.ViewModel.ActiveStreamCount);
        Assert.Equal(MediaServerHealth.Healthy, fixture.ViewModel.ServerHealth);
    }

    [Fact]
    public async Task MediaDiagnosticsViewModelPollDoesNotOverlap()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        api.BlockNextGet();
        var delay = new PollDelay();
        var viewModel = CreateViewModel(api, delay);

        await viewModel.StartAsync();
        delay.Release();
        await api.SecondGetStarted.Task;
        delay.Release();
        delay.Release();

        Assert.Equal(1, api.MaximumConcurrentGetCalls);

        api.ReleaseBlockedGet();
        await viewModel.StopAsync();
    }

    [Fact]
    public async Task RepeatedStartDoesNotCreateSecondPollingLoop()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        var delay = new PollDelay();
        var viewModel = CreateViewModel(api, delay);

        await Task.WhenAll(viewModel.StartAsync(), viewModel.StartAsync());

        Assert.Equal(1, api.GetCalls);
        Assert.Equal(1, delay.Waiters);

        await viewModel.StopAsync();
    }

    [Fact]
    public async Task MediaDiagnosticsViewModelShowsStale()
    {
        var fixture = CreateFixture(SnapshotWithStaleStream());

        await fixture.ViewModel.StartAsync();

        Assert.True(Assert.Single(fixture.ViewModel.Streams).IsStale);
    }

    [Fact]
    public async Task RetryOnlyEnabledForFaulted()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyAndFaultedStreams());
        var viewModel = CreateViewModel(api, new PollDelay());

        await viewModel.StartAsync();

        var ready = viewModel.Streams.Single(stream =>
            stream.RuntimeState == StreamRuntimeState.Ready);
        var faulted = viewModel.Streams.Single(stream =>
            stream.RuntimeState == StreamRuntimeState.Faulted);

        Assert.False(viewModel.RetryCommand.CanExecute(ready));
        Assert.True(viewModel.RetryCommand.CanExecute(faulted));

        await viewModel.StopAsync();
    }

    [Fact]
    public async Task RetryUsesStableIdentity()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithFaultedStream());
        var viewModel = CreateViewModel(api, new PollDelay());

        await viewModel.StartAsync();
        var row = Assert.Single(viewModel.Streams);

        await viewModel.RetryCommand.ExecuteAsync(row);

        Assert.Equal((row.DeviceId, row.ChannelId, row.StreamType), api.LastRetry);
        await viewModel.StopAsync();
    }

    [Fact]
    public async Task RefreshPostsThenFetches()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        var viewModel = CreateViewModel(api, new PollDelay());

        await viewModel.StartAsync();
        api.Events.Clear();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "POST", "GET" }, api.Events.ToArray());
        await viewModel.StopAsync();
    }

    [Fact]
    public async Task StopCancelsPolling()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        var delay = new PollDelay(blockUntilCancelled: true);
        var viewModel = CreateViewModel(api, delay);

        await viewModel.StartAsync();
        await viewModel.StopAsync();

        Assert.True(delay.CancellationObserved);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        var viewModel = CreateViewModel(api, new PollDelay(blockUntilCancelled: true));

        await viewModel.StartAsync();
        await viewModel.DisposeAsync();
        await viewModel.DisposeAsync();

        Assert.Equal(1, api.GetCalls);
    }

    [Fact]
    public async Task ServerUnavailableDoesNotCreateMessageLoop()
    {
        var api = new TestDiagnosticsApiClient(
            new CatalogApiException("CATALOG_UNAVAILABLE"));
        var viewModel = CreateViewModel(api, new PollDelay());

        await viewModel.StartAsync();

        Assert.True(viewModel.IsUnavailable);
        Assert.Equal(1, api.GetCalls);
        Assert.Equal(0, api.MessageBoxCalls);
        await viewModel.StopAsync();
    }

    [Fact]
    public async Task ServerRecoveryClearsUnavailableState()
    {
        var api = new TestDiagnosticsApiClient(
            new CatalogApiException("CATALOG_UNAVAILABLE"),
            SnapshotWithReadyStreams());
        var viewModel = CreateViewModel(api, new PollDelay());

        await viewModel.StartAsync();
        Assert.True(viewModel.IsUnavailable);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsUnavailable);
        Assert.Equal(MediaServerHealth.Healthy, viewModel.ServerHealth);
        await viewModel.StopAsync();
    }

    [Fact]
    public async Task RowsPreserveStableIdentityAcrossSnapshots()
    {
        var first = SnapshotWithReadyStreams();
        var second = SnapshotWithReadyStreams(
            first.Streams.Select(stream => stream with
            {
                RuntimeState = StreamRuntimeState.Starting
            }).ToArray());
        var api = new TestDiagnosticsApiClient(first, second);
        var viewModel = CreateViewModel(api, new PollDelay());

        await viewModel.StartAsync();
        var originalRows = viewModel.Streams.ToDictionary(row => row.Key);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.All(viewModel.Streams, row =>
            Assert.Same(originalRows[row.Key], row));
        await viewModel.StopAsync();
    }

    [Fact]
    public async Task DeactivateDuringPendingActivateDoesNotLeaveDiagnosticsPolling()
    {
        var settingsApi = new BlockingMediaSettingsApiClient();
        var settings = new MediaSettingsViewModel(settingsApi, () => ServerUri)
        {
            ZlmSecret = "transient-secret"
        };
        var diagnosticsApi = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        var diagnosticsDelay = new PollDelay(blockUntilCancelled: true);
        var diagnostics = CreateViewModel(diagnosticsApi, diagnosticsDelay);
        var page = new MediaPageViewModel(settings, diagnostics);

        try
        {
            var activateTask = page.ActivateAsync();
            await settingsApi.LoadStarted.Task;

            var deactivateTask = page.DeactivateAsync();
            settingsApi.ReleaseLoad();

            await Task.WhenAll(activateTask, deactivateTask);

            Assert.Equal(string.Empty, settings.ZlmSecret);
            Assert.Equal(0, diagnosticsApi.GetCalls);
            Assert.Equal(0, diagnosticsDelay.Waiters);
        }
        finally
        {
            await page.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeDuringInFlightRefreshWaitsForOperationToExit()
    {
        var api = new TestDiagnosticsApiClient(SnapshotWithReadyStreams());
        var delay = new PollDelay(blockUntilCancelled: true);
        var viewModel = CreateViewModel(api, delay);

        await viewModel.StartAsync();
        api.BlockNextRefresh();
        var refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);
        await api.RefreshStarted.Task;

        var disposalTask = viewModel.DisposeAsync().AsTask();
        await api.RefreshCancellationObserved.Task;

        Assert.False(disposalTask.IsCompleted);

        api.ReleaseBlockedRefresh();
        var exception = await Record.ExceptionAsync(async () =>
        {
            try
            {
                await refreshTask;
            }
            catch (OperationCanceledException)
            {
            }
        });

        Assert.Null(exception);
        await disposalTask;
    }

    private static Fixture CreateFixture(
        MediaDiagnosticsSnapshotDto? snapshot = null)
    {
        var api = new TestDiagnosticsApiClient(
            snapshot ?? SnapshotWithReadyStreams());
        var viewModel = CreateViewModel(api, new PollDelay());
        return new Fixture(api, viewModel);
    }

    private static MediaDiagnosticsViewModel CreateViewModel(
        TestDiagnosticsApiClient api,
        PollDelay delay) =>
        new(
            api,
            new TestCatalog(),
            () => ServerUri,
            delay.DelayAsync);

    private static MediaDiagnosticsSnapshotDto SnapshotWithReadyStreams(
        IReadOnlyList<MediaStreamDiagnosticsDto>? streams = null) =>
        new(
            MediaServerHealth.Healthy,
            streams?.Count ?? 2,
            streams?.Sum(stream => stream.ViewerCount) ?? 3,
            streams?.Count(stream => stream.RuntimeState == StreamRuntimeState.Faulted) ?? 0,
            streams ??
            [
                CreateStream(
                    "10000000-0000-0000-0000-000000000001",
                    "20000000-0000-0000-0000-000000000001",
                    StreamRuntimeState.Ready,
                    2),
                CreateStream(
                    "10000000-0000-0000-0000-000000000002",
                    "20000000-0000-0000-0000-000000000002",
                    StreamRuntimeState.Ready,
                    1),
            ]);

    private static MediaDiagnosticsSnapshotDto SnapshotWithStaleStream() =>
        SnapshotWithReadyStreams(
        [
            CreateStream(
                "10000000-0000-0000-0000-000000000001",
                "20000000-0000-0000-0000-000000000001",
                StreamRuntimeState.Ready,
                1,
                isStale: true),
        ]);

    private static MediaDiagnosticsSnapshotDto SnapshotWithFaultedStream() =>
        SnapshotWithReadyStreams(
        [
            CreateStream(
                "10000000-0000-0000-0000-000000000001",
                "20000000-0000-0000-0000-000000000001",
                StreamRuntimeState.Faulted,
                0,
                errorCode: "MEDIA_STREAM_NOT_FOUND",
                errorMessage: "流不可用"),
        ]);

    private static MediaDiagnosticsSnapshotDto SnapshotWithReadyAndFaultedStreams() =>
        SnapshotWithReadyStreams(
        [
            CreateStream(
                "10000000-0000-0000-0000-000000000001",
                "20000000-0000-0000-0000-000000000001",
                StreamRuntimeState.Ready,
                1),
            CreateStream(
                "10000000-0000-0000-0000-000000000002",
                "20000000-0000-0000-0000-000000000002",
                StreamRuntimeState.Faulted,
                0,
                errorCode: "MEDIA_STREAM_NOT_FOUND",
                errorMessage: "流不可用"),
        ]);

    private static MediaStreamDiagnosticsDto CreateStream(
        string deviceId,
        string channelId,
        StreamRuntimeState state,
        int viewers,
        bool isStale = false,
        string? errorCode = null,
        string? errorMessage = null) =>
        new(
            Guid.Parse(deviceId),
            Guid.Parse(channelId),
            StreamType.Main,
            state,
            viewers,
            StreamOwnership.OwnedCurrentProcess,
            DateTimeOffset.UtcNow,
            SourceObservation.Reachable,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            errorCode,
            errorMessage,
            isStale);

    private sealed record Fixture(
        TestDiagnosticsApiClient Api,
        MediaDiagnosticsViewModel ViewModel);

    private sealed class TestCatalog : IDeviceCatalogReadModel
    {
        private readonly Guid groupId = Guid.Parse(
            "30000000-0000-0000-0000-000000000001");
        private readonly IReadOnlyDictionary<Guid, CameraDeviceDto> devices;

        public TestCatalog()
        {
            var firstDeviceId = Guid.Parse(
                "10000000-0000-0000-0000-000000000001");
            var secondDeviceId = Guid.Parse(
                "10000000-0000-0000-0000-000000000002");
            devices = new Dictionary<Guid, CameraDeviceDto>
            {
                [firstDeviceId] = CreateDevice(
                    firstDeviceId,
                    "20000000-0000-0000-0000-000000000001",
                    "设备一",
                    "通道一"),
                [secondDeviceId] = CreateDevice(
                    secondDeviceId,
                    "20000000-0000-0000-0000-000000000002",
                    "设备二",
                    "通道二"),
            };
        }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DeviceGroupDto> GetGroups() => [];

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            devices.Values.Where(device => device.GroupId == groupId).ToArray();

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            devices.TryGetValue(deviceId, out var device) ? device : null;

        private CameraDeviceDto CreateDevice(
            Guid deviceId,
            string channelId,
            string deviceName,
            string channelName) =>
            new(
                deviceId,
                groupId,
                deviceName,
                "",
                8000,
                554,
                "",
                false,
                "",
                "",
                default,
                true,
                "",
                1,
                [new CameraChannelDto(
                    Guid.Parse(channelId),
                    deviceId,
                    1,
                    channelName,
                    StreamType.Main,
                    true)]);
    }

    private sealed class PollDelay
    {
        private readonly bool blockUntilCancelled;
        private TaskCompletionSource<object?> next = CreateSource();

        public PollDelay(bool blockUntilCancelled = false)
        {
            this.blockUntilCancelled = blockUntilCancelled;
        }

        public int Waiters { get; private set; }

        public bool CancellationObserved { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Waiters++;
            if (!blockUntilCancelled)
            {
                return next.Task.WaitAsync(cancellationToken);
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        public void Release()
        {
            next.TrySetResult(null);
            next = CreateSource();
        }

        private async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
            }
        }

        private static TaskCompletionSource<object?> CreateSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestDiagnosticsApiClient : IMediaDiagnosticsApiClient
    {
        private readonly ConcurrentQueue<object> responses = new();
        private readonly TaskCompletionSource<object?> blockedGet =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> secondGetStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> refreshStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> refreshCancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> blockedRefresh =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeGetCalls;
        private bool blockRefresh;

        public TestDiagnosticsApiClient(params object[] responses)
        {
            foreach (var response in responses)
            {
                this.responses.Enqueue(response);
            }
        }

        public int GetCalls { get; private set; }

        public int MaximumConcurrentGetCalls { get; private set; }

        public int MessageBoxCalls { get; } = 0;

        public TaskCompletionSource<object?> SecondGetStarted => secondGetStarted;

        public TaskCompletionSource<object?> RefreshStarted => refreshStarted;

        public TaskCompletionSource<object?> RefreshCancellationObserved =>
            refreshCancellationObserved;

        public ConcurrentQueue<string> Events { get; } = new();

        public (Guid DeviceId, Guid ChannelId, StreamType StreamType)? LastRetry { get; private set; }

        public Task<MediaDiagnosticsSnapshotDto> GetDiagnosticsAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            Events.Enqueue("GET");
            GetCalls++;
            var active = Interlocked.Increment(ref activeGetCalls);
            MaximumConcurrentGetCalls = Math.Max(MaximumConcurrentGetCalls, active);
            if (GetCalls == 2)
            {
                secondGetStarted.TrySetResult(null);
            }

            return CompleteGetAsync(active, cancellationToken);
        }

        public Task RequestRefreshAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            Events.Enqueue("POST");
            return blockRefresh
                ? BlockRefreshAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task RetryFaultedAsync(
            Uri baseUri,
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            LastRetry = (deviceId, channelId, streamType);
            return Task.CompletedTask;
        }

        public void BlockNextGet() => responses.Enqueue(blockedGet);

        public void ReleaseBlockedGet() => blockedGet.TrySetResult(null);

        public void BlockNextRefresh() => blockRefresh = true;

        public void ReleaseBlockedRefresh() => blockedRefresh.TrySetResult(null);

        private async Task BlockRefreshAsync(CancellationToken cancellationToken)
        {
            refreshStarted.TrySetResult(null);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                refreshCancellationObserved.TrySetResult(null);
                await blockedRefresh.Task;
                throw;
            }
        }

        private async Task<MediaDiagnosticsSnapshotDto> CompleteGetAsync(
            int active,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = responses.TryDequeue(out var queued)
                    ? queued
                    : throw new InvalidOperationException("No diagnostics response queued.");
                if (response is TaskCompletionSource<object?> block)
                {
                    await block.Task.WaitAsync(cancellationToken);
                    response = new MediaDiagnosticsSnapshotDto(
                        MediaServerHealth.Healthy,
                        2,
                        3,
                        0,
                        []);
                }

                if (response is Exception exception)
                {
                    throw exception;
                }

                return (MediaDiagnosticsSnapshotDto)response;
            }
            finally
            {
                Interlocked.Decrement(ref activeGetCalls);
            }
        }
    }

    private sealed class BlockingMediaSettingsApiClient : IMediaSettingsApiClient
    {
        private readonly TaskCompletionSource<object?> loadCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> LoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MediaSettingsDto> GetAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            LoadStarted.TrySetResult(null);
            return CompleteLoadAsync(cancellationToken);
        }

        public Task<MediaSettingsDto> UpdateAsync(
            Uri baseUri,
            UpdateMediaSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaSettingsTestResult> TestAsync(
            Uri baseUri,
            TestMediaSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void ReleaseLoad() => loadCompletion.TrySetResult(null);

        private async Task<MediaSettingsDto> CompleteLoadAsync(
            CancellationToken cancellationToken)
        {
            await loadCompletion.Task.WaitAsync(cancellationToken);
            return new MediaSettingsDto(
                "",
                "",
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                false,
                30,
                1);
        }
    }
}
