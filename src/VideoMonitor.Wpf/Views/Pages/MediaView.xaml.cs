namespace VideoMonitor.Wpf.Views.Pages;

public partial class MediaView
{
    public static readonly System.Windows.DependencyProperty PageViewModelProperty =
        System.Windows.DependencyProperty.Register(
            nameof(PageViewModel),
            typeof(ViewModels.MediaPageViewModel),
            typeof(MediaView));

    public MediaView()
    {
        InitializeComponent();
    }

    public ViewModels.MediaPageViewModel? PageViewModel
    {
        get => (ViewModels.MediaPageViewModel?)GetValue(PageViewModelProperty);
        set => SetValue(PageViewModelProperty, value);
    }

    private async void OnIsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (PageViewModel is { } page)
            {
                if (e.NewValue is true)
                {
                    await page.ActivateAsync();
                }
                else
                {
                    await page.DeactivateAsync();
                }

                return;
            }

            if (DataContext is not ViewModels.MediaSettingsViewModel viewModel)
            {
                return;
            }

            if (e.NewValue is true)
            {
                await viewModel.LoadAsync();
            }
            else
            {
                viewModel.ClearTransientSecret();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception.GetType().Name);
        }
    }
}
