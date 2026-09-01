using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public enum MediaStreamNamespace
{
    Formal,
    Test
}

public sealed record MediaStreamRequest(
    MediaStreamNamespace Namespace,
    MediaStreamKey? CatalogKey,
    string Vhost,
    string App,
    string Stream,
    Uri SourceUri);
