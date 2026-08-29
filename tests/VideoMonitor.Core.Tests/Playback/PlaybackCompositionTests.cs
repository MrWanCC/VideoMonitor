using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class PlaybackCompositionTests
{
    [Fact]
    public void SelectDevice_SelectsOnlyWest401FirstPhysicalCamera()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var selection = SingleCameraPlaybackComposition.SelectDevice(
            catalog,
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"));

        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            selection.Device.Id);
        Assert.Equal("西401溜井 · 通道1", selection.Device.Name);
        Assert.Equal(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            selection.Channel.Id);
        Assert.Single(selection.Device.Channels);
    }

    [Fact]
    public void SelectDevice_WhenDeviceDoesNotExist_Throws()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SingleCameraPlaybackComposition.SelectDevice(
                catalog,
                Guid.Parse("50000000-0000-0000-0000-000000000099"),
                Guid.Parse("60000000-0000-0000-0000-000000000001")));

        Assert.Contains("设备不存在", exception.Message);
    }

    [Fact]
    public void SelectDevice_WhenChannelDoesNotExist_Throws()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SingleCameraPlaybackComposition.SelectDevice(
                catalog,
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000099")));

        Assert.Contains("通道不存在", exception.Message);
    }
}
