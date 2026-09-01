using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

internal sealed class MediaStreamRuntimeState
{
    public StreamRuntimeState RuntimeState { get; set; } = StreamRuntimeState.Idle;

    public SourceObservation SourceObservation { get; set; } = SourceObservation.Unknown;

    public ViewerCount ViewerCount { get; set; }

    public StreamOwnership Ownership { get; set; } = StreamOwnership.NotOwned;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? ObservedAtUtc { get; set; }

    public DateTimeOffset? LastSuccessUtc { get; set; }

    public string? SafeLastErrorCode { get; set; }

    public string? SafeLastErrorMessage { get; set; }

    public bool IsStale { get; set; }

    // Server-only ownership evidence. Never exposed through the runtime snapshot.
    public string? ProxyKey { get; set; }
}
