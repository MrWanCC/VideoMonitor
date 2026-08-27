using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
