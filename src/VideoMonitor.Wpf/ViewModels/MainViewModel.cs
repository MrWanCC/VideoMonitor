using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string selectedNavigation = "实时监控";
    private bool isMonitorFullscreen;
    private bool isSidebarCollapsed;

    public MainViewModel(
        MonitorViewModel monitor,
        DeviceManagementViewModel deviceManagement)
    {
        Monitor = monitor;
        DeviceManagement = deviceManagement;
        NavigateCommand = new RelayCommand<string>(Navigate);
        ToggleFullscreenCommand = new RelayCommand(() => IsMonitorFullscreen = !IsMonitorFullscreen);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
    }

    public MonitorViewModel Monitor { get; }

    public DeviceManagementViewModel DeviceManagement { get; }

    public IRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand ToggleFullscreenCommand { get; }

    public IRelayCommand ToggleSidebarCommand { get; }

    public string SelectedNavigation
    {
        get => selectedNavigation;
        private set => SetProperty(ref selectedNavigation, value);
    }

    public bool IsMonitorFullscreen
    {
        get => isMonitorFullscreen;
        set => SetProperty(ref isMonitorFullscreen, value);
    }

    public bool IsSidebarCollapsed
    {
        get => isSidebarCollapsed;
        private set => SetProperty(ref isSidebarCollapsed, value);
    }

    private void Navigate(string? navigation)
    {
        if (!string.IsNullOrWhiteSpace(navigation))
        {
            SelectedNavigation = navigation;
        }
    }
}
