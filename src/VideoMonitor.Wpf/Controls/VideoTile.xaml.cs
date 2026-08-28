namespace VideoMonitor.Wpf.Controls;

public partial class VideoTile
{
    public VideoTile()
    {
        InitializeComponent();
        IsVisibleChanged += OnVideoTileIsVisibleChanged;
    }

    private void OnVideoTileIsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            SynchronizeVideoOverlayWindow,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SynchronizeVideoOverlayWindow()
    {
        var overlayWindow = System.Windows.Window.GetWindow(VideoInteractionSurface);
        if (overlayWindow is null || overlayWindow == System.Windows.Window.GetWindow(this))
        {
            return;
        }

        if (IsVisible)
        {
            if (!overlayWindow.IsVisible)
            {
                overlayWindow.Show();
            }

            return;
        }

        overlayWindow.Hide();
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
