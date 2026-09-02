using LibVLCSharp.WPF;

namespace VideoMonitor.Wpf.Controls;

public partial class VideoTile
{
    private System.Windows.Window? foregroundOverlayWindow;
    private VideoView? videoHost;

    public VideoTile()
    {
        InitializeComponent();
        IsVisibleChanged += OnVideoTileIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnVideoTileIsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            SynchronizeVideoOverlayWindow,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ViewModels.VideoTileViewModel viewModel)
        {
            viewModel.RegisterVideoHostReadiness();
        }
    }

    private void OnVideoHostLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not VideoView host)
        {
            return;
        }

        if (!ReferenceEquals(videoHost, host))
        {
            if (videoHost is not null)
            {
                videoHost.IsVisibleChanged -= OnVideoHostIsVisibleChanged;
            }

            videoHost = host;
            videoHost.IsVisibleChanged += OnVideoHostIsVisibleChanged;
        }
        if (host.IsVisible
            && DataContext is ViewModels.VideoTileViewModel viewModel)
        {
            viewModel.MarkVideoHostReady();
        }

        Dispatcher.BeginInvoke(
            SynchronizeVideoOverlayWindow,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnVideoHostUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is VideoView host)
        {
            host.IsVisibleChanged -= OnVideoHostIsVisibleChanged;
        }

        foregroundOverlayWindow?.Hide();
        foregroundOverlayWindow = null;
        if (ReferenceEquals(videoHost, sender))
        {
            videoHost = null;
        }
    }

    private void OnVideoHostIsVisibleChanged(
        object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (videoHost?.IsVisible == true
            && DataContext is ViewModels.VideoTileViewModel viewModel)
        {
            viewModel.MarkVideoHostReady();
        }

        Dispatcher.BeginInvoke(
            SynchronizeVideoOverlayWindow,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SynchronizeVideoOverlayWindow()
    {
        var interactionSurface = videoHost is null
            ? null
            : FindVisualChildByName(videoHost, "VideoInteractionSurface");
        var overlayWindow = foregroundOverlayWindow
            ?? (interactionSurface is null
                ? null
                : System.Windows.Window.GetWindow(interactionSurface));
        if (overlayWindow is null || overlayWindow == System.Windows.Window.GetWindow(this))
        {
            return;
        }

        foregroundOverlayWindow = overlayWindow;

        if (videoHost?.IsVisible == true)
        {
            if (!overlayWindow.IsVisible)
            {
                overlayWindow.Show();
            }

            return;
        }

        overlayWindow.Hide();
    }

    private static System.Windows.DependencyObject? FindVisualChildByName(
        System.Windows.DependencyObject root,
        string name)
    {
        for (var index = 0;
             index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is System.Windows.FrameworkElement element
                && element.Name == name)
            {
                return child;
            }

            var descendant = FindVisualChildByName(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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
