using System.ComponentModel;
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
        var screenService = new ScreenService();
        var mainViewModel = new MainViewModel(
            monitorViewModel,
            deviceManagementViewModel,
            screenService.HasSecondaryScreen);
        var secondaryViewModel = new SecondaryMonitorViewModel(switchService, groups);
        var mainWindow = new MainWindow(mainViewModel);
        var secondaryWindow = new SecondaryMonitorWindow(secondaryViewModel);

        mainWindow.SourceInitialized += (_, _) => screenService.PlaceMainWindow(mainWindow);

        void ApplySecondaryScreenVisibility()
        {
            if (!mainViewModel.IsSecondaryScreenVisible)
            {
                secondaryWindow.Hide();
                return;
            }

            screenService.PlaceSecondaryWindow(secondaryWindow);
            secondaryWindow.Show();
            secondaryWindow.Activate();
        }

        void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainViewModel.IsSecondaryScreenVisible))
            {
                ApplySecondaryScreenVisibility();
            }
        }

        mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        secondaryWindow.HiddenByUser += (_, _) => mainViewModel.IsSecondaryScreenVisible = false;
        mainWindow.Closing += (_, _) =>
        {
            mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            secondaryWindow.AllowSecondaryWindowClose = true;
            secondaryWindow.Close();
        };

        MainWindow = mainWindow;
        mainWindow.Show();
        ApplySecondaryScreenVisibility();
    }
}
