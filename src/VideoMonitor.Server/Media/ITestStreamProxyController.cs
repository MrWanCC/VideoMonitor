using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public interface ITestStreamProxyController
{
    Task<TestStreamProxyHandle> StartAsync(
        ResolvedTestCameraSource source,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        TestStreamProxyHandle handle,
        CancellationToken cancellationToken = default);
}
