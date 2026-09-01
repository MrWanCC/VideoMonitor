using VideoMonitor.Core.Models;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed record CameraMediaCredential(
    Guid DeviceId,
    Guid ChannelId,
    string IpAddress,
    int RtspPort,
    string Username,
    string Password,
    int ChannelNo,
    StreamType StreamType,
    TransportMode TransportMode);
