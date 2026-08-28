using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Wpf.Playback;

public sealed record SingleCameraPlaybackSelection(
    CameraDevice Device,
    CameraChannel Channel);

public static class SingleCameraPlaybackComposition
{
    public static SingleCameraPlaybackSelection SelectDevice(
        IDeviceCatalog catalog,
        LocalDeviceOptions localDevice)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(localDevice);

        var device = catalog.GetDevice(localDevice.DeviceId)
            ?? throw new InvalidOperationException("本地播放配置对应的设备不存在。");
        var channel = device.Channels.SingleOrDefault(item => item.Id == localDevice.ChannelId)
            ?? throw new InvalidOperationException("本地播放配置对应的通道不存在。");
        return new SingleCameraPlaybackSelection(device, channel);
    }
}
