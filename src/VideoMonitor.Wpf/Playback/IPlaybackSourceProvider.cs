using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Playback;

public interface IPlaybackSourceProvider
{
    Task<PlaybackSource> PrepareAsync(
        CameraDevice device,
        CameraChannel channel,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        PlaybackSource source,
        CancellationToken cancellationToken);
}
