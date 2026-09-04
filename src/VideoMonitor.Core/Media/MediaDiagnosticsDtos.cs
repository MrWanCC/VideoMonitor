using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Media;

public sealed record MediaDiagnosticsSnapshotDto(
    MediaServerHealth ServerHealth,
    int ActiveStreamCount,
    int ViewerCount,
    int FaultCount,
    IReadOnlyList<MediaStreamDiagnosticsDto> Streams);

public sealed record MediaStreamDiagnosticsDto(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType,
    StreamRuntimeState RuntimeState,
    int ViewerCount,
    StreamOwnership Ownership,
    DateTimeOffset? StartedAtUtc,
    SourceObservation SourceObservation,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastErrorCode,
    string? SafeLastErrorMessage,
    bool IsStale);
