using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public interface IMediaSettingsProbe
{
    Task<MediaSettingsTestResult> TestAsync(
        TestMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}
