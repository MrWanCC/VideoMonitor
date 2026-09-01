using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public interface IMediaObservationRecorder
{
    void Record(
        MediaStreamKey key,
        SourceObservation observation,
        DateTimeOffset observedAtUtc,
        string? safeErrorCode,
        string? safeErrorMessage);
}
