using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MonitorRuntimeStatusTests
{
    [Fact]
    public void ReadyReachableUpdatesTileOnline()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable)));

        Assert.Equal(CameraStatus.Online, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void FaultedConnectFailedUpdatesTileOffline()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Faulted, SourceObservation.ConnectFailed)));

        Assert.Equal(CameraStatus.Offline, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void AuthFailedUpdatesTileWarning()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Faulted, SourceObservation.AuthFailed)));

        Assert.Equal(CameraStatus.Warning, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void StaleUpdatesTileUnknown()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable, stale: true)));

        Assert.Equal(CameraStatus.Unknown, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void UnavailableServerUpdatesTileUnknown()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Unavailable,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable)));

        Assert.Equal(CameraStatus.Unknown, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void MissingExactRuntimeRemainsUnknown()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(new MediaDiagnosticsSnapshotDto(
            MediaServerHealth.Healthy,
            0,
            0,
            0,
            []));

        Assert.Equal(CameraStatus.Unknown, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void WrongStreamTypeDoesNotLeakStatus()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable),
            streamType: StreamType.Sub));

        Assert.Equal(CameraStatus.Unknown, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public async Task TileStatusRefreshDoesNotRestartPlayback()
    {
        var fixture = CreateFixture(withPlayback: true);

        await fixture.ViewModel.ActivatePlaybackAsync();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable)));

        Assert.Equal(1, fixture.Provider.PrepareCount);
    }

    [Fact]
    public void CatalogRefreshPreservesRuntimeStatusProjection()
    {
        var fixture = CreateFixture();
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable)));

        fixture.ReadModel.RaiseChanged();

        Assert.Equal(CameraStatus.Online, fixture.ViewModel.MainTiles[0].Status);
    }

    [Fact]
    public void GroupAllOnlineIsOnline()
    {
        var fixture = CreateFixture(cameraCount: 2);
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable),
            second: Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable)));

        Assert.Equal(CameraStatus.Online, GroupStatus(fixture));
    }

    [Fact]
    public void GroupAllOfflineIsOffline()
    {
        var fixture = CreateFixture(cameraCount: 2);
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Faulted, SourceObservation.ConnectFailed),
            second: Runtime(StreamRuntimeState.Faulted, SourceObservation.ConnectFailed)));

        Assert.Equal(CameraStatus.Offline, GroupStatus(fixture));
    }

    [Fact]
    public void GroupMixedOnlineOfflineIsWarning()
    {
        var fixture = CreateFixture(cameraCount: 2);
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable),
            second: Runtime(StreamRuntimeState.Faulted, SourceObservation.ConnectFailed)));

        Assert.Equal(CameraStatus.Warning, GroupStatus(fixture));
    }

    [Fact]
    public void GroupOfflineUnknownIsWarning()
    {
        var fixture = CreateFixture(cameraCount: 2);
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Faulted, SourceObservation.ConnectFailed)));

        Assert.Equal(CameraStatus.Warning, GroupStatus(fixture));
    }

    [Fact]
    public void GroupOnlineUnknownIsUnknown()
    {
        var fixture = CreateFixture(cameraCount: 2);
        fixture.Store.Apply(Snapshot(fixture,
            MediaServerHealth.Healthy,
            Runtime(StreamRuntimeState.Ready, SourceObservation.Reachable)));

        Assert.Equal(CameraStatus.Unknown, GroupStatus(fixture));
    }

    [Fact]
    public void GroupAllUnknownIsUnknown()
    {
        var fixture = CreateFixture(cameraCount: 2);

        Assert.Equal(CameraStatus.Unknown, GroupStatus(fixture));
    }

    private static CameraStatus GroupStatus(Fixture fixture) =>
        fixture.ViewModel.TreeSections.Single().Children.Single().Status;

    private static Fixture CreateFixture(
        int cameraCount = 1,
        bool withPlayback = false)
    {
        var rootId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var devices = Enumerable.Range(0, cameraCount)
            .Select(index =>
            {
                var deviceId = Guid.NewGuid();
                var channelId = Guid.NewGuid();
                return new CameraDeviceDto(
                    deviceId,
                    groupId,
                    $"Camera {index}",
                    $"192.0.2.{10 + index}",
                    8000,
                    554,
                    "safe-user",
                    true,
                    "Maker",
                    "Model",
                    TransportMode.Tcp,
                    true,
                    string.Empty,
                    1,
                    [new CameraChannelDto(
                        channelId,
                        deviceId,
                        index + 1,
                        "Main",
                        StreamType.Main,
                        true)]);
            })
            .ToArray();
        var readModel = new MutableReadModel(
            rootId,
            groupId,
            devices);
        var store = new MediaRuntimeStatusStore();
        var provider = new CapturingProvider();
        var viewModel = new MonitorViewModel(
            new MonitorSwitchService(Array.Empty<MonitorGroup>()),
            readModel,
            withPlayback
                ? tile => new FormalPlaybackCoordinator(
                    provider,
                    (source, _) => new PlaybackSession(
                        new PlaybackSource(
                            source.ChannelId,
                            source.StreamId,
                            source.PlaybackUrl,
                            null,
                            false),
                        null,
                        null),
                    session => session.Dispose(),
                    tile,
                    new InlineDispatcher())
                : null,
            runtimeStatusStore: store);

        return new Fixture(viewModel, store, readModel, devices, provider);
    }

    private static MediaDiagnosticsSnapshotDto Snapshot(
        Fixture fixture,
        MediaServerHealth health,
        MediaStreamRuntimeInfo first,
        MediaStreamRuntimeInfo? second = null,
        StreamType streamType = StreamType.Main)
    {
        var firstDevice = fixture.Devices[0];
        first = first with
        {
            Key = new MediaStreamKey(
                firstDevice.Id,
                firstDevice.Channels[0].Id,
                streamType)
        };
        var runtimes = second is null
            ? new[] { first }
            : new[]
            {
                first,
                second with
                {
                    Key = new MediaStreamKey(
                        fixture.Devices[1].Id,
                        fixture.Devices[1].Channels[0].Id,
                        streamType)
                }
            };
        return new MediaDiagnosticsSnapshotDto(
            health,
            runtimes.Length,
            0,
            0,
            runtimes.Select(ToDto).ToArray());
    }

    private static MediaStreamDiagnosticsDto ToDto(MediaStreamRuntimeInfo runtime) =>
        new(
            runtime.Key.DeviceId,
            runtime.Key.ChannelId,
            runtime.Key.StreamType,
            runtime.RuntimeState,
            runtime.ViewerCount.Value,
            runtime.Ownership,
            runtime.StartedAtUtc,
            runtime.SourceObservation,
            runtime.ObservedAtUtc,
            runtime.LastSuccessUtc,
            runtime.SafeLastErrorCode,
            runtime.SafeLastErrorMessage,
            runtime.IsStale);

    private static MediaStreamRuntimeInfo Runtime(
        StreamRuntimeState state,
        SourceObservation observation,
        bool stale = false) =>
        new(
            new MediaStreamKey(Guid.Empty, Guid.Empty, StreamType.Main),
            state,
            observation,
            new ViewerCount(1),
            StreamOwnership.OwnedCurrentProcess,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            stale);

    private sealed record Fixture(
        MonitorViewModel ViewModel,
        MediaRuntimeStatusStore Store,
        MutableReadModel ReadModel,
        IReadOnlyList<CameraDeviceDto> Devices,
        CapturingProvider Provider);

    private sealed class MutableReadModel : IDeviceCatalogReadModel
    {
        private readonly Guid groupId;
        private readonly IReadOnlyList<CameraDeviceDto> devices;

        public MutableReadModel(
            Guid rootId,
            Guid groupId,
            IReadOnlyList<CameraDeviceDto> devices)
        {
            this.groupId = groupId;
            this.devices = devices;
            Groups =
            [
                new DeviceGroupDto(
                    rootId,
                    "Chute Root",
                    null,
                    0,
                    true,
                    MonitorGroupType.Chute,
                    1),
                new DeviceGroupDto(
                    groupId,
                    "Chute A",
                    rootId,
                    0,
                    true,
                    null,
                    1)
            ];
        }

        public IReadOnlyList<DeviceGroupDto> Groups { get; }

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => Groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid id) =>
            id == groupId ? devices : [];

        public CameraDeviceDto? GetDevice(Guid id) =>
            devices.SingleOrDefault(device => device.Id == id);

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingProvider : IFormalPlaybackSourceProvider
    {
        private readonly Uri playbackUrl = new("rtsp://playback.example/live");

        public int PrepareCount { get; private set; }

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return Task.FromResult(new FormalPlaybackSource(
                deviceId,
                channelId,
                "vm_test",
                playbackUrl,
                DateTimeOffset.UtcNow.AddMinutes(1)));
        }

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
