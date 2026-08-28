using VideoMonitor.Client.Mock;
using VideoMonitor.Client.Models;
using VideoMonitor.Client.Services;

namespace VideoMonitor.Client.Tests.Services;

public sealed class MonitorSwitchServiceTests
{
    private readonly IReadOnlyList<MonitorGroup> groups = MockMonitorData.CreateGroups();

    [Fact]
    public void SwitchShaftGroup_ReplacesFirstThreeMainSlots_AndKeepsTunnel()
    {
        var service = CreateService();
        var originalTunnel = service.Current.MainSlots[3];

        service.SwitchShaftGroup(Group("西402溜井"));

        Assert.Equal(new[] { 1, 2, 3 }, service.Current.MainSlots.Take(3).Select(x => x.ChannelNumber));
        Assert.All(service.Current.MainSlots.Take(3), x => Assert.Equal("西402溜井", x.GroupName));
        Assert.Same(originalTunnel, service.Current.MainSlots[3]);
    }

    [Fact]
    public void SwitchTunnel_ReplacesOnlyFourthMainSlot()
    {
        var service = CreateService();
        var originalShaft = service.Current.MainSlots.Take(3).ToArray();

        service.SwitchTunnel(Group("Z-2#巷"));

        Assert.Equal(originalShaft, service.Current.MainSlots.Take(3));
        Assert.Equal("Z-2#巷", service.Current.MainSlots[3].Name);
    }

    [Fact]
    public void SwitchUnloadingGroup_ReplacesAllSecondarySlots_AndKeepsMainSlots()
    {
        var service = CreateService();
        var originalMain = service.Current.MainSlots.ToArray();

        service.SwitchUnloadingGroup(Group("3#主溜井"));

        Assert.Equal(originalMain, service.Current.MainSlots);
        Assert.All(service.Current.SecondarySlots, x => Assert.Equal("3#主溜井", x.GroupName));
        Assert.Equal(new[] { 1, 2, 3 }, service.Current.SecondarySlots.Select(x => x.ChannelNumber));
    }

    private MonitorSwitchService CreateService() => new(
        Group("备用1"), Group("Z-1#巷"), Group("2#主溜井"));

    private MonitorGroup Group(string name) => groups.Single(x => x.Name == name);
}
