using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Media;

public sealed record TestStreamStartRequest(
    Guid? ExistingDeviceId,
    Guid? ExistingChannelId,
    CameraDeviceDraftDto Draft,
    DateTimeOffset RequestedAtUtc);

public sealed record CameraDeviceDraftDto(
    string IpAddress,
    int RtspPort,
    string Username,
    string? Password,
    int ChannelNo,
    StreamType StreamType,
    TransportMode TransportMode);

public sealed record TestStreamProxyHandle(
    string Vhost,
    string App,
    string StreamId,
    string ProxyKey,
    DateTimeOffset CreatedAtUtc);

public enum TestStreamErrorCode
{
    InvalidDraft,
    MediaServerUnavailable,
    AuthFailed,
    ConnectFailed,
    MediaRegistrationTimeout,
    PlaybackPreparationFailed,
    CatalogUnavailable,
    IdentityConflict,
    SessionNotFound
}

public sealed record TestSessionDto(
    Guid SessionId,
    Guid? DeviceId,
    Guid? ChannelId,
    string App,
    string StreamId,
    Uri PlaybackUrl,
    DateTimeOffset ExpiresUtc);
