using System.Windows;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Services;
using VideoMonitor.Wpf.ViewModels;
using VideoMonitor.Wpf.Views;

namespace VideoMonitor.Wpf;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var groups = MockMonitorData.CreateGroups();
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name == "备用1"),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitorViewModel = new MonitorViewModel(switchService, groups);
        var deviceData = MockDeviceData.Create();
        var deviceManagementViewModel = new DeviceManagementViewModel(deviceData.Groups, deviceData.Devices);
        var mainViewModel = new MainViewModel(monitorViewModel, deviceManagementViewModel);
        var secondaryViewModel = new SecondaryMonitorViewModel(switchService, groups);
        var screenService = new ScreenService();
        var mainWindow = new MainWindow(mainViewModel);
        var secondaryWindow = new SecondaryMonitorWindow(secondaryViewModel);

        mainWindow.SourceInitialized += (_, _) => screenService.PlaceMainWindow(mainWindow);
        secondaryWindow.SourceInitialized += (_, _) => screenService.PlaceSecondaryWindow(secondaryWindow);
        mainWindow.Closed += (_, _) => secondaryWindow.Close();

        MainWindow = mainWindow;
        mainWindow.Show();
        secondaryWindow.Show();
    }
}
