using System.Net;

namespace VideoMonitor.Server.Media;

public sealed class LoopbackZlmHookTrustPolicy : IZlmHookTrustPolicy
{
    public bool IsTrusted(IPAddress? remoteAddress) =>
        remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
}
