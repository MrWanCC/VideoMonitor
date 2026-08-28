using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Core.Tests.Services;

public sealed class MonitorSwitchServiceTests
{
    private readonly IReadOnlyList<MonitorGroup> groups = CreateGroups();

    [Fact]
    public void SwitchChuteGroup_ReplacesSlotsOneToThree_AndKeepsSlotFour()
    {
        var service = CreateService();
        var tunnel = service.Current.MainSlots[3];

        service.SwitchChuteGroup(Group("西402溜井"));

        Assert.All(service.Current.MainSlots.Take(3), camera =>
            Assert.Equal("西402溜井", camera.GroupName));
        Assert.Equal(new[] { 1, 2, 3 },
            service.Current.MainSlots.Take(3).Select(camera => camera.ChannelNumber));
        Assert.Same(tunnel, service.Current.MainSlots[3]);
    }

    [Fact]
    public void SwitchTunnel_ReplacesOnlySlotFour()
    {
        var service = CreateService();
        var chute = service.Current.MainSlots.Take(3).ToArray();

        service.SwitchTunnel(Group("Z-2#巷"));

        Assert.Equal(chute, service.Current.MainSlots.Take(3));
        Assert.Equal("Z-2#巷", service.Current.MainSlots[3].Name);
    }

    [Fact]
    public void SwitchUnloadingGroup_ReplacesAllSecondarySlots_AndKeepsMain()
    {
        var service = CreateService();
        var main = service.Current.MainSlots.ToArray();

        service.SwitchUnloadingGroup(Group("3#主溜井"));

        Assert.Equal(main, service.Current.MainSlots);
        Assert.All(service.Current.SecondarySlots, camera =>
            Assert.Equal("3#主溜井", camera.GroupName));
        Assert.Equal(new[] { 1, 2, 3 },
            service.Current.SecondarySlots.Select(camera => camera.ChannelNumber));
    }

    private MonitorSwitchService CreateService() => new(
        Group("备用1"), Group("Z-1#巷"), Group("2#主溜井"));

    private static IReadOnlyList<MonitorGroup> CreateGroups()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        return MonitorCatalogProjection.CreateGroups(catalog);
    }

    private MonitorGroup Group(string name) => groups.Single(group => group.Name == name);
}
