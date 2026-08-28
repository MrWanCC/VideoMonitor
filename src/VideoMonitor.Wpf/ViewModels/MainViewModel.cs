using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string selectedNavigation = "实时监控";
    private bool isMonitorFullscreen;
    private bool isSidebarCollapsed = true;
    private bool isSignalLinkageEnabled;
    private bool isSecondaryScreenVisible;

    public MainViewModel(
        MonitorViewModel monitor,
        DeviceManagementViewModel deviceManagement,
        bool isSecondaryScreenVisible = false)
    {
        Monitor = monitor;
        DeviceManagement = deviceManagement;
        this.isSecondaryScreenVisible = isSecondaryScreenVisible;
        NavigateCommand = new RelayCommand<string>(Navigate);
        ToggleFullscreenCommand = new RelayCommand(() => IsMonitorFullscreen = !IsMonitorFullscreen);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        ToggleSignalLinkageCommand = new RelayCommand(() => IsSignalLinkageEnabled = !IsSignalLinkageEnabled);
        ToggleSecondaryScreenCommand = new RelayCommand(() => IsSecondaryScreenVisible = !IsSecondaryScreenVisible);
    }

    public MonitorViewModel Monitor { get; }

    public DeviceManagementViewModel DeviceManagement { get; }

    public IRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand ToggleFullscreenCommand { get; }

    public IRelayCommand ToggleSidebarCommand { get; }

    public IRelayCommand ToggleSignalLinkageCommand { get; }

    public IRelayCommand ToggleSecondaryScreenCommand { get; }

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

    public bool IsSignalLinkageEnabled
    {
        get => isSignalLinkageEnabled;
        private set => SetProperty(ref isSignalLinkageEnabled, value);
    }

    public bool IsSecondaryScreenVisible
    {
        get => isSecondaryScreenVisible;
        set => SetProperty(ref isSecondaryScreenVisible, value);
    }

    private void Navigate(string? navigation)
    {
        if (!string.IsNullOrWhiteSpace(navigation))
        {
            SelectedNavigation = navigation;
        }
    }
}
