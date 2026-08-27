using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views;

public partial class SecondaryMonitorWindow
{
    public SecondaryMonitorWindow(SecondaryMonitorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
