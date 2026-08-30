using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Catalog;

public sealed record CameraDeviceDto(
    Guid Id,
    Guid GroupId,
    string Name,
    string IpAddress,
    int SdkPort,
    int RtspPort,
    string Username,
    bool HasPassword,
    string Manufacturer,
    string Model,
    TransportMode TransportMode,
    bool Enabled,
    string Remark,
    long Revision,
    IReadOnlyList<CameraChannelDto> Channels);
