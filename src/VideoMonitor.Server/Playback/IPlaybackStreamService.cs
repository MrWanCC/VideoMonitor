using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Catalog;

namespace VideoMonitor.Server.Playback;

public interface IPlaybackStreamService
{
    Task<CatalogOperationResult<EnsurePlaybackStreamResponse>> EnsureAsync(
        EnsurePlaybackStreamRequest request,
        CancellationToken cancellationToken = default);
}
