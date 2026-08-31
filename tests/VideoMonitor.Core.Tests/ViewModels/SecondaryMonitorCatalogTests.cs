using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class SecondaryMonitorCatalogTests
{
    [Fact]
    public void SecondaryGroups_AreDynamicFromCatalog()
    {
        var rootId = Guid.NewGuid();
        var readModel = new MutableReadModelStub(
        [
            new DeviceGroupDto(rootId, "Unloading Root", null, 0, true, MonitorGroupType.UnloadingStation, 1),
            new DeviceGroupDto(Guid.NewGuid(), "卸矿 A", rootId, 0, true, null, 1),
            new DeviceGroupDto(Guid.NewGuid(), "卸矿 A", rootId, 1, true, null, 1),
            new DeviceGroupDto(Guid.NewGuid(), "卸矿 B", rootId, 2, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var viewModel = new SecondaryMonitorViewModel(new MonitorSwitchService(groups), readModel);

        Assert.Equal(3, viewModel.UnloadingGroups.Count);
    }

    [Fact]
    public void SecondaryDuplicateNames_SwitchesByGuid()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var readModel = new MutableReadModelStub(
        [
            new DeviceGroupDto(rootId, "Unloading Root", null, 0, true, MonitorGroupType.UnloadingStation, 1),
            new DeviceGroupDto(firstId, "同名卸矿", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "同名卸矿", rootId, 1, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var switchService = new MonitorSwitchService(groups);
        var viewModel = new SecondaryMonitorViewModel(switchService, readModel);

        viewModel.SelectGroupCommand.Execute(secondId);

        Assert.Equal(secondId, viewModel.SelectedGroupId);
        Assert.Equal(secondId, switchService.SelectedUnloadingGroupId);
    }

    [Fact]
    public void SecondaryCatalogChange_DeletedSelectionFallsBack()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var initial = new[]
        {
            new DeviceGroupDto(rootId, "Unloading Root", null, 0, true, MonitorGroupType.UnloadingStation, 1),
            new DeviceGroupDto(firstId, "A", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "B", rootId, 1, true, null, 1)
        };
        var readModel = new MutableReadModelStub(initial);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var switchService = new MonitorSwitchService(groups);
        var viewModel = new SecondaryMonitorViewModel(switchService, readModel);

        viewModel.SelectGroupCommand.Execute(secondId);
        readModel.Replace(initial.Take(2).ToArray());
        readModel.RaiseChanged();

        Assert.Equal(firstId, viewModel.SelectedGroupId);
        Assert.Equal("A", viewModel.CurrentGroupName);
    }

    [Fact]
    public void SecondaryEmptyCatalog_RendersThreeUnconfiguredTiles()
    {
        var readModel = new MutableReadModelStub([]);
        var viewModel = new SecondaryMonitorViewModel(
            new MonitorSwitchService(Array.Empty<MonitorGroup>()),
            readModel);

        Assert.Equal(3, viewModel.Tiles.Count);
        Assert.All(viewModel.Tiles, tile =>
        {
            Assert.Equal("未配置", tile.CameraName);
            Assert.Equal(CameraStatus.Unknown, tile.Status);
        });
        Assert.Null(viewModel.SelectedGroupId);
        Assert.Equal("未配置", viewModel.CurrentGroupName);
    }

    private sealed class MutableReadModelStub : IDeviceCatalogReadModel
    {
        private IReadOnlyList<DeviceGroupDto> groups;

        public MutableReadModelStub(IReadOnlyList<DeviceGroupDto> groups) => this.groups = groups;

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => [];

        public CameraDeviceDto? GetDevice(Guid deviceId) => null;

        public void Replace(IReadOnlyList<DeviceGroupDto> next) => groups = next;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
