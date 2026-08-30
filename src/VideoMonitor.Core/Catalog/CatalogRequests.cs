using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Catalog;

public sealed record CameraChannelInput(
    Guid Id,
    int ChannelNo,
    string ChannelName,
    StreamType StreamType,
    bool Enabled);

public sealed record CreateGroupRequest(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Sort,
    bool Enabled);

public sealed record UpdateGroupRequest(
    string Name,
    Guid? ParentId,
    int Sort,
    bool Enabled,
    long ExpectedRevision);

public sealed record CreateDeviceRequest(
    Guid Id,
    Guid GroupId,
    string Name,
    string IpAddress,
    int SdkPort,
    int RtspPort,
    string Username,
    string Password,
    string Manufacturer,
    string Model,
    TransportMode TransportMode,
    bool Enabled,
    string Remark,
    IReadOnlyList<CameraChannelInput> Channels);

public sealed record UpdateDeviceRequest(
    Guid GroupId,
    string Name,
    string IpAddress,
    int SdkPort,
    int RtspPort,
    string Username,
    string? NewPassword,
    string Manufacturer,
    string Model,
    TransportMode TransportMode,
    bool Enabled,
    string Remark,
    long ExpectedRevision,
    IReadOnlyList<CameraChannelInput> Channels);
