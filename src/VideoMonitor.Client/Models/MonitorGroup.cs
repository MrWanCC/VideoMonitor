namespace VideoMonitor.Client.Models;

public sealed record MonitorGroup(
    string Name,
    MonitorGroupType Type,
    IReadOnlyList<CameraInfo> Cameras);
