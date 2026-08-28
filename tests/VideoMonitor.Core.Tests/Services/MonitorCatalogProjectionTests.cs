using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Core.Tests.Services;

public sealed class MonitorCatalogProjectionTests
{
    [Fact]
    public void CreateGroups_ProjectsAllRequired3Plus1ChannelsFromCatalog()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);

        var groups = MonitorCatalogProjection.CreateGroups(catalog);

        Assert.Equal(12, groups.Count);
        Assert.Equal(3, groups.Single(group => group.Name == "备用1").Cameras.Count);
        var west401 = groups.Single(group => group.Name == "西401溜井");
        Assert.Equal(3, west401.Cameras.Count);
        Assert.Equal(new[] { 1, 2, 3 }, west401.Cameras.Select(camera => camera.ChannelNumber));
        Assert.Equal(3, groups.Single(group => group.Name == "2#主溜井").Cameras.Count);
        Assert.Single(groups.Single(group => group.Name == "Z-1#巷").Cameras);
    }

    [Fact]
    public void CreateGroups_CarriesOnlyStableDeviceAndChannelAssociations()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var device = catalog.GetDevice(
            data.Devices.Single(item => item.Name == "西401溜井 · 通道1").Id)!;
        var channel = Assert.Single(device.Channels);

        var camera = MonitorCatalogProjection.CreateGroups(catalog)
            .Single(group => group.Name == "西401溜井")
            .Cameras
            .Single(item => item.DeviceId == device.Id);

        Assert.Equal(device.Id, camera.DeviceId);
        Assert.Equal(channel.Id, camera.ChannelId);
    }
}
