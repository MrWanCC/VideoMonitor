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

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void FocusAddressInput(object sender, RoutedEventArgs e)
    {
        ServerBaseUrlTextBox.Focus();
        ServerBaseUrlTextBox.SelectAll();
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is ServerSettingsViewModel viewModel
            && viewModel.IsBusy)
        {
            e.Cancel = true;
        }
    }
}
