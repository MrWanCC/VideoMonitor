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

public sealed class VlcPlaybackService : IPlaybackEngine, IDisposable
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
