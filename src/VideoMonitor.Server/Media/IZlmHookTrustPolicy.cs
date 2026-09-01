using System.Net;

namespace VideoMonitor.Server.Media;

public interface IZlmHookTrustPolicy
{
    bool IsTrusted(IPAddress? remoteAddress);
}
