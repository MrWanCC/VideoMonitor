using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Playback;

public interface IPlaybackUrlBuilder
{
    Task<Uri> BuildAsync(
        PlaybackMediaIdentity media,
        PlaybackTicket ticket,
        CancellationToken cancellationToken = default);
}
