using System.Diagnostics;
using LibVLCSharp.Shared;

namespace VideoMonitor.Wpf.Playback;

public interface IPlaybackEngine
{
    PlaybackSession Start(PlaybackSource source);

    void Stop(PlaybackSession session);
}

public sealed class PlaybackEngineException : Exception
{
    public PlaybackEngineException(string message)
        : base(message)
    {
    }
}

public sealed class VlcPlaybackService : IPlaybackEngine, IFormalPlaybackEngine, IDisposable, IAsyncDisposable
{
    private readonly LibVLC libVlc;
    private readonly PlaybackDiagnosticsWriter? diagnosticsWriter;
    private int disposed;

    public VlcPlaybackService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        libVlc = new LibVLC("--no-video-title-show", "--rtsp-tcp", "--stats");
        diagnosticsWriter = PlaybackDiagnosticsWriter.TryCreateDefault(libVlc.Version);
        libVlc.Log += OnLibVlcLog;
    }

    public PlaybackSession Start(PlaybackSource source)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(source);

        var media = new Media(
            libVlc,
            source.PlaybackUrl.AbsoluteUri,
            FromType.FromLocation);
        var mediaPlayer = new MediaPlayer(media);
        var diagnostics = CreateDiagnostics(source.CameraChannelId, source.StreamId, media, mediaPlayer);
        var session = new PlaybackSession(
            source,
            media,
            mediaPlayer,
            diagnostics: diagnostics);
        try
        {
            if (!mediaPlayer.Play())
            {
                throw new PlaybackEngineException("LibVLC拒绝启动播放。");
            }

            mediaPlayer.AspectRatio = "19:10";
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public PlaybackSession Prepare(
        FormalPlaybackSource source,
        IPlaybackRuntimeEventSink eventSink)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(eventSink);

        var media = new Media(
            libVlc,
            source.PlaybackUrl.AbsoluteUri,
            FromType.FromLocation);
        var mediaPlayer = new MediaPlayer(media);
        var diagnostics = CreateDiagnostics(source.ChannelId, source.StreamId, media, mediaPlayer);
        EventHandler<EventArgs> playing = (_, _) =>
            eventSink.Publish(PlaybackRuntimeEvent.ForPlaying(source.ChannelId));
        EventHandler<EventArgs> stopped = (_, _) =>
            eventSink.Publish(PlaybackRuntimeEvent.ForStopped(source.ChannelId));
        EventHandler<EventArgs> failed = (_, _) =>
            eventSink.Publish(PlaybackRuntimeEvent.ForFailed(source.ChannelId));
        mediaPlayer.Playing += playing;
        mediaPlayer.Stopped += stopped;
        mediaPlayer.EncounteredError += failed;

        mediaPlayer.AspectRatio = "19:10";
        return new PlaybackSession(
            source.ChannelId,
            source.StreamId,
            media,
            mediaPlayer,
            () =>
            {
                mediaPlayer.Playing -= playing;
                mediaPlayer.Stopped -= stopped;
                mediaPlayer.EncounteredError -= failed;
            },
            diagnostics: diagnostics);
    }

    public void Play(PlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (session.MediaPlayer is null || !session.MediaPlayer.Play())
        {
            throw new PlaybackEngineException("LibVLC拒绝启动播放。");
        }
    }

    public void Stop(PlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        libVlc.Log -= OnLibVlcLog;
        if (diagnosticsWriter is not null)
        {
            await diagnosticsWriter.DisposeAsync().ConfigureAwait(false);
        }

        libVlc.Dispose();
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private PlaybackDiagnosticsSession? CreateDiagnostics(
        Guid channelId,
        string streamId,
        Media media,
        MediaPlayer mediaPlayer) =>
        diagnosticsWriter is null
            ? null
            : TryCreateDiagnostics(channelId, streamId, media, mediaPlayer);

    private PlaybackDiagnosticsSession? TryCreateDiagnostics(
        Guid channelId,
        string streamId,
        Media media,
        MediaPlayer mediaPlayer)
    {
        try
        {
            return new PlaybackDiagnosticsSession(
                channelId,
                streamId,
                media,
                mediaPlayer,
                diagnosticsWriter!);
        }
        catch
        {
            Debug.WriteLine("Playback diagnostics unavailable.");
            return null;
        }
    }

    private void OnLibVlcLog(object? sender, LogEventArgs args)
    {
        try
        {
            if (diagnosticsWriter is null
                || !PlaybackDiagnosticsNativeLogFilter.IsRelevant(args.Message))
            {
                return;
            }

            diagnosticsWriter.TryWrite(PlaybackDiagnosticsFormatter.FormatNativeLog(
                args.Level,
                args.Module,
                args.Message));
        }
        catch
        {
            Debug.WriteLine("Playback diagnostics unavailable.");
        }
    }
}
