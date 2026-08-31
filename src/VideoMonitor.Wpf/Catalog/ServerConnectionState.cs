namespace VideoMonitor.Wpf.Catalog;

public enum ServerConnectionState
{
    Unconfigured,
    Connecting,
    Connected,
    Unavailable
}

public sealed record ServerConnectionStatus(
    Uri? BaseUri,
    ServerConnectionState State,
    DateTimeOffset? LastSuccessfulSyncUtc,
    bool IsStale);
