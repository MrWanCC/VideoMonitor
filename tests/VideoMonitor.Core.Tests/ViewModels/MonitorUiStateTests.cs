using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
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
        var main = string.Join('|', service.Current.MainSlots.Select(camera => camera.Name));
        var secondary = string.Join('|', service.Current.SecondarySlots.Select(camera => camera.Name));
        var unloadingGroup = service.Current.SecondarySlots[0].GroupName;
        return $"{viewModel.CurrentChuteName};{viewModel.CurrentTunnelName};{unloadingGroup};{main};{secondary}";
    }

    private static MonitorGroup Group(IReadOnlyList<MonitorGroup> groups, string name) =>
        groups.Single(group => group.Name == name);
}
