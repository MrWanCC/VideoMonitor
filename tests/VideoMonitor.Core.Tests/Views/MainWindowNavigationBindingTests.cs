namespace VideoMonitor.Core.Tests.Views;

public sealed class MainWindowNavigationBindingTests
{
    [Fact]
    public void PageVisibilityBindings_ReadSelectedNavigationFromWindowDataContext()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var xamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        const string expectedBinding =
            "DataContext.SelectedNavigation, RelativeSource={RelativeSource AncestorType=Window}";

        Assert.Equal(4, CountOccurrences(xaml, expectedBinding));
    }

    [Fact]
    public void MediaSettingsNavigation_RendersFormalMediaPage()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var xamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("<pages:MediaView", xaml, StringComparison.Ordinal);
        Assert.Contains("DataContext=\"{Binding MediaSettings}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "ConverterParameter=流媒体管理",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=实时监控", xaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=设备管理", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaSettingsPage_LoadsWhenItBecomesVisible()
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
            "MediaView.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("IsVisibleChanged=\"OnIsVisibleChanged\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnLoaded\"", xaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var position = 0;
        while ((position = source.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }
}
