namespace VideoMonitor.Wpf.Controls;

public partial class VideoTile
{
    public VideoTile()
    {
        InitializeComponent();
    }

    private void OnVideoSurfaceMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        e.Handled = true;
        RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            e.MouseDevice,
            e.Timestamp,
            e.ChangedButton)
        {
            RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent,
            Source = this
        });
    }
}
