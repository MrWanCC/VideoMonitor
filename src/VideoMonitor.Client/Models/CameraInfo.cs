namespace VideoMonitor.Client.Models;

public sealed record CameraInfo(
    string Name,
    string GroupName,
    int ChannelNumber,
    bool IsOnline = true);
