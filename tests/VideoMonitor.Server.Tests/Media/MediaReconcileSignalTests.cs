using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaReconcileSignalTests
{
    [Fact]
    public void SignalBeforeStartIsUnavailable()
    {
        var fixture = CreateFixture();

        var result = Signal(fixture.Service);

        Assert.Equal(ReconcileSignalResult.Unavailable, result);
        Assert.Equal(0, fixture.Contributor.Calls);
    }

    [Fact]
    public async Task RefreshOnlySignalsSerializedReconciler()
    {
        var fixture = CreateFixture();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.Contributor.WaitForCallAsync(1);

        var result = Signal(fixture.Service);

        Assert.Equal(ReconcileSignalResult.Accepted, result);
        Assert.Equal(1, fixture.Contributor.Active);
        Assert.Equal(1, fixture.Contributor.MaximumActive);
        Assert.Equal(1, fixture.Contributor.Calls);

        fixture.Contributor.Release(1);
        await fixture.Contributor.WaitForCallAsync(2);

        Assert.Equal(1, fixture.Contributor.MaximumActive);
        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedRefreshIsCoalesced()
    {
        var fixture = CreateFixture();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.Contributor.WaitForCallAsync(1);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(ReconcileSignalResult.Accepted, Signal(fixture.Service));
        }

        fixture.Contributor.Release(1);
        await fixture.Contributor.WaitForCallAsync(2);
        fixture.Contributor.Release(2);
        await fixture.Contributor.WaitForCompletionAsync(2);

        Assert.Equal(2, fixture.Contributor.Calls);
        Assert.Equal(1, fixture.Contributor.MaximumActive);
        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedRefreshDoesNotCreateParallelReconcile()
    {
        var fixture = CreateFixture();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.Contributor.WaitForCallAsync(1);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(ReconcileSignalResult.Accepted, Signal(fixture.Service));
        }

        fixture.Contributor.Release(1);
        await fixture.Contributor.WaitForCallAsync(2);

        Assert.Equal(1, fixture.Contributor.Active);
        Assert.Equal(1, fixture.Contributor.MaximumActive);

        fixture.Contributor.Release(2);
        await fixture.Contributor.WaitForCompletionAsync(2);
        Assert.Equal(1, fixture.Contributor.MaximumActive);
        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SignalAfterStopIsUnavailable()
    {
        var fixture = CreateFixture();

        await fixture.Service.StartAsync(CancellationToken.None);
        await fixture.Contributor.WaitForCallAsync(1);
        await fixture.Service.StopAsync(CancellationToken.None);

        var result = Signal(fixture.Service);

        Assert.Equal(ReconcileSignalResult.Unavailable, result);
        Assert.Equal(1, fixture.Contributor.Calls);
        Assert.Equal(1, fixture.Contributor.MaximumActive);
    }

    private static ReconcileSignalResult Signal(
        MediaReconcilerHostedService service) =>
        ((IMediaReconcileSignal)service).TryRequestRecovery();

    private static Fixture CreateFixture()
    {
        var contributor = new BlockingReconcileContributor();
        var service = new MediaReconcilerHostedService(
            new[] { contributor },
            new MediaServerHealthState(),
            new TestRuntimeSettingsProvider(),
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        return new Fixture(service, contributor);
    }

    private sealed record Fixture(
        MediaReconcilerHostedService Service,
        BlockingReconcileContributor Contributor);

    private sealed class BlockingReconcileContributor
        : IMediaReconcileContributor
    {
        private readonly object sync = new();
        private readonly Dictionary<int, TaskCompletionSource<object?>> started = new();
        private readonly Dictionary<int, TaskCompletionSource<object?>> released = new();
        private readonly Dictionary<int, TaskCompletionSource<object?>> completed = new();
        private int calls;
        private int active;
        private int maximumActive;

        public int Calls => Volatile.Read(ref calls);

        public int Active => Volatile.Read(ref active);

        public int MaximumActive => Volatile.Read(ref maximumActive);

        public async Task ReconcileAsync(
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(current);
            var call = Interlocked.Increment(ref calls);
            Get(started, call).TrySetResult(null);

            try
            {
                await Get(released, call).Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref active);
                Get(completed, call).TrySetResult(null);
            }
        }

        public Task WaitForCallAsync(int call) =>
            Get(started, call).Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForCompletionAsync(int call) =>
            Get(completed, call).Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release(int call) =>
            Get(released, call).TrySetResult(null);

        private TaskCompletionSource<object?> Get(
            Dictionary<int, TaskCompletionSource<object?>> values,
            int call)
        {
            lock (sync)
            {
                if (!values.TryGetValue(call, out var value))
                {
                    value = new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    values.Add(call, value);
                }

                return value;
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumActive);
                if (current >= value
                    || Interlocked.CompareExchange(
                        ref maximumActive,
                        value,
                        current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class TestRuntimeSettingsProvider
        : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://127.0.0.1:8080",
                "",
                "configured-vhost",
                "videomonitor",
                "videomonitor-test",
                "",
                30,
                1));
    }
}
