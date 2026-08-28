using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MainNavigationTests
{
    [Fact]
    public void Navigate_PreservesDeviceManagementInstanceAndState()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var monitorGroups = MonitorCatalogProjection.CreateGroups(catalog);
        var switchService = new MonitorSwitchService(
            monitorGroups.Single(group => group.Name == "备用1"),
            monitorGroups.Single(group => group.Name == "Z-1#巷"),
            monitorGroups.Single(group => group.Name == "2#主溜井"));
        var monitor = new MonitorViewModel(switchService, monitorGroups, catalog);
        var deviceManagement = new DeviceManagementViewModel(catalog);
        var main = new MainViewModel(monitor, deviceManagement);
        deviceManagement.SearchKeyword = "192.168";

        main.NavigateCommand.Execute("设备管理");
        main.NavigateCommand.Execute("实时监控");
        main.NavigateCommand.Execute("设备管理");

        Assert.Same(deviceManagement, main.DeviceManagement);
        Assert.Equal("192.168", main.DeviceManagement.SearchKeyword);
    }
}
