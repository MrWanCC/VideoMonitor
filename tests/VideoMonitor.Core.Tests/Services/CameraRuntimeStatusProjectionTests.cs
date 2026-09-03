using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Core.Tests.Services;

public sealed class CameraRuntimeStatusProjectionTests
{
    private static readonly MediaStreamKey Key = new(
        Guid.Parse("93000000-0000-0000-0000-000000000001"),
        Guid.Parse("94000000-0000-0000-0000-000000000001"),
        StreamType.Main);

    [Theory]
    [InlineData(MediaServerHealth.Unconfigured)]
    [InlineData(MediaServerHealth.Unavailable)]
    [InlineData(MediaServerHealth.ConfigurationError)]
    public void UnhealthyMediaServer_ProjectsUnknown(MediaServerHealth health)
    {
        var runtime = CreateRuntime();

        Assert.Equal(
            CameraStatus.Unknown,
            CameraRuntimeStatusProjection.Project(health, Key, runtime));
    }

    [Fact]
    public void HealthyWithoutRuntime_ProjectsUnknown()
    {
        Assert.Equal(
            CameraStatus.Unknown,
            CameraRuntimeStatusProjection.Project(
                MediaServerHealth.Healthy,
                Key,
                null));
    }

    [Fact]
    public void RuntimeForAnotherKey_ProjectsUnknown()
    {
        var anotherKey = Key with
        {
            ChannelId = Guid.Parse("95000000-0000-0000-0000-000000000001")
        };

        Assert.Equal(
            CameraStatus.Unknown,
            CameraRuntimeStatusProjection.Project(
                MediaServerHealth.Healthy,
                Key,
                CreateRuntime(key: anotherKey)));
    }

    [Fact]
    public void StaleRuntime_ProjectsUnknown()
    {
        Assert.Equal(
            CameraStatus.Unknown,
            Project(CreateRuntime(isStale: true)));
    }

    [Fact]
    public void AuthFailed_ProjectsWarning()
    {
        Assert.Equal(
            CameraStatus.Warning,
            Project(CreateRuntime(sourceObservation: SourceObservation.AuthFailed)));
    }

    [Fact]
    public void FaultedConnectFailed_ProjectsOffline()
    {
        Assert.Equal(
            CameraStatus.Offline,
            Project(CreateRuntime(
                runtimeState: StreamRuntimeState.Faulted,
                sourceObservation: SourceObservation.ConnectFailed)));
    }

    [Fact]
    public void FaultedWithoutConnectFailure_ProjectsWarning()
    {
        Assert.Equal(
            CameraStatus.Warning,
            Project(CreateRuntime(runtimeState: StreamRuntimeState.Faulted)));
    }

    [Fact]
    public void ReadyReachableWithNoViewers_ProjectsOnline()
    {
        Assert.Equal(
            CameraStatus.Online,
            Project(CreateRuntime(viewerCount: 0)));
    }

    [Fact]
    public void Starting_ProjectsUnknown()
    {
        Assert.Equal(
            CameraStatus.Unknown,
            Project(CreateRuntime(runtimeState: StreamRuntimeState.Starting)));
    }

    [Fact]
    public void Idle_ProjectsUnknown()
    {
        Assert.Equal(
            CameraStatus.Unknown,
            Project(CreateRuntime(runtimeState: StreamRuntimeState.Idle)));
    }

    [Fact]
    public void ReadyWithoutReachableObservation_ProjectsUnknown()
    {
        Assert.Equal(
            CameraStatus.Unknown,
            Project(CreateRuntime(sourceObservation: SourceObservation.Unknown)));
    }

    private static CameraStatus Project(MediaStreamRuntimeInfo runtime) =>
        CameraRuntimeStatusProjection.Project(MediaServerHealth.Healthy, Key, runtime);

    private static MediaStreamRuntimeInfo CreateRuntime(
        MediaStreamKey? key = null,
        StreamRuntimeState runtimeState = StreamRuntimeState.Ready,
        SourceObservation sourceObservation = SourceObservation.Reachable,
        int viewerCount = 0,
        bool isStale = false) =>
        new(
            key ?? Key,
            runtimeState,
            sourceObservation,
            new ViewerCount(viewerCount),
            StreamOwnership.OwnedCurrentProcess,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            isStale);
}
