namespace VideoMonitor.Core.Tests.Views;

public sealed class MonitorViewStructureTests
{
    [Fact]
    public void MonitorView_DetailHeaderUsesChannelDetailsWithoutRepeatedSummary()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var xamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Views",
            "Pages",
            "MonitorView.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"通道详情\"", xaml);
        Assert.DoesNotContain("Text=\"当前监控信息\"", xaml);
        Assert.DoesNotContain("Text=\"当前溜井：\"", xaml);
        Assert.DoesNotContain("Text=\"当前巷道：\"", xaml);
        Assert.Contains("Text=\"溜井监控\"", xaml);
        Assert.Contains("Text=\"{Binding CurrentChuteName}\"", xaml);
        Assert.DoesNotContain("当前画面：", xaml);
        Assert.DoesNotContain("Text=\"摄像头\"", xaml);
        Assert.Contains("Text=\"IP 地址\"", xaml);
        Assert.Contains("Text=\"状态\"", xaml);
        Assert.Contains("Text=\"码流\"", xaml);
        Assert.Contains("Text=\"码率\"", xaml);
        Assert.Contains("Text=\"分辨率\"", xaml);
        Assert.Contains("Text=\"通道\"", xaml);
        Assert.Contains("Text=\"更新时间\"", xaml);
    }

    [Fact]
    public void MonitorView_ActivatesFormalPlaybackWithViewLifecycle()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var codePath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Views",
            "Pages",
            "MonitorView.xaml.cs");
        var code = File.ReadAllText(codePath);

        Assert.Contains("Loaded += OnLoaded", code);
        Assert.Contains("Unloaded += OnUnloaded", code);
        Assert.Contains("IsVisibleChanged += OnIsVisibleChanged", code);
        Assert.Contains("ActivatePlaybackAsync", code);
        Assert.Contains("DeactivatePlaybackAsync", code);
    }
}
