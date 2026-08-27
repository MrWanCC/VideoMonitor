using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string selectedNavigation = "实时监控";
    private bool isMonitorFullscreen;

    public MainViewModel(MonitorViewModel monitor)
    {
        Monitor = monitor;
        NavigateCommand = new RelayCommand<string>(Navigate);
        ToggleFullscreenCommand = new RelayCommand(() => IsMonitorFullscreen = !IsMonitorFullscreen);
    }

    public MonitorViewModel Monitor { get; }

    public IRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand ToggleFullscreenCommand { get; }

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

    private void Navigate(string? navigation)
    {
        if (!string.IsNullOrWhiteSpace(navigation))
        {
            SelectedNavigation = navigation;
        }
    }
}
