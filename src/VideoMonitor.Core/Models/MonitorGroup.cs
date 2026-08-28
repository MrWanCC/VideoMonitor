namespace VideoMonitor.Core.Models;

public sealed record MonitorGroup(
    string Name,
    MonitorGroupType Type,
    IReadOnlyList<CameraInfo> Cameras);
