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

        Assert.Equal(3, CountOccurrences(xaml, expectedBinding));
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
