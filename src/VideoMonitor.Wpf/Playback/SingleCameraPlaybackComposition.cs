using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.Playback;

public sealed record SingleCameraPlaybackSelection(
    CameraDevice Device,
    CameraChannel Channel);

public static class SingleCameraPlaybackComposition
{
    public static SingleCameraPlaybackSelection SelectDevice(
        IDeviceCatalog catalog,
        Guid deviceId,
        Guid channelId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var device = catalog.GetDevice(deviceId)
            ?? throw new InvalidOperationException("本地播放配置对应的设备不存在。");
        var channel = device.Channels.SingleOrDefault(item => item.Id == channelId)
            ?? throw new InvalidOperationException("本地播放配置对应的通道不存在。");
        return new SingleCameraPlaybackSelection(device, channel);
    }
}
