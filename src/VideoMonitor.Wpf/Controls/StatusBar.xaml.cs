namespace VideoMonitor.Wpf.Controls;

public partial class StatusBar
{
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public StatusBar()
    {
        InitializeComponent();
        timer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        timer.Start();
        Unloaded += (_, _) => timer.Stop();
    }
}
