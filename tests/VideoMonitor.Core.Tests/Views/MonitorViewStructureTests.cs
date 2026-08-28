namespace VideoMonitor.Core.Tests.Views;

public sealed class MonitorViewStructureTests
{
    [Fact]
    public void MonitorView_SummaryOmitsRepeatedCurrentScreenAndCameraName()
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

        Assert.Contains("Text=\"当前监控信息\"", xaml);
        Assert.Contains("Text=\"当前溜井：\"", xaml);
        Assert.Contains("Text=\"当前巷道：\"", xaml);
        Assert.DoesNotContain("当前画面：", xaml);
        Assert.DoesNotContain("Text=\"摄像头\"", xaml);
        Assert.Contains("Text=\"IP 地址\"", xaml);
        Assert.Contains("Text=\"状态\"", xaml);
        Assert.Contains("Text=\"分辨率\"", xaml);
    }
}
