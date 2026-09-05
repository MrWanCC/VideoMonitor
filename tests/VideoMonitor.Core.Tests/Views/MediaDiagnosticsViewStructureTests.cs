namespace VideoMonitor.Core.Tests.Views;

public sealed class MediaDiagnosticsViewStructureTests
{
    [Fact]
    public void MediaViewContainsDiagnosticsSummaryAndRefresh()
    {
        var xaml = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.Contains("ActiveStreamCount", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewerCount", xaml, StringComparison.Ordinal);
        Assert.Contains("FaultCount", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RetryCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ChannelNo", xaml, StringComparison.Ordinal);
        Assert.Contains("SafeLastErrorMessage", xaml, StringComparison.Ordinal);
        Assert.Contains("IsStale", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaViewKeepsMediaSettingsDataContextCompatibility()
    {
        var mainWindow = ReadProjectFile("src/VideoMonitor.Wpf/MainWindow.xaml");
        var mediaView = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.Contains("DataContext=\"{Binding MediaSettings}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("PageViewModel=\"{Binding DataContext.MediaPage", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "behaviors:PasswordBoxBinding.BoundPassword=\"{Binding ZlmSecret, Mode=TwoWay}\"",
            mediaView,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VideoMonitor.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
