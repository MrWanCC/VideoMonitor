using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Playback;

public interface IFormalPlaybackSourceProvider
{
    Task<FormalPlaybackSource> PrepareAsync(
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        FormalPlaybackSource source,
        CancellationToken cancellationToken = default);
}
