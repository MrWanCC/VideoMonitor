using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public interface IStreamManager
{
    Task<StreamEnsureResult> EnsureStreamAsync(
        MediaStreamRequest request,
        CancellationToken cancellationToken = default);

    Task CleanupOwnedStreamIfEligibleAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default);

    MediaRuntimeSnapshot GetSnapshot();
}
