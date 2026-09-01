using System.Globalization;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Converters;

namespace VideoMonitor.Core.Tests.Views;

public sealed class DeviceViewFinalPolishTests
{
    [Fact]
    public void RootKindConverter_UsesChineseDisplayNames()
    {
        var converter = new MonitorGroupTypeToTextConverter();

        Assert.Equal("卸矿站", converter.Convert(
            MonitorGroupType.UnloadingStation,
            typeof(string),
            string.Empty,
            CultureInfo.InvariantCulture));
        Assert.Equal("溜井", converter.Convert(
            MonitorGroupType.Chute,
            typeof(string),
            string.Empty,
            CultureInfo.InvariantCulture));
        Assert.Equal("巷道", converter.Convert(
            MonitorGroupType.Tunnel,
            typeof(string),
            string.Empty,
            CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DeviceView_RootKindComboBox_UsesDisplayConverter()
    {
        var xaml = ReadXaml();
        var comboStart = xaml.IndexOf(
            "AutomationProperties.AutomationId=\"RootCategoryKindComboBox\"",
            StringComparison.Ordinal);
        var comboEnd = xaml.IndexOf("</ComboBox>", comboStart, StringComparison.Ordinal);

        Assert.True(comboStart >= 0 && comboEnd > comboStart);
        var combo = xaml[comboStart..comboEnd];

        Assert.Contains("ItemsSource=\"{Binding RootKindOptions}\"", combo);
        Assert.Contains("SelectedItem=\"{Binding RootEditKind, Mode=TwoWay}\"", combo);
        Assert.Contains("MonitorGroupTypeTextConverter", combo);
    }

    [Fact]
    public void DeviceView_DoesNotExposePlaceholderTestStreamButton()
    {
        var xaml = ReadXaml();

        Assert.DoesNotContain("Content=\"测试拉流\"", xaml);
        Assert.DoesNotContain("接入ZLMediaKit后启用", xaml);
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
            "Pages",
            "DeviceView.xaml"));
    }
}
