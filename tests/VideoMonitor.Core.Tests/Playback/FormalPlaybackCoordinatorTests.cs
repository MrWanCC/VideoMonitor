using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class FormalPlaybackCoordinatorTests
{
    [Fact]
    public async Task PlayHappensAfterSessionIsAttachedWhileTileIsLoading()
    {
        var provider = new FakeProvider(Source(1));
        var tile = new VideoTileViewModel();
        PlaybackSession? preparedSession = null;
        var playObserved = false;
        await using var coordinator = new FormalPlaybackCoordinator(
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
                preparedSession = session;
                return session;
            },
            session => session.Dispose(),
            tile,
            new ImmediateDispatcher(),
            playPlayback: session =>
            {
                playObserved = true;
                Assert.Same(preparedSession, session);
                Assert.Same(session, tile.PlaybackSession);
                Assert.Equal(PlaybackState.Loading, tile.PlaybackState);
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);

        Assert.True(playObserved);
        Assert.Same(preparedSession, tile.PlaybackSession);
        Assert.Equal(PlaybackState.Loading, tile.PlaybackState);
    }

    [Fact]
    public async Task StopDuringAttachDoesNotPlayInactiveSession()
    {
        var provider = new FakeProvider(Source(1));
        var dispatcher = new BlockingDispatcher();
        var playCalls = 0;
        await using var coordinator = new FormalPlaybackCoordinator(
            provider,
            (source, _) => new PlaybackSession(
                new PlaybackSource(
                    source.ChannelId,
                    source.StreamId,
                    source.PlaybackUrl,
                    null,
                    false),
                null,
                null),
            session => session.Dispose(),
            new VideoTileViewModel(),
            dispatcher,
            playPlayback: _ => playCalls++);

        var startTask = coordinator.StartAsync(
            Guid.NewGuid(),
            provider.ChannelId,
            StreamType.Main);
        await dispatcher.AttachStarted.Task;

        var stopTask = coordinator.StopAsync();
        dispatcher.ReleaseAttach();
        await Task.WhenAll(startTask, stopTask);

        Assert.Equal(0, playCalls);
    }

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

    [Fact]
    public async Task RapidSwitchesOnlyLatestRequestCanCommitSession()
    {
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var deviceC = Guid.NewGuid();
        var provider = new ControlledProvider(
            [deviceA, deviceB, deviceC],
            blockAll: true);
        var createdStreams = new List<string>();
        var stoppedStreams = new List<string>();
        await using var coordinator = CreateCoordinator(
            provider,
            (source, _) =>
            {
                lock (createdStreams)
                {
                    createdStreams.Add(source.StreamId);
                }
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            },
            stop: session =>
            {
                lock (stoppedStreams)
                {
                    stoppedStreams.Add(session.StreamId);
                }

                session.Dispose();
            });

        var startA = coordinator.StartAsync(deviceA, provider.ChannelId, StreamType.Main);
        await provider.WaitForPrepareAsync(deviceA);
        var startB = coordinator.StartAsync(deviceB, provider.ChannelId, StreamType.Main);
        var startC = coordinator.StartAsync(deviceC, provider.ChannelId, StreamType.Main);

        provider.Release(deviceA);
        await provider.WaitForPrepareAsync(deviceC);
        provider.Release(deviceC);
        await startC;

        var bPrepare = provider.WaitForPrepareAsync(deviceB);
        var bStarted = await Task.WhenAny(
            bPrepare,
            Task.Delay(TimeSpan.FromMilliseconds(500)));
        if (ReferenceEquals(bStarted, bPrepare))
        {
            provider.Release(deviceB);
        }

        await Task.WhenAll(startA, startB, startC);

        Assert.Equal("stream-" + deviceC.ToString("N"), coordinator.CurrentSource?.StreamId);
        lock (createdStreams)
        {
            Assert.DoesNotContain("stream-" + deviceB.ToString("N"), createdStreams);
        }

        if (provider.PreparedDeviceIds.Contains(deviceB))
        {
            Assert.Contains(
                provider.ReleasedSources,
                source => source.DeviceId == deviceB);
            lock (stoppedStreams)
            {
                Assert.Contains(
                    "stream-" + deviceB.ToString("N"),
                    stoppedStreams);
            }
        }
    }

    [Fact]
    public async Task StopInvalidatesQueuedStartBeforePreviousAttemptCompletes()
    {
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var provider = new ControlledProvider(
            [deviceA, deviceB],
            blockAll: false,
            blockedDevices: [deviceA]);
        await using var coordinator = CreateCoordinator(
            provider,
            (source, _) => new PlaybackSession(
                new PlaybackSource(
                    source.ChannelId,
                    source.StreamId,
                    source.PlaybackUrl,
                    null,
                    false),
                null,
                null),
            stop: session => session.Dispose());

        var startA = coordinator.StartAsync(deviceA, provider.ChannelId, StreamType.Main);
        await provider.WaitForPrepareAsync(deviceA);
        var startB = coordinator.StartAsync(deviceB, provider.ChannelId, StreamType.Main);
        var stop = coordinator.StopAsync();

        provider.Release(deviceA);
        await Task.WhenAll(startA, startB, stop);

        Assert.DoesNotContain(deviceB, provider.PreparedDeviceIds);
        Assert.Null(coordinator.CurrentSession);
    }

    [Fact]
    public async Task StaleRuntimeEventFromPreviousSessionCannotRestartCurrentSession()
    {
        var provider = new FakeProvider(Source(1), Source(2));
        var sinks = new List<IPlaybackRuntimeEventSink>();
        var startCount = 0;
        await using var coordinator = CreateCoordinator(
            provider,
            (source, sink) =>
            {
                sinks.Add(sink);
                startCount++;
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);
        coordinator.Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
        await WaitForConditionAsync(() => startCount == 2);

        sinks[0].Publish(PlaybackRuntimeEvent.ForStopped(provider.ChannelId));
        await Task.Delay(50);

        Assert.Equal(2, provider.PrepareCount);
        Assert.Equal("stream-2", coordinator.CurrentSource?.StreamId);
        Assert.NotNull(coordinator.CurrentSession);
    }

    [Fact]
    public async Task DuplicateOldRuntimeEventsDoNotKillFreshSession()
    {
        var provider = new FakeProvider(Source(1), Source(2));
        var sinks = new List<IPlaybackRuntimeEventSink>();
        var startCount = 0;
        await using var coordinator = CreateCoordinator(
            provider,
            (source, sink) =>
            {
                sinks.Add(sink);
                startCount++;
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);
        sinks[0].Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
        sinks[0].Publish(PlaybackRuntimeEvent.ForStopped(provider.ChannelId));

        await WaitForConditionAsync(() => startCount == 2);
        await Task.Delay(50);

        Assert.Equal(2, provider.PrepareCount);
        Assert.Equal("stream-2", coordinator.CurrentSource?.StreamId);
        Assert.NotNull(coordinator.CurrentSession);
    }

    [Fact]
    public async Task RepeatedRuntimeFailuresUseBoundedRecoveryDelays()
    {
        var provider = new FakeProvider(
            Enumerable.Range(1, 8).Select(Source).ToArray());
        var sinks = new List<IPlaybackRuntimeEventSink>();
        var sources = new List<FormalPlaybackSource>();
        var delays = new List<TimeSpan>();
        var startCount = 0;
        await using var coordinator = CreateCoordinator(
            provider,
            (source, sink) =>
            {
                sinks.Add(sink);
                sources.Add(source);
                startCount++;
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            },
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);
        for (var failure = 0; failure < 7; failure++)
        {
            sinks[^1].Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
            await WaitForConditionAsync(() => startCount == failure + 2);
        }

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
        Assert.Equal(8, sources.Select(source => source.StreamId).Distinct().Count());
        Assert.Equal(8, sources.Select(source => source.TicketExpiresUtc).Distinct().Count());
    }

    [Fact]
    public async Task PlayingEventResetsRuntimeRecoveryBackoff()
    {
        var provider = new FakeProvider(Source(1), Source(2), Source(3));
        var sinks = new List<IPlaybackRuntimeEventSink>();
        var delays = new List<TimeSpan>();
        var startCount = 0;
        await using var coordinator = CreateCoordinator(
            provider,
            (source, sink) =>
            {
                sinks.Add(sink);
                startCount++;
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            },
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);
        sinks[0].Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
        await WaitForConditionAsync(() => startCount == 2);

        sinks[1].Publish(PlaybackRuntimeEvent.ForPlaying(provider.ChannelId));
        await Task.Delay(20);
        sinks[1].Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
        await WaitForConditionAsync(() => startCount == 3);

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)],
            delays);
    }

    [Fact]
    public async Task RuntimeRecoveryCannotRestartAfterUserStop()
    {
        var provider = new FakeProvider(Source(1), Source(2));
        var delayStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sinks = new List<IPlaybackRuntimeEventSink>();
        var starts = 0;
        await using var coordinator = CreateCoordinator(
            provider,
            (source, sink) =>
            {
                sinks.Add(sink);
                starts++;
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            },
            async (_, _) =>
            {
                delayStarted.TrySetResult(true);
                await releaseDelay.Task;
            });

        await coordinator.StartAsync(Guid.NewGuid(), provider.ChannelId, StreamType.Main);
        sinks[0].Publish(PlaybackRuntimeEvent.ForFailed(provider.ChannelId));
        await delayStarted.Task;

        await coordinator.StopAsync();
        releaseDelay.TrySetResult(true);
        await Task.Delay(50);

        Assert.Equal(1, starts);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Null(coordinator.CurrentSession);
    }

    [Fact]
    public async Task SameKeyCanRetryAfterPermanentFailureWhenStartedAgain()
    {
        var deviceId = Guid.NewGuid();
        var provider = new FakeProvider(
            new CatalogApiException("CATALOG_VALIDATION_FAILED"),
            Source(1));
        var starts = 0;
        await using var coordinator = CreateCoordinator(
            provider,
            (source, _) =>
            {
                starts++;
                return new PlaybackSession(
                    new PlaybackSource(
                        source.ChannelId,
                        source.StreamId,
                        source.PlaybackUrl,
                        null,
                        false),
                    null,
                    null);
            });

        await coordinator.StartAsync(deviceId, provider.ChannelId, StreamType.Main);
        await coordinator.StartAsync(deviceId, provider.ChannelId, StreamType.Main);

        Assert.Equal(2, provider.PrepareCount);
        Assert.Equal(1, starts);
        Assert.NotNull(coordinator.CurrentSession);
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static FormalPlaybackCoordinator CreateCoordinator(
        IFormalPlaybackSourceProvider provider,
        Func<FormalPlaybackSource, IPlaybackRuntimeEventSink, PlaybackSession> start,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<PlaybackSession>? stop = null)
    {
        stop ??= session => session.Dispose();
        return new FormalPlaybackCoordinator(
            provider,
            start,
            stop,
            new VideoTileViewModel(),
            new ImmediateDispatcher(),
            (duration, cancellationToken) =>
                (delay ?? ((_, _) => Task.CompletedTask))(
                    duration,
                    cancellationToken));
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

    private sealed class ControlledProvider : IFormalPlaybackSourceProvider
    {
        private readonly Dictionary<Guid, TaskCompletionSource<FormalPlaybackSource>> completions;
        private readonly HashSet<Guid> blockedDevices;
        private readonly Dictionary<Guid, TaskCompletionSource<bool>> started = [];
        private readonly object startedGate = new();

        public ControlledProvider(
            IEnumerable<Guid> devices,
            bool blockAll,
            IEnumerable<Guid>? blockedDevices = null)
        {
            var requestedBlockedDevices = blockedDevices?.ToHashSet() ?? [];
            this.blockedDevices = blockAll
                ? devices.ToHashSet()
                : requestedBlockedDevices;
            completions = devices.ToDictionary(
                deviceId => deviceId,
                _ => new TaskCompletionSource<FormalPlaybackSource>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
        }

        public Guid ChannelId { get; } = Guid.NewGuid();

        public List<Guid> PreparedDeviceIds { get; } = [];

        public List<FormalPlaybackSource> ReleasedSources { get; } = [];

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            lock (PreparedDeviceIds)
            {
                PreparedDeviceIds.Add(deviceId);
            }

            GetStarted(deviceId).TrySetResult(true);

            if (blockedDevices.Contains(deviceId))
            {
                return AwaitBlockedPrepareAsync(deviceId, deviceId, channelId);
            }

            return Task.FromResult(Source(deviceId, channelId));
        }

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default)
        {
            lock (ReleasedSources)
            {
                ReleasedSources.Add(source);
            }

            return Task.CompletedTask;
        }

        public Task WaitForPrepareAsync(Guid deviceId) =>
            GetStarted(deviceId).Task;

        public void Release(Guid deviceId) =>
            completions[deviceId].TrySetResult(Source(deviceId, ChannelId));

        private TaskCompletionSource<bool> GetStarted(Guid deviceId)
        {
            lock (startedGate)
            {
                if (!started.TryGetValue(deviceId, out var completion))
                {
                    completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    started.Add(deviceId, completion);
                }

                return completion;
            }
        }

        private async Task<FormalPlaybackSource> AwaitBlockedPrepareAsync(
            Guid completionDeviceId,
            Guid sourceDeviceId,
            Guid channelId)
        {
            var source = await completions[completionDeviceId].Task.ConfigureAwait(false);
            return source with
            {
                DeviceId = sourceDeviceId,
                ChannelId = channelId
            };
        }

        private static FormalPlaybackSource Source(Guid deviceId, Guid channelId) => new(
            deviceId,
            channelId,
            "stream-" + deviceId.ToString("N"),
            new Uri("https://server-b/live/" + deviceId.ToString("N")),
            DateTimeOffset.UtcNow.AddMinutes(1));
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

    private sealed class BlockingDispatcher : IUiDispatcher
    {
        private int invocationCount;
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> AttachStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            if (Interlocked.Increment(ref invocationCount) != 2)
            {
                return Task.CompletedTask;
            }

            AttachStarted.TrySetResult(true);
            return release.Task;
        }

        public void ReleaseAttach() => release.TrySetResult(true);
    }
}
