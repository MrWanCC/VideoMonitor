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

public sealed class VlcPlaybackService : IPlaybackEngine, IFormalPlaybackEngine, IDisposable
{
    private readonly LibVLC libVlc;
    private int disposed;

    public VlcPlaybackService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        libVlc = new LibVLC("--no-video-title-show", "--rtsp-tcp");
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
        if (!mediaPlayer.Play())
        {
            mediaPlayer.Dispose();
            media.Dispose();
            throw new PlaybackEngineException("LibVLC拒绝启动播放。");
        }

        mediaPlayer.AspectRatio = "19:10";

        return new PlaybackSession(source, media, mediaPlayer);
    }

    public PlaybackSession Start(
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
        EventHandler<EventArgs> playing = (_, _) =>
            eventSink.Publish(PlaybackRuntimeEvent.ForPlaying(source.ChannelId));
        EventHandler<EventArgs> stopped = (_, _) =>
            eventSink.Publish(PlaybackRuntimeEvent.ForStopped(source.ChannelId));
        EventHandler<EventArgs> failed = (_, _) =>
            eventSink.Publish(PlaybackRuntimeEvent.ForFailed(source.ChannelId));
        mediaPlayer.Playing += playing;
        mediaPlayer.Stopped += stopped;
        mediaPlayer.EncounteredError += failed;

        if (!mediaPlayer.Play())
        {
            mediaPlayer.Playing -= playing;
            mediaPlayer.Stopped -= stopped;
            mediaPlayer.EncounteredError -= failed;
            mediaPlayer.Dispose();
            media.Dispose();
            throw new PlaybackEngineException("LibVLC拒绝启动播放。");
        }

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
            });
    }

    public void Stop(PlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        libVlc.Dispose();
    }
}
