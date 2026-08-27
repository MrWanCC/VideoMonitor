namespace VideoMonitor.Wpf.Views.Pages;

public partial class MonitorView
{
    public MonitorView()
    {
        InitializeComponent();
    }

    public void SetFullscreen(bool fullscreen)
    {
        MonitorHeader.Visibility = fullscreen
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        MonitorHeaderRow.Height = fullscreen
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(54);
    }
}
