using System.Windows;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views;

public partial class ServerSettingsWindow
{
    public ServerSettingsWindow(ServerSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CancelWindow(object sender, RoutedEventArgs e) => Close();
}
