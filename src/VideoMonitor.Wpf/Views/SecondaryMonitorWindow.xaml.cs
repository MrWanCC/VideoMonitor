using System.ComponentModel;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views;

public partial class SecondaryMonitorWindow
{
    private const double RestoredHeight = 540d;
    private System.Windows.ResizeMode restoredResizeMode;
    private System.Windows.Rect restoredBounds;

    public bool AllowSecondaryWindowClose { get; set; }

    public event EventHandler? HiddenByUser;

    public SecondaryMonitorWindow(SecondaryMonitorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private void MinimizeWindow(object sender, System.Windows.RoutedEventArgs e) => WindowState = System.Windows.WindowState.Minimized;

    private void MaximizeWindow(object sender, System.Windows.RoutedEventArgs e)
    {
        if (WindowState == System.Windows.WindowState.Maximized)
        {
            WindowState = System.Windows.WindowState.Normal;
            MinHeight = RestoredHeight;
            MaxHeight = RestoredHeight;
            ResizeMode = restoredResizeMode;
            Left = restoredBounds.Left;
            Top = restoredBounds.Top;
            Width = restoredBounds.Width;
            Height = RestoredHeight;
            return;
        }

        restoredResizeMode = ResizeMode;
        restoredBounds = new System.Windows.Rect(Left, Top, Width, Height);
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        ResizeMode = System.Windows.ResizeMode.CanResize;
        WindowState = System.Windows.WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowSecondaryWindowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenByUser?.Invoke(this, EventArgs.Empty);
        }

        base.OnClosing(e);
    }

    private void CloseWindow(object sender, System.Windows.RoutedEventArgs e) => Close();
}
