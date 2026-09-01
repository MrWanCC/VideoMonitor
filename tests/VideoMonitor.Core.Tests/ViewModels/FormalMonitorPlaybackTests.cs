using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class FormalMonitorPlaybackTests
{
    [Fact]
    public void ProjectionUsesCatalogDtoIdsOnly()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var provider = new CapturingProvider(deviceId, channelId);
        var readModel = new CatalogReadModel(
            [
                new DeviceGroupDto(rootId, "Chute", null, 0, true, MonitorGroupType.Chute, 1),
                new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)
            ],
            [Device(deviceId, childId, channelId)]);

        var viewModel = new MonitorViewModel(
            new MonitorSwitchService(Array.Empty<MonitorGroup>()),
            readModel,
            tile => new FormalPlaybackCoordinator(
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
                new ImmediateDispatcher()));

        var tile = Assert.Single(viewModel.MainTiles.Where(item => item.CurrentDeviceId == deviceId));
        Assert.Equal(deviceId, tile.CurrentDeviceId);
        Assert.Equal(channelId, tile.CurrentChannelId);
        Assert.Equal(deviceId, provider.LastDeviceId);
        Assert.Equal(channelId, provider.LastChannelId);
    }

    private static CameraDeviceDto Device(Guid deviceId, Guid groupId, Guid channelId) =>
        new(
            deviceId,
            groupId,
            "Camera",
            "192.0.2.10",
            8000,
            554,
            "safe-user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "",
            1,
            [new CameraChannelDto(channelId, deviceId, 1, "Main", StreamType.Main, true)]);

    private sealed class CapturingProvider : IFormalPlaybackSourceProvider
    {
        private readonly Guid deviceId;
        private readonly Guid channelId;

        public CapturingProvider(Guid deviceId, Guid channelId)
        {
            this.deviceId = deviceId;
            this.channelId = channelId;
        }

        public Guid? LastDeviceId { get; private set; }

        public Guid? LastChannelId { get; private set; }

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            LastDeviceId = deviceId;
            LastChannelId = channelId;
            return Task.FromResult(new FormalPlaybackSource(
                this.deviceId,
                this.channelId,
                "formal-stream",
                new Uri("https://server-b/live/formal-stream"),
                DateTimeOffset.UtcNow.AddMinutes(1)));
        }

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
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

    private sealed class CatalogReadModel : IDeviceCatalogReadModel
    {
        public CatalogReadModel(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto> devices)
        {
            Groups = groups;
            Devices = devices;
        }

        public IReadOnlyList<DeviceGroupDto> Groups { get; }

        public IReadOnlyList<CameraDeviceDto> Devices { get; }

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => Groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            Devices.Where(device => device.GroupId == groupId).ToArray();

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            Devices.SingleOrDefault(device => device.Id == deviceId);

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
