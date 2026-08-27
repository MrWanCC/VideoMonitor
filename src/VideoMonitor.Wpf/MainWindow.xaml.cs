using System.ComponentModel;
using System.Windows;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf;

public partial class MainWindow
{
    private const double SidebarExpandedWidth = 188d;
    private const double SidebarCollapsedWidth = 56d;
    private readonly MainViewModel viewModel;
    private System.Windows.WindowStyle savedWindowStyle;
    private System.Windows.ResizeMode savedResizeMode;
    private System.Windows.WindowState savedWindowState;
    private Rect savedBounds;
    private bool fullscreenApplied;

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ApplySidebarState();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && viewModel.IsMonitorFullscreen)
        {
            viewModel.IsMonitorFullscreen = false;
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed))
        {
            if (!fullscreenApplied)
            {
                ApplySidebarState();
            }

            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsMonitorFullscreen))
        {
            if (viewModel.IsMonitorFullscreen)
            {
                viewModel.Monitor.ExitSingleTileModeCommand.Execute(null);
                EnterMonitorFullscreen();
            }
            else
            {
                ExitMonitorFullscreen();
            }
        }
    }

    private void EnterMonitorFullscreen()
    {
        if (fullscreenApplied)
        {
            return;
        }

        savedWindowStyle = WindowStyle;
        savedResizeMode = ResizeMode;
        savedWindowState = WindowState;
        savedBounds = new Rect(Left, Top, Width, Height);
        fullscreenApplied = true;

        HeaderChrome.Visibility = Visibility.Collapsed;
        NavigationChrome.Visibility = Visibility.Collapsed;
        TreeChrome.Visibility = Visibility.Collapsed;
        FooterChrome.Visibility = Visibility.Collapsed;
        HeaderRow.Height = new GridLength(0);
        FooterRow.Height = new GridLength(0);
        NavigationColumn.Width = new GridLength(0);
        TreeColumn.Width = new GridLength(0);
        MonitorContent.SetFullscreen(true);

        WindowStyle = System.Windows.WindowStyle.None;
        ResizeMode = System.Windows.ResizeMode.NoResize;
        WindowState = System.Windows.WindowState.Maximized;
        Focus();
    }

    private void ExitMonitorFullscreen()
    {
        if (!fullscreenApplied)
        {
            return;
        }

        WindowState = System.Windows.WindowState.Normal;
        WindowStyle = savedWindowStyle;
        ResizeMode = savedResizeMode;
        Left = savedBounds.Left;
        Top = savedBounds.Top;
        Width = savedBounds.Width;
        Height = savedBounds.Height;

        HeaderRow.Height = new GridLength(56);
        FooterRow.Height = new GridLength(36);
        ApplySidebarState();
        TreeColumn.Width = new GridLength(300);
        HeaderChrome.Visibility = Visibility.Visible;
        NavigationChrome.Visibility = Visibility.Visible;
        TreeChrome.Visibility = Visibility.Visible;
        FooterChrome.Visibility = Visibility.Visible;
        MonitorContent.SetFullscreen(false);
        WindowState = savedWindowState;
        fullscreenApplied = false;
    }

    private void ApplySidebarState()
    {
        var width = viewModel.IsSidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;
        NavigationColumn.Width = new GridLength(width);
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
