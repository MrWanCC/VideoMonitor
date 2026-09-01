namespace VideoMonitor.Core.Media;

public enum StreamOwnership
{
    OwnedCurrentProcess,
    OwnedAdopted,
    NotOwned,
    External
}

public enum MediaServerHealth
{
    Unconfigured,
    Healthy,
    Unavailable,
    ConfigurationError
}

public enum StreamRuntimeState
{
    Idle,
    Starting,
    Ready,
    Stopping,
    Faulted
}

public enum SourceObservation
{
    Unknown,
    Reachable,
    ConnectFailed,
    AuthFailed
}

public readonly record struct ViewerCount(int Value);

public sealed record MediaStreamRuntimeInfo(
    MediaStreamKey Key,
    StreamRuntimeState RuntimeState,
    SourceObservation SourceObservation,
    ViewerCount ViewerCount,
    StreamOwnership Ownership,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastErrorCode,
    string? SafeLastErrorMessage,
    bool IsStale);

public sealed record MediaRuntimeSnapshot(
    MediaServerHealth ServerHealth,
    IReadOnlyList<MediaStreamRuntimeInfo> Streams);
