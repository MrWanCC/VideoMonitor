namespace VideoMonitor.Wpf.Views.Pages;

public partial class MediaView
{
    private bool loadedOnce;

    public MediaView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (loadedOnce || DataContext is not ViewModels.MediaSettingsViewModel viewModel)
        {
            return;
        }

        loadedOnce = true;
        await viewModel.LoadAsync();
    }
}
