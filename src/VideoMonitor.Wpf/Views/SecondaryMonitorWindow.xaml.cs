using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views;

public partial class SecondaryMonitorWindow
{
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

    private void CloseWindow(object sender, System.Windows.RoutedEventArgs e) => Close();
}
