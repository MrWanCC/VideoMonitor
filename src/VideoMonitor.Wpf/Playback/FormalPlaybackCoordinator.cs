using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Playback;

public interface IFormalPlaybackEngine
{
    PlaybackSession Prepare(
        FormalPlaybackSource source,
        IPlaybackRuntimeEventSink eventSink);

    void Play(PlaybackSession session);

    void Stop(PlaybackSession session);
}

public sealed class FormalPlaybackCoordinator : IAsyncDisposable, IPlaybackRuntimeEventSink
{
    private static readonly TimeSpan[] RecoveryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];

    private readonly IFormalPlaybackSourceProvider sourceProvider;
    private readonly Func<FormalPlaybackSource, IPlaybackRuntimeEventSink, PlaybackSession> preparePlayback;
    private readonly Action<PlaybackSession> playPlayback;
    private readonly Action<PlaybackSession> stopPlayback;
    private readonly VideoTileViewModel tile;
    private readonly IUiDispatcher dispatcher;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly object stateGate = new();
    private readonly SemaphoreSlim runtimeRecoveryGate = new(1, 1);
    private CancellationTokenSource? operationCancellation;
    private CancellationTokenSource? runtimeRecoveryCancellation;
    private Task? operationTask;
    private PlaybackSession? currentSession;
    private FormalPlaybackSource? currentSource;
    private PlaybackKey? currentKey;
    private SessionPlaybackRuntimeEventSink? currentRuntimeSink;
    private long currentGeneration;
    private int runtimeRecoveryIndex;
    private bool disposed;

    public FormalPlaybackCoordinator(
        IFormalPlaybackSourceProvider sourceProvider,
        Func<FormalPlaybackSource, IPlaybackRuntimeEventSink, PlaybackSession> preparePlayback,
        Action<PlaybackSession> stopPlayback,
        VideoTileViewModel tile,
        IUiDispatcher dispatcher,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<PlaybackSession>? playPlayback = null)
    {
        this.sourceProvider = sourceProvider
            ?? throw new ArgumentNullException(nameof(sourceProvider));
        this.preparePlayback = preparePlayback
            ?? throw new ArgumentNullException(nameof(preparePlayback));
        this.stopPlayback = stopPlayback
            ?? throw new ArgumentNullException(nameof(stopPlayback));
        this.tile = tile ?? throw new ArgumentNullException(nameof(tile));
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        this.delay = delay ?? Task.Delay;
        this.playPlayback = playPlayback ?? (_ => { });
    }

    public PlaybackSession? CurrentSession
    {
        get
        {
            lock (stateGate)
            {
                return currentSession;
            }
        }
    }

    public FormalPlaybackSource? CurrentSource
    {
        get
        {
            lock (stateGate)
            {
                return currentSource;
            }
        }
    }

    public async Task StartAsync(
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
        CancellationToken cancellationToken = default)
    {
        await StartAsyncCore(
                new PlaybackKey(deviceId, channelId, streamType),
                cancellationToken,
                resetRuntimeRecovery: true,
                cancelRuntimeRecovery: true,
                expectedGeneration: null)
            .ConfigureAwait(false);
    }

    private async Task StartAsyncCore(
        PlaybackKey key,
        CancellationToken cancellationToken,
        bool resetRuntimeRecovery,
        bool cancelRuntimeRecovery,
        long? expectedGeneration)
    {
        Task? previousTask;
        var sameKey = false;
        long generation;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedGeneration is { } requiredGeneration
                && requiredGeneration != currentGeneration)
            {
                return;
            }

            if (currentKey == key
                && (currentSession is not null
                    || operationTask is { IsCompleted: false }))
            {
                previousTask = operationTask;
                sameKey = true;
                generation = currentGeneration;
            }
            else
            {
                generation = ++currentGeneration;
                operationCancellation?.Cancel();
                if (cancelRuntimeRecovery)
                {
                    runtimeRecoveryCancellation?.Cancel();
                }

                if (resetRuntimeRecovery)
                {
                    runtimeRecoveryIndex = 0;
                }

                currentRuntimeSink = null;
                currentKey = key;
                previousTask = operationTask;
            }
        }

        if (sameKey)
        {
            if (previousTask is not null)
            {
                await previousTask.ConfigureAwait(false);
            }

            return;
        }

        if (!IsCurrentRequest(generation, key))
        {
            return;
        }

        if (previousTask is not null)
        {
            await IgnoreCancellationAsync(previousTask).ConfigureAwait(false);
            await StopCurrentSessionAsync(previousTask).ConfigureAwait(false);
        }

        if (!IsCurrentRequest(generation, key))
        {
            return;
        }

        Task task;
        lock (stateGate)
        {
            if (!IsCurrentRequestNoLock(generation, key))
            {
                return;
            }

            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            task = operationTask = RunAsync(
                key,
                generation,
                operationCancellation,
                previousTask: null);
        }

        await task.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (stateGate)
        {
            currentGeneration++;
            operationCancellation?.Cancel();
            runtimeRecoveryCancellation?.Cancel();
            task = operationTask;
            currentKey = null;
            currentRuntimeSink = null;
            runtimeRecoveryIndex = 0;
        }

        if (task is not null)
        {
            await IgnoreCancellationAsync(task).ConfigureAwait(false);
        }

        await StopCurrentSessionAsync(task).ConfigureAwait(false);
    }

    public void Publish(PlaybackRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        SessionPlaybackRuntimeEventSink? sink;
        lock (stateGate)
        {
            sink = currentRuntimeSink;
        }

        if (sink is not null)
        {
            PublishFromAttempt(sink, runtimeEvent);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            currentGeneration++;
            operationCancellation?.Cancel();
            runtimeRecoveryCancellation?.Cancel();
            task = operationTask;
            currentKey = null;
            currentRuntimeSink = null;
            runtimeRecoveryIndex = 0;
        }

        if (task is not null)
        {
            await IgnoreCancellationAsync(task).ConfigureAwait(false);
        }

        await StopCurrentSessionAsync(null).ConfigureAwait(false);

        lock (stateGate)
        {
            operationCancellation?.Dispose();
            operationCancellation = null;
            operationTask = null;
        }
    }

    private async Task RunAsync(
        PlaybackKey key,
        long generation,
        CancellationTokenSource cancellation,
        Task? previousTask)
    {
        if (previousTask is not null)
        {
            await IgnoreCancellationAsync(previousTask).ConfigureAwait(false);
        }

        var cancellationToken = cancellation.Token;
        var retryIndex = 0;
        while (true)
        {
            FormalPlaybackSource? source = null;
            PlaybackSession? session = null;
            SessionPlaybackRuntimeEventSink? runtimeSink = null;
            try
            {
                if (!await InvokeIfCurrentAsync(
                        generation,
                        key,
                        cancellation,
                        tile.ShowLoading,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return;
                }

                source = await sourceProvider
                    .PrepareAsync(
                        key.DeviceId,
                        key.ChannelId,
                        key.StreamType,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!IsCurrentRequest(generation, key, cancellation))
                {
                    await ReleaseSafelyAsync(source).ConfigureAwait(false);
                    return;
                }

                runtimeSink = new SessionPlaybackRuntimeEventSink(this, generation, key);
                session = preparePlayback(source, runtimeSink);
                if (!TryCommitAttempt(generation, key, cancellation, source, session, runtimeSink))
                {
                    await CleanupAttemptAsync(session, source, runtimeSink)
                        .ConfigureAwait(false);
                    return;
                }

                var committedSession = session;
                if (!await InvokeIfCurrentAsync(
                        generation,
                        key,
                        cancellation,
                        () => tile.AttachPreparedSession(committedSession),
                        CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    await CleanupAttemptAsync(session, source, runtimeSink)
                        .ConfigureAwait(false);
                    return;
                }

                if (!TryPlayCurrentAttempt(
                        generation,
                        key,
                        cancellation,
                        committedSession))
                {
                    await CleanupAttemptAsync(session, source, runtimeSink)
                        .ConfigureAwait(false);
                }

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupAttemptAsync(session, source, runtimeSink)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                await CleanupAttemptAsync(session, source, runtimeSink)
                    .ConfigureAwait(false);

                var recoveryDelay = RecoveryDelays[Math.Min(retryIndex, RecoveryDelays.Length - 1)];
                retryIndex++;
                try
                {
                    await delay(recoveryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                await CleanupAttemptAsync(session, source, runtimeSink)
                    .ConfigureAwait(false);

                var safeCode = GetSafeFailureCode(exception);
                await InvokeIfCurrentAsync(
                        generation,
                        key,
                        cancellation,
                        () => tile.ShowError("播放失败", safeCode),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }
        }
    }

    private void PublishFromAttempt(
        SessionPlaybackRuntimeEventSink runtimeSink,
        PlaybackRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        lock (stateGate)
        {
            if (!IsCurrentAttemptNoLock(runtimeSink, runtimeEvent))
            {
                return;
            }
        }

        _ = PublishAsync(runtimeSink, runtimeEvent);
    }

    private async Task PublishAsync(
        SessionPlaybackRuntimeEventSink runtimeSink,
        PlaybackRuntimeEvent runtimeEvent)
    {
        try
        {
            lock (stateGate)
            {
                if (!IsCurrentAttemptNoLock(runtimeSink, runtimeEvent))
                {
                    return;
                }
            }

            switch (runtimeEvent.Kind)
            {
                case PlaybackRuntimeEventKind.Playing:
                    lock (stateGate)
                    {
                        if (IsCurrentAttemptNoLock(runtimeSink, runtimeEvent))
                        {
                            runtimeRecoveryIndex = 0;
                        }
                    }

                    await InvokeIfAttemptAsync(
                            runtimeSink,
                            () =>
                            {
                                if (currentSession is not null)
                                {
                                    tile.ShowPlaying(currentSession);
                                }
                            })
                        .ConfigureAwait(false);
                    break;
                case PlaybackRuntimeEventKind.Stopped:
                    await InvokeIfAttemptAsync(
                            runtimeSink,
                            tile.ShowPlaceholder)
                        .ConfigureAwait(false);
                    await RecoverFromRuntimeEventAsync(runtimeSink)
                        .ConfigureAwait(false);
                    break;
                case PlaybackRuntimeEventKind.Failed:
                    await RecoverFromRuntimeEventAsync(runtimeSink)
                        .ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RecoverFromRuntimeEventAsync(
        SessionPlaybackRuntimeEventSink runtimeSink)
    {
        await runtimeRecoveryGate.WaitAsync().ConfigureAwait(false);
        CancellationTokenSource? recoveryCancellation = null;
        try
        {
            PlaybackKey key;
            Task? task;
            long generation;
            TimeSpan recoveryDelay;
            lock (stateGate)
            {
                if (!IsCurrentAttemptNoLock(runtimeSink, null))
                {
                    return;
                }

                key = runtimeSink.Key;
                generation = runtimeSink.Generation;
                task = operationTask;
                recoveryDelay = RecoveryDelays[
                    Math.Min(runtimeRecoveryIndex, RecoveryDelays.Length - 1)];
                runtimeRecoveryIndex++;
                runtimeRecoveryCancellation?.Cancel();
                recoveryCancellation = new CancellationTokenSource();
                runtimeRecoveryCancellation = recoveryCancellation;
            }

            await StopCurrentSessionAsync(task).ConfigureAwait(false);
            if (!IsCurrentRecoveryRequest(generation, key, recoveryCancellation))
            {
                return;
            }

            try
            {
                await delay(
                        recoveryDelay,
                        recoveryCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (recoveryCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!IsCurrentRecoveryRequest(generation, key, recoveryCancellation))
            {
                return;
            }

            await StartAsyncCore(
                    key,
                    recoveryCancellation.Token,
                    resetRuntimeRecovery: false,
                    cancelRuntimeRecovery: false,
                    expectedGeneration: generation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            lock (stateGate)
            {
                if (ReferenceEquals(runtimeRecoveryCancellation, recoveryCancellation))
                {
                    runtimeRecoveryCancellation?.Dispose();
                    runtimeRecoveryCancellation = null;
                }
            }

            runtimeRecoveryGate.Release();
        }
    }

    private async Task ReleaseSafelyAsync(FormalPlaybackSource source)
    {
        try
        {
            await sourceProvider.ReleaseAsync(source).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task CleanupAttemptAsync(
        PlaybackSession? session,
        FormalPlaybackSource? source,
        SessionPlaybackRuntimeEventSink? runtimeSink)
    {
        if (session is not null)
        {
            stopPlayback(session);
        }

        if (source is not null)
        {
            await ReleaseSafelyAsync(source).ConfigureAwait(false);
        }

        ClearCurrentIf(session, source, runtimeSink);
    }

    private async Task<bool> InvokeIfCurrentAsync(
        long generation,
        PlaybackKey key,
        CancellationTokenSource cancellation,
        Action action,
        CancellationToken cancellationToken)
    {
        var committed = false;
        await dispatcher
            .InvokeAsync(
                () =>
                {
                    lock (stateGate)
                    {
                        if (IsCurrentRequestNoLock(generation, key, cancellation))
                        {
                            action();
                            committed = true;
                        }
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
        return committed;
    }

    private async Task<bool> InvokeIfAttemptAsync(
        SessionPlaybackRuntimeEventSink runtimeSink,
        Action action)
    {
        var committed = false;
        await dispatcher
            .InvokeAsync(
                () =>
                {
                    lock (stateGate)
                    {
                        if (IsCurrentAttemptNoLock(runtimeSink, null))
                        {
                            action();
                            committed = true;
                        }
                    }
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        return committed;
    }

    private bool TryCommitAttempt(
        long generation,
        PlaybackKey key,
        CancellationTokenSource cancellation,
        FormalPlaybackSource source,
        PlaybackSession session,
        SessionPlaybackRuntimeEventSink runtimeSink)
    {
        lock (stateGate)
        {
            if (!IsCurrentRequestNoLock(generation, key, cancellation))
            {
                return false;
            }

            currentSource = source;
            currentSession = session;
            currentRuntimeSink = runtimeSink;
            return true;
        }
    }

    private bool TryPlayCurrentAttempt(
        long generation,
        PlaybackKey key,
        CancellationTokenSource cancellation,
        PlaybackSession session)
    {
        lock (stateGate)
        {
            if (!IsCurrentRequestNoLock(generation, key, cancellation))
            {
                return false;
            }

            playPlayback(session);
            return true;
        }
    }

    private bool IsCurrentRequest(long generation, PlaybackKey key)
    {
        lock (stateGate)
        {
            return IsCurrentRequestNoLock(generation, key);
        }
    }

    private bool IsCurrentRequest(
        long generation,
        PlaybackKey key,
        CancellationTokenSource cancellation)
    {
        lock (stateGate)
        {
            return IsCurrentRequestNoLock(generation, key, cancellation);
        }
    }

    private bool IsCurrentRecoveryRequest(
        long generation,
        PlaybackKey key,
        CancellationTokenSource cancellation)
    {
        lock (stateGate)
        {
            return !disposed
                && currentGeneration == generation
                && currentKey == key
                && ReferenceEquals(runtimeRecoveryCancellation, cancellation)
                && !cancellation.IsCancellationRequested;
        }
    }

    private bool IsCurrentRequestNoLock(long generation, PlaybackKey key) =>
        !disposed
        && currentGeneration == generation
        && currentKey == key;

    private bool IsCurrentRequestNoLock(
        long generation,
        PlaybackKey key,
        CancellationTokenSource cancellation) =>
        IsCurrentRequestNoLock(generation, key)
        && ReferenceEquals(operationCancellation, cancellation)
        && !cancellation.IsCancellationRequested;

    private bool IsCurrentAttemptNoLock(
        SessionPlaybackRuntimeEventSink runtimeSink,
        PlaybackRuntimeEvent? runtimeEvent)
    {
        return !disposed
            && currentGeneration == runtimeSink.Generation
            && currentKey == runtimeSink.Key
            && ReferenceEquals(currentRuntimeSink, runtimeSink)
            && currentSession is not null
            && (runtimeEvent is null || runtimeEvent.ChannelId == runtimeSink.Key.ChannelId);
    }

    private async Task StopCurrentSessionAsync(Task? expectedTask)
    {
        PlaybackSession? session = null;
        FormalPlaybackSource? source = null;
        lock (stateGate)
        {
            if (expectedTask is not null
                && !ReferenceEquals(operationTask, expectedTask))
            {
                return;
            }

            session = currentSession;
            source = currentSource;
            currentSession = null;
            currentSource = null;
            currentRuntimeSink = null;
            operationTask = null;
            operationCancellation?.Dispose();
            operationCancellation = null;
        }

        if (session is not null)
        {
            stopPlayback(session);
        }

        if (source is not null)
        {
            await ReleaseSafelyAsync(source).ConfigureAwait(false);
        }
    }

    private void ClearCurrentIf(
        PlaybackSession? session,
        FormalPlaybackSource? source,
        SessionPlaybackRuntimeEventSink? runtimeSink)
    {
        lock (stateGate)
        {
            if (ReferenceEquals(currentSession, session))
            {
                currentSession = null;
            }

            if (ReferenceEquals(currentSource, source))
            {
                currentSource = null;
            }

            if (ReferenceEquals(currentRuntimeSink, runtimeSink))
            {
                currentRuntimeSink = null;
            }
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is PlaybackEngineException
        || exception is CatalogApiException catalogException
            && catalogException.Code is "CATALOG_UNAVAILABLE" or "MEDIA_UNAVAILABLE";

    private static string GetSafeFailureCode(Exception exception) =>
        exception is CatalogApiException catalogException
            ? catalogException.Code
            : "PLAYBACK_FAILED";

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class SessionPlaybackRuntimeEventSink : IPlaybackRuntimeEventSink
    {
        private readonly FormalPlaybackCoordinator owner;

        public SessionPlaybackRuntimeEventSink(
            FormalPlaybackCoordinator owner,
            long generation,
            PlaybackKey key)
        {
            this.owner = owner;
            Generation = generation;
            Key = key;
        }

        public long Generation { get; }

        public PlaybackKey Key { get; }

        public void Publish(PlaybackRuntimeEvent runtimeEvent) =>
            owner.PublishFromAttempt(this, runtimeEvent);
    }

    private readonly record struct PlaybackKey(
        Guid DeviceId,
        Guid ChannelId,
        StreamType StreamType);
}
