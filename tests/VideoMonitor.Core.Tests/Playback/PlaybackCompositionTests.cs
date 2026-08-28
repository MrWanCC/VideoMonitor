using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class PlaybackCompositionTests
{
    [Fact]
    public void SelectDevice_SelectsOnlyWest401FirstPhysicalCamera()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var localDevice = new LocalDeviceOptions
        {
            DeviceId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            ChannelId = Guid.Parse("60000000-0000-0000-0000-000000000001"),
            LocalIdentifier = "camera001",
            IpAddress = "192.0.2.20",
            RtspPort = 554,
            Username = "admin",
            Password = "test-password",
            ChannelNo = 1
        };

        var selection = SingleCameraPlaybackComposition.SelectDevice(catalog, localDevice);

        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            selection.Device.Id);
        Assert.Equal("西401溜井 · 通道1", selection.Device.Name);
        Assert.Equal(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            selection.Channel.Id);
        Assert.Single(selection.Device.Channels);
    }
}
