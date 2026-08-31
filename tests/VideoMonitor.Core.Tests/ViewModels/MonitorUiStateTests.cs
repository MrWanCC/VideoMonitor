using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MonitorUiStateTests
{
    [Fact]
    public void ToggleSingleTile_EntersModeWithRequestedExistingSlot()
    {
        var (viewModel, _) = CreateFixture();
        var requestedSlot = viewModel.MainTiles[2];

        viewModel.ToggleSingleTileCommand.Execute(requestedSlot);

        Assert.True(viewModel.IsSingleTileMode);
        Assert.Same(requestedSlot, viewModel.SelectedVideoSlot);
    }

    [Fact]
    public void ToggleSingleTile_SameSlotAgain_RestoresFourViewState()
    {
        var (viewModel, _) = CreateFixture();
        var requestedSlot = viewModel.MainTiles[1];

        viewModel.ToggleSingleTileCommand.Execute(requestedSlot);
        viewModel.ToggleSingleTileCommand.Execute(requestedSlot);

        Assert.False(viewModel.IsSingleTileMode);
        Assert.Equal(4, viewModel.MainTiles.Count);
        Assert.Same(requestedSlot, viewModel.SelectedVideoSlot);
    }

    [Fact]
    public void ToggleSidebar_DoesNotChangeCurrentMonitorGroups()
    {
        var (monitor, service) = CreateFixture();
        var deviceData = MockDeviceData.Create();
        var main = new MainViewModel(
            monitor,
            new DeviceManagementViewModel(
                new InMemoryDeviceCatalog(deviceData.Groups, deviceData.Devices)));
        var before = Snapshot(monitor, service);

        main.ToggleSidebarCommand.Execute(null);

        Assert.False(main.IsSidebarCollapsed);
        Assert.Equal(before, Snapshot(monitor, service));
    }

    [Fact]
    public void MainView_DefaultsSidebarCollapsed()
    {
        var (monitor, _) = CreateFixture();
        var deviceData = MockDeviceData.Create();
        var main = new MainViewModel(
            monitor,
            new DeviceManagementViewModel(
                new InMemoryDeviceCatalog(deviceData.Groups, deviceData.Devices)));

        Assert.True(main.IsSidebarCollapsed);
        Assert.Equal("实时监控", main.SelectedNavigation);
    }

    [Fact]
    public void ToggleDetailPanel_DoesNotChangeCurrentMonitorGroups()
    {
        var (monitor, service) = CreateFixture();
        var before = Snapshot(monitor, service);

        monitor.ToggleDetailPanelCommand.Execute(null);

        Assert.False(monitor.IsDetailPanelCollapsed);
        Assert.Equal(before, Snapshot(monitor, service));
    }

    [Fact]
    public void MonitorView_DefaultsDetailPanelCollapsed()
    {
        var (monitor, _) = CreateFixture();

        Assert.True(monitor.IsDetailPanelCollapsed);
    }

    [Fact]
    public void NullTile_ResetShowsUnconfiguredAndUnknown()
    {
        var tile = new VideoTileViewModel();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var channel = new CameraChannelDto(
            channelId,
            deviceId,
            1,
            "Main",
            StreamType.Main,
            true);
        var info = new CameraInfo("Camera", "Group", 1)
        {
            DeviceId = deviceId,
            ChannelId = channelId
        };
        var device = new CameraDeviceDto(
            deviceId,
            Guid.NewGuid(),
            "Camera",
            "192.0.2.10",
            8000,
            554,
            "user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            1,
            [channel]);

        tile.Update(info, device, channel, CameraStatus.Warning);
        tile.ShowError("播放失败", "旧错误");

        tile.ResetUnconfigured();

        Assert.Equal("未配置", tile.CameraName);
        Assert.Equal("--", tile.GroupName);
        Assert.Equal(0, tile.ChannelNumber);
        Assert.Equal(CameraStatus.Unknown, tile.Status);
        Assert.Equal("--", tile.IpAddress);
        Assert.Equal("-- Mbps", tile.Bitrate);
        Assert.Equal("--", tile.StreamType);
        Assert.Equal(PlaybackState.Placeholder, tile.PlaybackState);
        Assert.Null(tile.PlaybackSession);
        Assert.Equal(string.Empty, tile.PlaybackErrorTitle);
        Assert.Equal(string.Empty, tile.PlaybackErrorDetail);
    }

    [Fact]
    public void VideoTileUpdate_UsesPasswordSafeDtoAndExplicitRuntimeStatus()
    {
        var tile = new VideoTileViewModel();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var channel = new CameraChannelDto(
            channelId,
            deviceId,
            1,
            "Main",
            StreamType.Main,
            true);
        var info = new CameraInfo("Camera", "Group", 1)
        {
            DeviceId = deviceId,
            ChannelId = channelId
        };
        var device = new CameraDeviceDto(
            deviceId,
            Guid.NewGuid(),
            "Camera DTO",
            "192.0.2.20",
            8000,
            554,
            "user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            1,
            [channel]);

        tile.Update(info, device, channel, CameraStatus.Warning);

        Assert.Equal("Camera", tile.CameraName);
        Assert.Equal("Group", tile.GroupName);
        Assert.Equal("192.0.2.20", tile.IpAddress);
        Assert.Equal(CameraStatus.Warning, tile.Status);
        Assert.Equal("主码流", tile.StreamType);
    }

    [Fact]
    public void EmptyCentralCatalog_RendersFourUnconfiguredMainTiles()
    {
        var readModel = new CentralReadModelStub();
        var viewModel = new MonitorViewModel(
            new MonitorSwitchService(Array.Empty<MonitorGroup>()),
            readModel);

        Assert.Equal(4, viewModel.MainTiles.Count);
        Assert.All(viewModel.MainTiles, tile =>
        {
            Assert.Equal("未配置", tile.CameraName);
            Assert.Equal(CameraStatus.Unknown, tile.Status);
        });
    }

    [Fact]
    public void SelectedZeroCameraChute_ShowsGroupNameButNullTiles()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var readModel = new CentralReadModelStub
        {
            Groups =
            [
                new DeviceGroupDto(rootId, "Chute Root", null, 0, true, MonitorGroupType.Chute, 1),
                new DeviceGroupDto(childId, "Chute A", rootId, 0, true, null, 1)
            ]
        };
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var viewModel = new MonitorViewModel(new MonitorSwitchService(groups), readModel);

        Assert.Equal("Chute A", viewModel.CurrentChuteName);
        Assert.All(viewModel.MainTiles.Take(3), tile =>
            Assert.Equal("未配置", tile.CameraName));
    }

    private static (MonitorViewModel ViewModel, MonitorSwitchService Service) CreateFixture()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var service = new MonitorSwitchService(
            Group(groups, "备用1"),
            Group(groups, "Z-1#巷"),
            Group(groups, "2#主溜井"));

        return (new MonitorViewModel(service, groups, catalog), service);
    }

    private static string Snapshot(MonitorViewModel viewModel, MonitorSwitchService service)
    {
        var main = string.Join('|', service.Current.MainSlots.Select(camera => camera?.Name ?? "--"));
        var secondary = string.Join('|', service.Current.SecondarySlots.Select(camera => camera?.Name ?? "--"));
        var unloadingGroup = service.Current.SecondarySlots[0]?.GroupName ?? "--";
        return $"{viewModel.CurrentChuteName};{viewModel.CurrentTunnelName};{unloadingGroup};{main};{secondary}";
    }

    private static MonitorGroup Group(IReadOnlyList<MonitorGroup> groups, string name) =>
        groups.Single(group => group.Name == name);

    private sealed class CentralReadModelStub : IDeviceCatalogReadModel
    {
        public IReadOnlyList<DeviceGroupDto> Groups { get; init; } = [];

        public IReadOnlyList<CameraDeviceDto> Devices { get; init; } = [];

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => Groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            Devices.Where(device => device.GroupId == groupId).ToArray();

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            Devices.SingleOrDefault(device => device.Id == deviceId);

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
