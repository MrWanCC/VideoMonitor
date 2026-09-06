using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class MediaRuntimeStatusCoordinatorTests
{
    private static readonly Uri ServerUri = new("https://server.example/");

    [Fact]
    public async Task CoordinatorInitialFetchPublishesSnapshot()
    {
        var api = new TestDiagnosticsApiClient(Snapshot());
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = CreateCoordinator(api, store);

        await coordinator.StartAsync();

        Assert.Equal(1, api.GetCalls);
        Assert.Equal(MediaServerHealth.Healthy, store.Snapshot.ServerHealth);
        Assert.Single(store.Snapshot.Streams);
    }

    [Fact]
    public async Task CoordinatorPollDoesNotOverlap()
    {
        var api = new TestDiagnosticsApiClient(Snapshot()) { BlockSecondGet = true };
        var delay = new ManualDelay();
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = CreateCoordinator(api, store, delay.DelayAsync);

        await coordinator.StartAsync();
        delay.ReleaseNext();
        await api.SecondGetStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, api.MaximumConcurrentGetCalls);

        api.ReleaseBlockedGet();
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task RepeatedStartDoesNotCreateSecondLoop()
    {
        var api = new TestDiagnosticsApiClient(Snapshot());
        var delay = new ManualDelay();
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = CreateCoordinator(api, store, delay.DelayAsync);

        await Task.WhenAll(coordinator.StartAsync(), coordinator.StartAsync());

        Assert.Equal(1, api.GetCalls);
        Assert.Equal(1, delay.Waiters);

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task CoordinatorStopCancelsPolling()
    {
        var api = new TestDiagnosticsApiClient(Snapshot());
        var delay = new BlockingDelay();
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = CreateCoordinator(api, store, delay.DelayAsync);

        await coordinator.StartAsync();
        await coordinator.StopAsync();

        Assert.True(delay.CancellationObserved);
    }

    [Fact]
    public async Task CoordinatorFailurePublishesUnavailable()
    {
        var api = new TestDiagnosticsApiClient(
            new CatalogApiException("CATALOG_UNAVAILABLE"));
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = CreateCoordinator(api, store);

        await coordinator.StartAsync();

        Assert.Equal(MediaServerHealth.Unavailable, store.Snapshot.ServerHealth);
        Assert.Empty(store.Snapshot.Streams);
    }

    [Fact]
    public async Task CoordinatorRecoversAfterNextSuccess()
    {
        var api = new TestDiagnosticsApiClient(
            new CatalogApiException("CATALOG_UNAVAILABLE"),
            Snapshot());
        var delay = new ManualDelay();
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = CreateCoordinator(api, store, delay.DelayAsync);

        await coordinator.StartAsync();
        Assert.Equal(MediaServerHealth.Unavailable, store.Snapshot.ServerHealth);

        delay.ReleaseNext();
        await api.SecondGetCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(MediaServerHealth.Healthy, store.Snapshot.ServerHealth);
        Assert.Single(store.Snapshot.Streams);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task CoordinatorInvalidEndpointPublishesUnavailable()
    {
        var api = new TestDiagnosticsApiClient(Snapshot());
        var store = new MediaRuntimeStatusStore();
        await using var coordinator = new MediaRuntimeStatusCoordinator(
            api,
            store,
            () => new Uri("not-a-valid-endpoint", UriKind.Relative),
            new InlineDispatcher());

        await coordinator.StartAsync();

        Assert.Equal(MediaServerHealth.Unavailable, store.Snapshot.ServerHealth);
        Assert.Equal(0, api.GetCalls);
    }

    private static MediaRuntimeStatusCoordinator CreateCoordinator(
        TestDiagnosticsApiClient api,
        MediaRuntimeStatusStore store,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null) =>
        new(
            api,
            store,
            () => ServerUri,
            new InlineDispatcher(),
            delayAsync);

    private static MediaDiagnosticsSnapshotDto Snapshot() =>
        new(
            MediaServerHealth.Healthy,
            1,
            1,
            0,
            [new MediaStreamDiagnosticsDto(
                Guid.Parse("a1000000-0000-0000-0000-000000000001"),
                Guid.Parse("b1000000-0000-0000-0000-000000000001"),
                StreamType.Main,
                StreamRuntimeState.Ready,
                1,
                StreamOwnership.OwnedCurrentProcess,
                DateTimeOffset.UtcNow,
                SourceObservation.Reachable,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null,
                false)]);

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class ManualDelay
    {
        private readonly object gate = new();
        private readonly Queue<TaskCompletionSource<bool>> waiters = new();

        public int Waiters
        {
            get
            {
                lock (gate)
                {
                    return waiters.Count;
                }
            }
        }

        public void ReleaseNext()
        {
            TaskCompletionSource<bool>? waiter = null;
            lock (gate)
            {
                if (waiters.Count > 0)
                {
                    waiter = waiters.Dequeue();
                }
            }

            waiter?.TrySetResult(true);
        }

        public Task DelayAsync(TimeSpan _, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                waiters.Enqueue(source);
            }

            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            return source.Task;
        }
    }

    private sealed class BlockingDelay
    {
        public bool CancellationObserved { get; private set; }

        public async Task DelayAsync(
            TimeSpan _,
            CancellationToken cancellationToken)
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
    }

    private sealed class TestDiagnosticsApiClient : IMediaDiagnosticsApiClient
    {
        private readonly Queue<object> responses;
        private readonly TaskCompletionSource<bool> secondGetStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> secondGetCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> blockedGetRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeGetCalls;
        private int maximumConcurrentGetCalls;

        public TestDiagnosticsApiClient(params object[] responses)
        {
            this.responses = new Queue<object>(responses);
        }

        public bool BlockSecondGet { get; init; }

        private int getCalls;

        public int GetCalls => Volatile.Read(ref getCalls);

        public int MaximumConcurrentGetCalls =>
            Volatile.Read(ref maximumConcurrentGetCalls);

        public TaskCompletionSource<bool> SecondGetStarted => secondGetStarted;

        public TaskCompletionSource<bool> SecondGetCompleted => secondGetCompleted;

        public Task<MediaDiagnosticsSnapshotDto> GetDiagnosticsAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            GetCoreAsync(cancellationToken);

        public Task RequestRefreshAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RetryFaultedAsync(
            Uri baseUri,
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ReleaseBlockedGet() => blockedGetRelease.TrySetResult(true);

        private async Task<MediaDiagnosticsSnapshotDto> GetCoreAsync(
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref getCalls);
            var active = Interlocked.Increment(ref activeGetCalls);
            UpdateMaximum(active);
            try
            {
                if (call == 2)
                {
                    secondGetStarted.TrySetResult(true);
                    if (BlockSecondGet)
                    {
                        await blockedGetRelease.Task.WaitAsync(cancellationToken);
                    }

                    secondGetCompleted.TrySetResult(true);
                }

                var response = responses.Count > 0
                    ? responses.Dequeue()
                    : Snapshot();
                return response switch
                {
                    MediaDiagnosticsSnapshotDto snapshot => snapshot,
                    Exception exception => throw exception,
                    _ => throw new InvalidOperationException("Unexpected test response.")
                };
            }
            finally
            {
                Interlocked.Decrement(ref activeGetCalls);
            }
        }

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumConcurrentGetCalls);
                if (current >= active
                    || Interlocked.CompareExchange(
                        ref maximumConcurrentGetCalls,
                        active,
                        current) == current)
                {
                    return;
                }
            }
        }
    }
}
