using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Catalog;

namespace VideoMonitor.Server.Playback;

public sealed record FormalStreamEnsureRequest(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType);

public sealed record FormalStreamEnsureResult(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType,
    PlaybackMediaIdentity MediaIdentity,
    StreamRuntimeState RuntimeState);

public interface IFormalStreamEnsureService
{
    Task<CatalogOperationResult<FormalStreamEnsureResult>> EnsureAsync(
        FormalStreamEnsureRequest request,
        CancellationToken cancellationToken = default);
}
