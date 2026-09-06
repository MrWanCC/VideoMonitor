namespace VideoMonitor.Core.Tests.Views;

public sealed class MediaDiagnosticsViewStructureTests
{
    [Fact]
    public void MediaViewUsesManagementLayoutWithCollapsedAdvancedSettings()
    {
        var xaml = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.Contains("Text=\"流媒体管理\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"运行概览\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"当前活动媒体流\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"高级设置\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate TargetType=\"{x:Type ToggleButton}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{StaticResource SecondaryTextBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{StaticResource CardBackgroundBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderBrush\" Value=\"{StaticResource BorderBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderThickness\" Value=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PrimaryBlueMutedBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaViewContainsDiagnosticsSummaryAndRefresh()
    {
        var xaml = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.Contains("ServerHealth", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerHealthText", xaml, StringComparison.Ordinal);
        Assert.Contains("ActiveStreamCount", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewerCount", xaml, StringComparison.Ordinal);
        Assert.Contains("FaultCount", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RetryCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("DeviceName", xaml, StringComparison.Ordinal);
        Assert.Contains("StreamTypeText", xaml, StringComparison.Ordinal);
        Assert.Contains("RuntimeStateText", xaml, StringComparison.Ordinal);
        Assert.Contains("SafeLastErrorMessage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"陈旧：\"", xaml, StringComparison.Ordinal);
        Assert.Contains("状态数据已过期", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding CanRetry}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Visibility\" Value=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource SecondaryButtonStyle}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaViewKeepsSettingsInsideAdvancedExpander()
    {
        var xaml = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.Contains("ZlmApiBaseUrl", xaml, StringComparison.Ordinal);
        Assert.Contains("PlaybackBaseUrl", xaml, StringComparison.Ordinal);
        Assert.Contains("Vhost", xaml, StringComparison.Ordinal);
        Assert.Contains("FormalApp", xaml, StringComparison.Ordinal);
        Assert.Contains("TestApp", xaml, StringComparison.Ordinal);
        Assert.Contains("ZlmSecret", xaml, StringComparison.Ordinal);
        Assert.Contains("NoReaderGraceSeconds", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsSummaryText", xaml, StringComparison.Ordinal);
        Assert.Contains("TestCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SaveCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("测试连接", xaml, StringComparison.Ordinal);
        Assert.Contains("保存配置", xaml, StringComparison.Ordinal);
        Assert.Matches(
            "Command=\"\\{Binding TestCommand\\}\"\\s+Style=\"\\{StaticResource SecondaryButtonStyle\\}\"",
            xaml);
        Assert.Matches(
            "Command=\"\\{Binding SaveCommand\\}\"\\s+Style=\"\\{StaticResource PrimaryButtonStyle\\}\"",
            xaml);
        Assert.Matches(
            "Command=\"\\{Binding RefreshCommand\\}\"\\s+Style=\"\\{StaticResource SecondaryButtonStyle\\}\"",
            xaml);
        Assert.Contains("Style=\"{StaticResource SecondaryButtonStyle}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaViewUsesSafeSecretControlsAndProductizedSummary()
    {
        var xaml = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.Contains(
            "BasedOn=\"{StaticResource IndustrialPasswordBoxStyle}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("IsZlmSecretVisible", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{Binding ZlmSecret, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("显示", xaml, StringComparison.Ordinal);
        Assert.Contains("隐藏", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsSummaryText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding HasSecret}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaViewUsesNeutralAdvancedHeaderWhenExpanded()
    {
        var xaml = ReadProjectFile("src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml");

        Assert.DoesNotContain(
            "<Setter TargetName=\"HeaderChrome\" Property=\"BorderBrush\" Value=\"{StaticResource PrimaryBlueBrush}\" />",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"Background\" Value=\"{StaticResource CardBackgroundBrush}\" />",
            xaml,
            StringComparison.Ordinal);
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
