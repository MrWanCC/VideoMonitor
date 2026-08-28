using LibVLCSharp.Shared;

namespace VideoMonitor.Wpf.Playback;

public sealed class PlaybackSession : IDisposable
{
    private int disposed;

    public PlaybackSession(
        PlaybackSource source,
        Media? media,
        MediaPlayer? mediaPlayer)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Media = media;
        MediaPlayer = mediaPlayer;
    }

    public PlaybackSource Source { get; }

    public Guid CameraChannelId => Source.CameraChannelId;

    public string StreamId => Source.StreamId;

    public Uri PlaybackUrl => Source.PlaybackUrl;

    public string? ProxyKey => Source.ProxyKey;

    public bool OwnsProxy => Source.OwnsProxy;

    public Media? Media { get; }

    public MediaPlayer? MediaPlayer { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        MediaPlayer?.Stop();
        MediaPlayer?.Dispose();
        Media?.Dispose();
    }
}
