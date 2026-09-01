namespace VideoMonitor.Core.Tests.Views;

public sealed class StatusBarStructureTests
{
    [Fact]
    public void StatusBar_UsesServerStateAndSyncWithoutFakeMetrics()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var xamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Controls",
            "StatusBar.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("ServerStatus.State", xaml);
        Assert.Contains("ServerStatus.StateText", xaml);
        Assert.Contains("ServerStatus.LastSuccessfulSyncText", xaml);
        Assert.Contains("IsCentralServerUiAvailable", xaml);

        Assert.DoesNotContain("系统时间：", xaml);
        Assert.DoesNotContain("CPU：", xaml);
        Assert.DoesNotContain("内存：", xaml);
        Assert.DoesNotContain("上行：", xaml);
        Assert.DoesNotContain("下行：", xaml);
        Assert.DoesNotContain("ZLMediaKit：", xaml);
        Assert.DoesNotContain("运行时间：", xaml);
        Assert.DoesNotContain("客户端运行中", xaml);
        Assert.DoesNotContain("系统运行正常", xaml);
        Assert.DoesNotContain("安全运行中", xaml);
    }

    [Fact]
    public void StatusBar_DoesNotOwnPerSecondClockTimer()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var codeBehindPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Controls",
            "StatusBar.xaml.cs");
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.DoesNotContain("DispatcherTimer", codeBehind);
        Assert.DoesNotContain("ClockText", codeBehind);
        Assert.DoesNotContain("DateTime.Now", codeBehind);
    }
}
