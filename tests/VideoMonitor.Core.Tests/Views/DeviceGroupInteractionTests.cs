namespace VideoMonitor.Core.Tests.Views;

public sealed class DeviceGroupInteractionTests
{
    [Fact]
    public void GroupRow_IsTheClickableAndContextMenuOwningControl()
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
            "DeviceView.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("<Button x:Name=\"GroupRow\"", xaml);
        Assert.Contains("<Button.ContextMenu>", xaml);
        Assert.Contains("Command=\"{Binding Tag.SelectGroupCommand, RelativeSource={RelativeSource Self}}\"", xaml);
        Assert.DoesNotContain("<Border.ContextMenu>", xaml);
    }
}
