using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MainHeaderControlStateTests
{
    [Fact]
    public void ToggleSignalLinkageCommand_TogglesFalseTrueFalse()
    {
        var (main, _, _, _) = CreateFixture();

        Assert.False(main.IsSignalLinkageEnabled);

        main.ToggleSignalLinkageCommand.Execute(null);
        Assert.True(main.IsSignalLinkageEnabled);

        main.ToggleSignalLinkageCommand.Execute(null);
        Assert.False(main.IsSignalLinkageEnabled);
    }

    [Fact]
    public void ToggleSecondaryScreenCommand_TogglesVisibilityState()
    {
        var (main, _, _, _) = CreateFixture();

        Assert.False(main.IsSecondaryScreenVisible);

        main.ToggleSecondaryScreenCommand.Execute(null);
        Assert.True(main.IsSecondaryScreenVisible);

        main.ToggleSecondaryScreenCommand.Execute(null);
        Assert.False(main.IsSecondaryScreenVisible);
    }

    [Fact]
    public void HeaderToggles_DoNotChangeMonitorOrSecondarySelections()
    {
        var (main, monitor, secondary, service) = CreateFixture();
        var before = Snapshot(monitor, secondary, service);

        main.ToggleSignalLinkageCommand.Execute(null);
        main.ToggleSecondaryScreenCommand.Execute(null);
        main.ToggleSignalLinkageCommand.Execute(null);
        main.ToggleSecondaryScreenCommand.Execute(null);

        Assert.Equal(before, Snapshot(monitor, secondary, service));
    }

    private static (MainViewModel Main, MonitorViewModel Monitor, SecondaryMonitorViewModel Secondary, MonitorSwitchService Service) CreateFixture()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var service = new MonitorSwitchService(
            Group(groups, "备用1"),
            Group(groups, "Z-1#巷"),
            Group(groups, "2#主溜井"));
        var monitor = new MonitorViewModel(service, groups, catalog);
        var secondary = new SecondaryMonitorViewModel(service, groups, catalog);
        var main = new MainViewModel(
            monitor,
            new DeviceManagementViewModel(catalog));

        return (main, monitor, secondary, service);
    }

    private static string Snapshot(
        MonitorViewModel monitor,
        SecondaryMonitorViewModel secondary,
        MonitorSwitchService service)
    {
        var mainSlots = string.Join('|', service.Current.MainSlots.Select(camera => camera.Name));
        var secondarySlots = string.Join('|', service.Current.SecondarySlots.Select(camera => camera.Name));
        return $"{monitor.CurrentChuteName};{monitor.CurrentTunnelName};{secondary.CurrentGroupName};{mainSlots};{secondarySlots}";
    }

    private static MonitorGroup Group(IReadOnlyList<MonitorGroup> groups, string name) =>
        groups.Single(group => group.Name == name);
}
