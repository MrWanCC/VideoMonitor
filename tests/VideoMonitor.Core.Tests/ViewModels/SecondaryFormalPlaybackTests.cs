using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class SecondaryFormalPlaybackTests
{
    [Fact]
    public void CatalogRefreshPreservesSelectionByGuid()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var readModel = new MutableReadModel(
        [
            new DeviceGroupDto(rootId, "Unloading", null, 0, true, MonitorGroupType.UnloadingStation, 1),
            new DeviceGroupDto(firstId, "A", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "B", rootId, 1, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var switchService = new MonitorSwitchService(groups);
        var viewModel = new SecondaryMonitorViewModel(switchService, readModel);

        viewModel.SelectGroupCommand.Execute(secondId);
        readModel.Replace(
        [
            new DeviceGroupDto(rootId, "Unloading renamed", null, 0, true, MonitorGroupType.UnloadingStation, 2),
            new DeviceGroupDto(firstId, "A renamed", rootId, 0, true, null, 2),
            new DeviceGroupDto(secondId, "B renamed", rootId, 1, true, null, 2)
        ]);
        readModel.RaiseChanged();

        Assert.Equal(secondId, viewModel.SelectedGroupId);
        Assert.Equal("B renamed", viewModel.CurrentGroupName);
    }

    private sealed class MutableReadModel : IDeviceCatalogReadModel
    {
        private IReadOnlyList<DeviceGroupDto> groups;

        public MutableReadModel(IReadOnlyList<DeviceGroupDto> groups) => this.groups = groups;

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => [];

        public CameraDeviceDto? GetDevice(Guid deviceId) => null;

        public void Replace(IReadOnlyList<DeviceGroupDto> next) => groups = next;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
