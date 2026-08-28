namespace VideoMonitor.Core.Models;

public sealed record CameraInfo(
    string Name,
    string GroupName,
    int ChannelNumber,
    CameraStatus Status = CameraStatus.Online,
    string Bitrate = "4.2 Mbps",
    string StreamType = "主码流")
{
    public Guid DeviceId { get; init; }

    public Guid ChannelId { get; init; }
}
