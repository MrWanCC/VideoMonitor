namespace VideoMonitor.Wpf.Views.Pages;

public partial class MonitorView
{
    private const double ExpandedDetailHeight = 132;
    private const double CollapsedDetailHeight = 44;
    private bool detailExpanded = true;
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
            : new System.Windows.GridLength(48);
        DetailPanel.Visibility = fullscreen ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        DetailRow.Height = fullscreen
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(detailExpanded ? ExpandedDetailHeight : CollapsedDetailHeight);
    }

    private void ToggleDetailPanel(object sender, System.Windows.RoutedEventArgs e)
    {
        detailExpanded = !detailExpanded;
        DetailRow.Height = new System.Windows.GridLength(
            detailExpanded ? ExpandedDetailHeight : CollapsedDetailHeight);
        ((System.Windows.Media.RotateTransform)DetailChevron.RenderTransform).Angle = detailExpanded ? 90 : -90;
    }
}
