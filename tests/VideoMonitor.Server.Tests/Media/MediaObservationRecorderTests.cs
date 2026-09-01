using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaObservationRecorderTests
{
    [Fact]
    public void SavedDeviceObservationUpdatesTimestampAndSafeErrorFields()
    {
        var key = new MediaStreamKey(
            Guid.Parse("79000000-0000-0000-0000-000000000001"),
            Guid.Parse("7a000000-0000-0000-0000-000000000001"),
            StreamType.Sub);
        var recorder = new MediaRuntimeRegistry();
        var successfulAt = new DateTimeOffset(2026, 9, 1, 1, 2, 3, TimeSpan.Zero);
        var failedAt = successfulAt.AddMinutes(1);

        recorder.Record(key, SourceObservation.Reachable, successfulAt, null, null);
        recorder.Record(
            key,
            SourceObservation.ConnectFailed,
            failedAt,
            "CAMERA_UNREACHABLE",
            "camera unavailable");

        var info = Assert.Single(recorder.GetSnapshot().Streams);
        Assert.Equal(failedAt, info.ObservedAtUtc);
        Assert.Equal(successfulAt, info.LastSuccessUtc);
        Assert.Equal(SourceObservation.ConnectFailed, info.SourceObservation);
        Assert.Equal("CAMERA_UNREACHABLE", info.SafeLastErrorCode);
        Assert.Equal("camera unavailable", info.SafeLastErrorMessage);
    }
}
