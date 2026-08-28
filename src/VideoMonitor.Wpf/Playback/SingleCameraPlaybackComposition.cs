using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Wpf.Playback;

public sealed record SingleCameraPlaybackSelection(
    CameraDevice Device,
    CameraChannel Channel);

public static class SingleCameraPlaybackComposition
{
    private static readonly Guid Camera01DeviceId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    public static SingleCameraPlaybackSelection SelectDevice(
        MockDeviceDataSet data,
        LocalDeviceOptions localDevice)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(localDevice);

        var device = data.Devices.Single(candidate => candidate.Id == Camera01DeviceId);
        var channel = device.Channels.Single();
        return new SingleCameraPlaybackSelection(device, channel);
    }
}
