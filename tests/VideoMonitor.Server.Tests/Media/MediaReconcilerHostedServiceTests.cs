using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaReconcilerHostedServiceTests
{
    [Fact]
    public async Task StartupAndRecoveryReconcileDoNotOverlap()
    {
        using var cancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var calls = 0;
        var contributor = new DelegateContributor(async token =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximumActive, current);
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstStarted.SetResult(null);
                await releaseFirst.Task.WaitAsync(token);
            }
            else
            {
                secondStarted.SetResult(null);
            }

            Interlocked.Decrement(ref active);
        });
        var service = CreateService(contributor, (_, _) => Task.CompletedTask);
        var run = service.RunAsync(cancellation.Token);

        await firstStarted.Task;
        service.TriggerRecovery();
        releaseFirst.SetResult(null);
        await secondStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task UnavailableServerUsesBoundedBackoff()
    {
        using var cancellation = new CancellationTokenSource();
        var health = new MediaServerHealthState();
        var delays = new List<TimeSpan>();
        var contributor = new DelegateContributor(_ =>
        {
            health.MarkUnavailable();
            return Task.CompletedTask;
        });
        var service = new MediaReconcilerHostedService(
            new[] { contributor },
            health,
            new FakeRuntimeSettingsProvider(),
            (delay, token) =>
            {
                delays.Add(delay);
                if (delays.Count == 4)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled(token);
                }

                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(cancellation.Token));

        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60)
            },
            delays);
    }

    [Fact]
    public async Task NoReaderUsesConfiguredGracePeriod()
    {
        var delays = new List<TimeSpan>();
        var service = new MediaReconcilerHostedService(
            Array.Empty<IMediaReconcileContributor>(),
            new MediaServerHealthState(),
            new FakeRuntimeSettingsProvider(),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await service.WaitForNoReaderGraceAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, delays);
    }

    private static MediaReconcilerHostedService CreateService(
        IMediaReconcileContributor contributor,
        Func<TimeSpan, CancellationToken, Task> delay) =>
        new(
            new[] { contributor },
            new MediaServerHealthState(),
            new FakeRuntimeSettingsProvider(),
            delay);

    private sealed class DelegateContributor : IMediaReconcileContributor
    {
        private readonly Func<CancellationToken, Task> action;

        public DelegateContributor(Func<CancellationToken, Task> action)
        {
            this.action = action;
        }

        public Task ReconcileAsync(CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }

    private sealed class FakeRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
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

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref location);
                if (current >= value
                    || Interlocked.CompareExchange(ref location, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
