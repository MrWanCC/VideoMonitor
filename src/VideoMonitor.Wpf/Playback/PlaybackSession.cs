using LibVLCSharp.Shared;

namespace VideoMonitor.Wpf.Playback;

public sealed class PlaybackSession : IDisposable
{
    private int disposed;
    private readonly Action? detachRuntimeEvents;

    public PlaybackSession(
        PlaybackSource source,
        Media? media,
        MediaPlayer? mediaPlayer,
        Action? detachRuntimeEvents = null,
        IDisposable? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        CameraChannelId = source.CameraChannelId;
        StreamId = source.StreamId;
        Media = media;
        MediaPlayer = mediaPlayer;
        this.detachRuntimeEvents = detachRuntimeEvents;
        this.diagnostics = diagnostics;
    }

    public PlaybackSession(
        Guid channelId,
        string streamId,
        Media? media,
        MediaPlayer? mediaPlayer,
        Action? detachRuntimeEvents = null,
        IDisposable? diagnostics = null)
    {
        CameraChannelId = channelId;
        StreamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        Media = media;
        MediaPlayer = mediaPlayer;
        this.detachRuntimeEvents = detachRuntimeEvents;
        this.diagnostics = diagnostics;
    }

    public Guid CameraChannelId { get; }

    public string StreamId { get; }

    public Media? Media { get; }

    public MediaPlayer? MediaPlayer { get; }

    private readonly IDisposable? diagnostics;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        detachRuntimeEvents?.Invoke();
        diagnostics?.Dispose();
        MediaPlayer?.Stop();
        MediaPlayer?.Dispose();
        Media?.Dispose();
    }
}
