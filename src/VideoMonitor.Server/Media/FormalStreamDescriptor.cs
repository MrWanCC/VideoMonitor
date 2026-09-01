using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed record FormalStreamDescriptor(
    string Vhost,
    string App,
    string Stream,
    MediaStreamKey Key);
