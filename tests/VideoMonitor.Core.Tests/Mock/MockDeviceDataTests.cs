using VideoMonitor.Core.Mock;

namespace VideoMonitor.Core.Tests.Mock;

public sealed class MockDeviceDataTests
{
    [Fact]
    public void Create_ReturnsFixedCategoriesAndBusinessGroups()
    {
        var data = MockDeviceData.Create();
        var roots = data.Groups.Where(group => group.ParentId is null).ToArray();

        Assert.Equal(
            new[] { "卸矿站监控", "溜井监控", "巷道监控" },
            roots.OrderBy(group => group.Sort).Select(group => group.Name));
        Assert.Contains(data.Groups, group => group.Name == "西401溜井" && group.ParentId is not null);
        Assert.Contains(data.Groups, group => group.Name == "2#主溜井" && group.ParentId is not null);
    }

    [Fact]
    public void Create_West401ContainsThreePhysicalDevicesWithOneDefaultChannelEach()
    {
        var data = MockDeviceData.Create();
        var group = data.Groups.Single(item => item.Name == "西401溜井");
        var devices = data.Devices.Where(device => device.GroupId == group.Id).ToArray();

        Assert.Equal(3, devices.Length);
        Assert.Equal(
            new[] { "192.168.17.5", "192.168.17.6", "192.168.17.7" },
            devices.Select(device => device.IpAddress));
        Assert.All(devices, device =>
        {
            var channel = Assert.Single(device.Channels);
            Assert.Equal(1, channel.ChannelNo);
            Assert.Equal(device.Id, channel.DeviceId);
        });
    }
}
