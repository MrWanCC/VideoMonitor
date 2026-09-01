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
        if (e.NewValue is not true
            || DataContext is not ViewModels.MediaSettingsViewModel viewModel)
        {
            return;
        }

        await viewModel.LoadAsync();
    }
}
