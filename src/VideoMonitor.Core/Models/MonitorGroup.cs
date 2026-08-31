namespace VideoMonitor.Core.Models;

public sealed record MonitorGroup(
    string Name,
    MonitorGroupType Type,
    IReadOnlyList<CameraInfo> Cameras)
{
    public Guid GroupId { get; init; }

    public Guid RootGroupId { get; init; }

    public string RootName { get; init; } = string.Empty;

    public int RootSort { get; init; }

    public int Sort { get; init; }
}
