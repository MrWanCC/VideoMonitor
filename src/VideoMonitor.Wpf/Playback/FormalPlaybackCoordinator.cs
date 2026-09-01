using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Playback;

public interface IFormalPlaybackEngine
{
    PlaybackSession Start(
        FormalPlaybackSource source,
        IPlaybackRuntimeEventSink eventSink);

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
    private readonly Func<FormalPlaybackSource, IPlaybackRuntimeEventSink, PlaybackSession> startPlayback;
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
    private bool disposed;

    public FormalPlaybackCoordinator(
        IFormalPlaybackSourceProvider sourceProvider,
        Func<FormalPlaybackSource, IPlaybackRuntimeEventSink, PlaybackSession> startPlayback,
        Action<PlaybackSession> stopPlayback,
        VideoTileViewModel tile,
        IUiDispatcher dispatcher,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.sourceProvider = sourceProvider
            ?? throw new ArgumentNullException(nameof(sourceProvider));
        this.startPlayback = startPlayback
            ?? throw new ArgumentNullException(nameof(startPlayback));
        this.stopPlayback = stopPlayback
            ?? throw new ArgumentNullException(nameof(stopPlayback));
        this.tile = tile ?? throw new ArgumentNullException(nameof(tile));
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        this.delay = delay ?? Task.Delay;
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
        var key = new PlaybackKey(deviceId, channelId, streamType);
        Task? previousTask;
        var sameKey = false;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (currentKey == key && operationTask is not null)
            {
                previousTask = operationTask;
                sameKey = true;
            }
            else
            {
                operationCancellation?.Cancel();
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

        if (previousTask is not null)
        {
            await IgnoreCancellationAsync(previousTask).ConfigureAwait(false);
            await StopCurrentSessionAsync(previousTask).ConfigureAwait(false);
        }

        Task task;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            currentKey = key;
            task = operationTask = RunAsync(
                key,
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
            operationCancellation?.Cancel();
            runtimeRecoveryCancellation?.Cancel();
            task = operationTask;
            currentKey = null;
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
        _ = PublishAsync(runtimeEvent);
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
            operationCancellation?.Cancel();
            runtimeRecoveryCancellation?.Cancel();
            task = operationTask;
            currentKey = null;
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
            try
            {
                await dispatcher
                    .InvokeAsync(tile.ShowLoading, cancellationToken)
                    .ConfigureAwait(false);
                source = await sourceProvider
                    .PrepareAsync(
                        key.DeviceId,
                        key.ChannelId,
                        key.StreamType,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                session = startPlayback(source, this);
                lock (stateGate)
                {
                    currentSource = source;
                    currentSession = session;
                }

                await dispatcher
                    .InvokeAsync(() => tile.ShowPlaying(session), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (session is not null)
                {
                    stopPlayback(session);
                }

                if (source is not null)
                {
                    await ReleaseSafelyAsync(source).ConfigureAwait(false);
                }

                ClearCurrentIf(session, source);

                return;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                if (session is not null)
                {
                    stopPlayback(session);
                }

                if (source is not null)
                {
                    await ReleaseSafelyAsync(source).ConfigureAwait(false);
                }

                ClearCurrentIf(session, source);

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
                if (session is not null)
                {
                    stopPlayback(session);
                }

                if (source is not null)
                {
                    await ReleaseSafelyAsync(source).ConfigureAwait(false);
                }

                ClearCurrentIf(session, source);

                var safeCode = GetSafeFailureCode(exception);
                await dispatcher
                    .InvokeAsync(
                        () => tile.ShowError("播放失败", safeCode),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task PublishAsync(PlaybackRuntimeEvent runtimeEvent)
    {
        try
        {
            lock (stateGate)
            {
                if (disposed || currentKey?.ChannelId != runtimeEvent.ChannelId)
                {
                    return;
                }
            }

            switch (runtimeEvent.Kind)
            {
                case PlaybackRuntimeEventKind.Playing:
                    await dispatcher
                        .InvokeAsync(
                            () =>
                            {
                                if (CurrentSession is not null)
                                {
                                    tile.ShowPlaying(CurrentSession);
                                }
                            },
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    break;
                case PlaybackRuntimeEventKind.Stopped:
                    await dispatcher
                        .InvokeAsync(tile.ShowPlaceholder, CancellationToken.None)
                        .ConfigureAwait(false);
                    await RecoverFromRuntimeEventAsync(runtimeEvent).ConfigureAwait(false);
                    break;
                case PlaybackRuntimeEventKind.Failed:
                    await RecoverFromRuntimeEventAsync(runtimeEvent).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RecoverFromRuntimeEventAsync(
        PlaybackRuntimeEvent runtimeEvent)
    {
        await runtimeRecoveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            PlaybackKey key;
            Task? task;
            CancellationTokenSource recoveryCancellation;
            lock (stateGate)
            {
                if (disposed
                    || currentKey is not { } current
                    || current.ChannelId != runtimeEvent.ChannelId
                    || currentSession is null)
                {
                    return;
                }

                key = current;
                task = operationTask;
                runtimeRecoveryCancellation?.Cancel();
                runtimeRecoveryCancellation = new CancellationTokenSource();
                recoveryCancellation = runtimeRecoveryCancellation;
            }

            await StopCurrentSessionAsync(task).ConfigureAwait(false);
            lock (stateGate)
            {
                if (disposed || currentKey != key)
                {
                    return;
                }
            }

            try
            {
                await delay(
                        RecoveryDelays[0],
                        recoveryCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (recoveryCancellation.IsCancellationRequested)
            {
                return;
            }

            await StartAsync(key.DeviceId, key.ChannelId, key.StreamType)
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
                runtimeRecoveryCancellation?.Dispose();
                runtimeRecoveryCancellation = null;
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
        FormalPlaybackSource? source)
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

    private readonly record struct PlaybackKey(
        Guid DeviceId,
        Guid ChannelId,
        StreamType StreamType);
}
