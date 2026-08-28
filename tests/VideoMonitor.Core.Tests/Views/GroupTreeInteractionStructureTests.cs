namespace VideoMonitor.Core.Tests.Views;

public sealed class GroupTreeInteractionStructureTests
{
    [Fact]
    public void DeviceView_GroupRowContextMenuIsAttachedToClickableRowButton()
    {
        var xaml = ReadXaml(
            "Views",
            "Pages",
            "DeviceView.xaml");

        Assert.Contains("<Button x:Name=\"GroupRow\"", xaml);
        Assert.Contains("<Button.ContextMenu>", xaml);
        Assert.DoesNotContain("<Border.ContextMenu>", xaml);
    }

    [Fact]
    public void DeviceView_GroupRowUsesHoverStyleOnlyWhenNotSelected()
    {
        var xaml = ReadXaml(
            "Views",
            "Pages",
            "DeviceView.xaml");

        Assert.Contains("DeviceGroupRowStyle", xaml);
        Assert.Contains("<MultiDataTrigger>", xaml);
        Assert.Contains("Path=IsMouseOver", xaml);
        Assert.Contains("Binding=\"{Binding IsSelected}\"", xaml);
        Assert.Contains("Value=\"False\"", xaml);
    }

    [Fact]
    public void GroupTreeRootRowsToggleExpansionAcrossTheFullRow()
    {
        var deviceXaml = ReadXaml(
            "Views",
            "Pages",
            "DeviceView.xaml");
        var monitorXaml = ReadXaml(
            "Controls",
            "MonitorTree.xaml");
        var deviceToggleStyle = GetSection(
            deviceXaml,
            "<Style x:Key=\"DeviceGroupSectionToggleStyle\"",
            "</Style>");

        Assert.Contains("Grid.ColumnSpan=\"3\"", deviceXaml);
        Assert.Contains("IsChecked=\"{Binding IsExpanded, Mode=TwoWay}\"", deviceXaml);
        Assert.Contains("ContentPresenter", deviceToggleStyle);
        Assert.Contains("Foreground\" Value=\"{StaticResource SecondaryTextBrush}\"", deviceToggleStyle);
        Assert.Contains("Visibility=\"{Binding IsExpanded, Converter={StaticResource BoolToVisibilityConverter}}\"", deviceXaml);
        Assert.Contains("HorizontalAlignment\" Value=\"Stretch\"", monitorXaml);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Stretch\"", monitorXaml);
        Assert.Contains("IsChecked=\"{Binding IsExpanded, Mode=TwoWay}\"", monitorXaml);
    }

    private static string ReadXaml(params string[] path)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            new[] { repositoryRoot, "src", "VideoMonitor.Wpf" }
                .Concat(path)
                .ToArray()));
    }

    private static string GetSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
