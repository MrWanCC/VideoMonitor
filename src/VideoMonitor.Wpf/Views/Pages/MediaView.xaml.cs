namespace VideoMonitor.Wpf.Views.Pages;

public partial class MediaView
{
    public MediaView()
    {
        InitializeComponent();
    }

    private async void OnIsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is not ViewModels.MediaSettingsViewModel viewModel)
        {
            return;
        }

        if (e.NewValue is true)
        {
            await viewModel.LoadAsync();
            return;
        }

        viewModel.ClearTransientSecret();
    }
}
