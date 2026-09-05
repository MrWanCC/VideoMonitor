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
    private readonly ServerStatusViewModel? serverStatus;
    private readonly Func<ServerSettingsViewModel>? serverSettingsFactory;
    private readonly MediaSettingsViewModel? mediaSettings;
    private readonly MediaPageViewModel? mediaPage;

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

    public MainViewModel(
        MonitorViewModel monitor,
        DeviceManagementViewModel deviceManagement,
        ServerStatusViewModel serverStatus,
        Func<ServerSettingsViewModel> serverSettingsFactory,
        bool isSecondaryScreenVisible = false)
        : this(
            monitor,
            deviceManagement,
            serverStatus,
            serverSettingsFactory,
            null,
            null,
            isSecondaryScreenVisible)
    {
    }

    public MainViewModel(
        MonitorViewModel monitor,
        DeviceManagementViewModel deviceManagement,
        ServerStatusViewModel serverStatus,
        Func<ServerSettingsViewModel> serverSettingsFactory,
        MediaSettingsViewModel? mediaSettings,
        bool isSecondaryScreenVisible = false)
        : this(
            monitor,
            deviceManagement,
            serverStatus,
            serverSettingsFactory,
            mediaSettings,
            null,
            isSecondaryScreenVisible)
    {
    }

    public MainViewModel(
        MonitorViewModel monitor,
        DeviceManagementViewModel deviceManagement,
        ServerStatusViewModel serverStatus,
        Func<ServerSettingsViewModel> serverSettingsFactory,
        MediaSettingsViewModel? mediaSettings,
        MediaPageViewModel? mediaPage,
        bool isSecondaryScreenVisible = false)
        : this(monitor, deviceManagement, isSecondaryScreenVisible)
    {
        this.serverStatus = serverStatus
            ?? throw new ArgumentNullException(nameof(serverStatus));
        this.serverSettingsFactory = serverSettingsFactory
            ?? throw new ArgumentNullException(nameof(serverSettingsFactory));
        this.mediaSettings = mediaSettings;
        this.mediaPage = mediaPage;
    }

    public MonitorViewModel Monitor { get; }

    public DeviceManagementViewModel DeviceManagement { get; }

    public ServerStatusViewModel? ServerStatus => serverStatus;

    public MediaSettingsViewModel? MediaSettings => mediaSettings;

    public MediaPageViewModel? MediaPage => mediaPage;

    public bool IsMediaSettingsAvailable => mediaSettings is not null;

    public bool IsCentralServerUiAvailable =>
        serverStatus is not null && serverSettingsFactory is not null;

    public IRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand ToggleFullscreenCommand { get; }

    public IRelayCommand ToggleSidebarCommand { get; }

    public IRelayCommand ToggleSignalLinkageCommand { get; }

    public IRelayCommand ToggleSecondaryScreenCommand { get; }

    public ServerSettingsViewModel CreateServerSettingsViewModel()
    {
        if (serverSettingsFactory is null)
        {
            throw new InvalidOperationException(
                "Central Server settings are not available in legacy mode.");
        }

        return serverSettingsFactory();
    }

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
