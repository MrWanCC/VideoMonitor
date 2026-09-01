namespace VideoMonitor.Core.Tests.Views;

public sealed class ServerSettingsWindowStructureTests
{
    [Fact]
    public void ServerSettingsWindow_UsesDarkCustomChromeAndClearStatusArea()
    {
        var xaml = ReadXaml();

        Assert.Contains("WindowStyle=\"None\"", xaml);
        Assert.Contains("shell:WindowChrome.WindowChrome", xaml);
        Assert.Contains("Text=\"服务器地址\"", xaml);
        Assert.Contains(
            "BasedOn=\"{StaticResource IndustrialTextBoxStyle}\"",
            xaml);
        Assert.Contains("Text=\"连接状态\"", xaml);
        Assert.Contains("ConnectionStatusText", xaml);
        Assert.Contains("StatusIndicator", xaml);
    }

    [Fact]
    public void ServerSettingsWindow_UsesQuietCancelAndPrimarySaveActions()
    {
        var xaml = ReadXaml();

        Assert.Contains(
            "Style=\"{StaticResource BusyDisabledSecondaryButtonStyle}\"",
            xaml);
        Assert.Contains(
            "Style=\"{StaticResource BusyDisabledPrimaryButtonStyle}\"",
            xaml);
        Assert.Contains(
            "Style=\"{StaticResource QuietButtonStyle}\"",
            xaml);
        Assert.Contains("Content=\"测试连接\"", xaml);
        Assert.Contains("Content=\"保存\"", xaml);
        Assert.Contains("Content=\"取消\"", xaml);
    }

    [Fact]
    public void ServerSettingsWindow_UsesCompactPolishedFocusAndStatusTreatment()
    {
        var xaml = ReadXaml();

        Assert.Contains("Height=\"344\"", xaml);
        Assert.Contains("x:Name=\"ServerBaseUrlTextBox\"", xaml);
        Assert.Contains("Loaded=\"FocusAddressInput\"", xaml);
        Assert.Contains(
            "Property=\"FocusVisualStyle\" Value=\"{x:Null}\"",
            xaml);
        Assert.Contains("DialogSecondaryButtonStyle", xaml);
        Assert.Contains("DialogPrimaryButtonStyle", xaml);
        Assert.Contains("BorderBrush=\"{TemplateBinding BorderBrush}\"", xaml);
        Assert.Contains("StatusIconContainerStyle", xaml);
        Assert.Contains("StatusCheckIcon", xaml);
        Assert.Contains("IconShield", xaml);
    }

    private static string ReadXaml()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Views",
            "ServerSettingsWindow.xaml"));
    }
}
