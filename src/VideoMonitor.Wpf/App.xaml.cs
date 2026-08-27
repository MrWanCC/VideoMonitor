using System.Windows;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.ViewModels;

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
        var mainViewModel = new MainViewModel(monitorViewModel);

        MainWindow = new MainWindow(mainViewModel);
        MainWindow.Show();
    }
}
