using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Catalog;

public sealed record CameraChannelDto(
    Guid Id,
    Guid DeviceId,
    int ChannelNo,
    string ChannelName,
    StreamType StreamType,
    bool Enabled);
