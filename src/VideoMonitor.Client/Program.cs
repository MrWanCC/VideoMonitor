using VideoMonitor.Client.Forms;
using VideoMonitor.Client.Mock;
using VideoMonitor.Client.Services;

namespace VideoMonitor.Client;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var groups = MockMonitorData.CreateGroups();
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name == "备用1"),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));

        Application.Run(new MainForm(switchService, groups, new ScreenService()));
    }
}
