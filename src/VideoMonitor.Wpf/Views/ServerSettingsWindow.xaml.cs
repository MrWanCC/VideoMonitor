using System.ComponentModel;
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

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is ServerSettingsViewModel viewModel
            && viewModel.IsBusy)
        {
            e.Cancel = true;
        }
    }
}
