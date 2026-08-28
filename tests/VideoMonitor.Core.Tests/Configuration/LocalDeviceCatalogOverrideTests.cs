using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Core.Tests.Configuration;

public sealed class LocalDeviceCatalogOverrideTests
{
    [Fact]
    public void Apply_UpdatesCatalogDeviceAndChannelByStableIds()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var device = data.Devices.Single(item => item.Name == "西401溜井 · 通道1");
        var channel = Assert.Single(device.Channels);
        var options = new LocalDeviceOptions
        {
            DeviceId = device.Id,
            ChannelId = channel.Id,
            LocalIdentifier = "camera001",
            IpAddress = "192.0.2.20",
            RtspPort = 8554,
            Username = "test-user",
            Password = "test-password",
            ChannelNo = 2,
            StreamType = VideoMonitor.Core.Models.StreamType.Sub
        };

        LocalDeviceCatalogOverride.Apply(catalog, options);

        var actual = catalog.GetDevice(device.Id)!;
        var actualChannel = Assert.Single(actual.Channels);
        Assert.Equal("192.0.2.20", actual.IpAddress);
        Assert.Equal(8554, actual.RtspPort);
        Assert.Equal("test-user", actual.Username);
        Assert.Equal("test-password", actual.Password);
        Assert.Equal(2, actualChannel.ChannelNo);
        Assert.Equal(VideoMonitor.Core.Models.StreamType.Sub, actualChannel.StreamType);
    }
}
