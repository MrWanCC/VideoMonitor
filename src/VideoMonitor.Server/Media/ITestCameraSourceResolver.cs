using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public interface ITestCameraSourceResolver
{
    Task<ResolvedTestCameraSource> ResolveAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default);
}
