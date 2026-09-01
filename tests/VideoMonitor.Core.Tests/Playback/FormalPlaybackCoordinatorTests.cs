using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class FormalPlaybackCoordinatorTests
{
    [Fact]
    public async Task TransientFailureUsesBoundedRecoveryDelays()
    {
        var provider = new FakeProvider(
            Enumerable.Repeat<object>(
                    new PlaybackEngineException("transient"),
                    7)
                .Append(Source(1))
                .ToArray());
        var delays = new List<TimeSpan>();
        var coordinator = CreateCoordinator(
            provider,
            (source, _) => new PlaybackSession(
                new PlaybackSource(source.ChannelId, source.StreamId, source.PlaybackUrl, null, false),
                null,
                null),
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);

        Assert.Equal(
            [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30)
            ],
            delays);
        Assert.Equal(8, provider.PrepareCount);
    }

    [Fact]
    public async Task PermanentFailureStopsWithoutRetry()
    {
        var provider = new FakeProvider(
            new CatalogApiException("CATALOG_VALIDATION_FAILED"));
        var delays = new List<TimeSpan>();
        var coordinator = CreateCoordinator(
            provider,
            (_, _) => throw new InvalidOperationException("not expected"),
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);

        Assert.Equal(1, provider.PrepareCount);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task UserStopCancelsRecovery()
    {
        var provider = new FakeProvider(new PlaybackEngineException("transient"));
        var delayStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = CreateCoordinator(
            provider,
            (_, _) => throw new InvalidOperationException("not expected"),
            async (_, cancellationToken) =>
            {
                delayStarted.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        var startTask = coordinator.StartAsync(
            Guid.NewGuid(),
            provider.ChannelId,
            StreamType.Main);

        await delayStarted.Task;
        await coordinator.StopAsync();
        await startTask;

        Assert.Equal(1, provider.PrepareCount);
    }

    [Fact]
    public async Task RetryRequestsFreshTicket()
    {
        var provider = new FakeProvider(
            Source(1),
            Source(2));
        var sources = new List<FormalPlaybackSource>();
        var coordinator = CreateCoordinator(
            provider,
            (source, _) =>
            {
                sources.Add(source);
                if (sources.Count == 1)
                {
                    throw new PlaybackEngineException("transient");
                }

                return new PlaybackSession(
                    new PlaybackSource(source.ChannelId, source.StreamId, source.PlaybackUrl, null, false),
                    null,
                    null);
            },
            (_, _) => Task.CompletedTask);

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Sub);

        Assert.Equal(2, sources.Count);
        Assert.NotEqual(sources[0].TicketExpiresUtc, sources[1].TicketExpiresUtc);
    }

    [Fact]
    public async Task SameKeyStart_DoesNotRestart()
    {
        var deviceId = Guid.NewGuid();
        var provider = new FakeProvider(Source(1));
        var starts = 0;
        var coordinator = CreateCoordinator(
            provider,
            (source, _) =>
            {
                starts++;
                return new PlaybackSession(
                    new PlaybackSource(source.ChannelId, source.StreamId, source.PlaybackUrl, null, false),
                    null,
                    null);
            },
            (_, _) => Task.CompletedTask);

        await coordinator.StartAsync(deviceId, provider.ChannelId, StreamType.Main);
        await coordinator.StartAsync(deviceId, provider.ChannelId, StreamType.Main);

        Assert.Equal(1, starts);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ChangedKey_StopsPreviousBeforeStartingNext()
    {
        var firstDeviceId = Guid.NewGuid();
        var secondDeviceId = Guid.NewGuid();
        var provider = new FakeProvider(Source(1), Source(2));
        var events = new List<string>();
        var coordinator = CreateCoordinator(
            provider,
            (source, _) =>
            {
                events.Add($"start:{source.StreamId}");
                return new PlaybackSession(
                    new PlaybackSource(source.ChannelId, source.StreamId, source.PlaybackUrl, null, false),
                    null,
                    null);
            },
            (_, _) => Task.CompletedTask,
            session =>
            {
                events.Add($"stop:{session.StreamId}");
            });

        await coordinator.StartAsync(firstDeviceId, provider.ChannelId, StreamType.Main);
        await coordinator.StartAsync(secondDeviceId, provider.ChannelId, StreamType.Sub);

        Assert.Equal(["start:stream-1", "stop:stream-1", "start:stream-2"], events);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task SevenConsumersHaveIndependentSessions_WhenOneFails()
    {
        var sessions = new List<PlaybackSession>();
        var coordinators = Enumerable.Range(0, 7)
            .Select(index =>
            {
                var provider = index == 3
                    ? new FakeProvider(new CatalogApiException("CATALOG_VALIDATION_FAILED"))
                    : new FakeProvider(Source(index));
                return CreateCoordinator(
                    provider,
                    (source, _) =>
                    {
                        var session = new PlaybackSession(
                            new PlaybackSource(
                                source.ChannelId,
                                source.StreamId,
                                source.PlaybackUrl,
                                null,
                                false),
                            null,
                            null);
                        sessions.Add(session);
                        return session;
                    },
                    (_, _) => Task.CompletedTask);
            })
            .ToArray();

        var startTasks = coordinators
            .Select((coordinator, index) => coordinator.StartAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                index % 2 == 0 ? StreamType.Main : StreamType.Sub))
            .ToArray();
        await Task.WhenAll(startTasks);

        Assert.Equal(6, sessions.Count);
        Assert.Equal(7, coordinators.Length);
        foreach (var coordinator in coordinators)
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task RuntimeFailureStartsFreshReader()
    {
        var secondStart = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(Source(1), Source(2));
        var starts = 0;
        var coordinator = CreateCoordinator(
            provider,
            (source, _) =>
            {
                starts++;
                if (starts == 2)
                {
                    secondStart.SetResult(true);
                }

                return new PlaybackSession(
                    new PlaybackSource(source.ChannelId, source.StreamId, source.PlaybackUrl, null, false),
                    null,
                    null);
            },
            (_, _) => Task.CompletedTask);

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);
        coordinator.Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
        await secondStart.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, starts);
        Assert.Equal(2, provider.PrepareCount);
        await coordinator.DisposeAsync();
    }

    private static FormalPlaybackCoordinator CreateCoordinator(
        FakeProvider provider,
        Func<FormalPlaybackSource, IPlaybackRuntimeEventSink, PlaybackSession> start,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<PlaybackSession>? stop = null)
    {
        stop ??= session => session.Dispose();
        return new FormalPlaybackCoordinator(
            provider,
            start,
            stop,
            new VideoTileViewModel(),
            new ImmediateDispatcher(),
            (duration, cancellationToken) => delay(duration, cancellationToken));
    }

    private static FormalPlaybackSource Source(int offset) => new(
        Guid.NewGuid(),
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        $"stream-{offset}",
        new Uri($"https://server-b/live/{offset}"),
        DateTimeOffset.UtcNow.AddSeconds(offset));

    private sealed class FakeProvider : IFormalPlaybackSourceProvider
    {
        private readonly Queue<object> results;

        public FakeProvider(params object[] results)
        {
            this.results = new Queue<object>(results);
        }

        public Guid ChannelId { get; } =
            Guid.Parse("60000000-0000-0000-0000-000000000001");

        public int PrepareCount { get; private set; }

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            var result = results.Count == 0
                ? Source(PrepareCount)
                : results.Dequeue();
            return result switch
            {
                Exception exception => Task.FromException<FormalPlaybackSource>(exception),
                FormalPlaybackSource source => Task.FromResult(source with { DeviceId = deviceId }),
                _ => throw new InvalidOperationException()
            };
        }

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
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
}
