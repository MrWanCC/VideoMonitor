using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed record ResolvedCameraSource(
    MediaStreamKey Key,
    Uri SourceUri,
    string SourceBindingFingerprint);

public interface ICameraSourceResolver
{
    Task<ResolvedCameraSource> ResolveAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default);
}
