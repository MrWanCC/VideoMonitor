using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Mock;

public sealed record MockDeviceDataSet(
    IReadOnlyList<DeviceGroup> Groups,
    IReadOnlyList<CameraDevice> Devices);
