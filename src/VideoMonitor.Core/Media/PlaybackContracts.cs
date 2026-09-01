using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Media;

public sealed record EnsurePlaybackStreamRequest(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType);

public sealed record PlaybackMediaIdentity(
    string Vhost,
    string App,
    string Stream);

public sealed record EnsurePlaybackStreamResponse(
    string StreamId,
    Uri PlaybackUrl,
    DateTimeOffset ExpiresAtUtc,
    StreamRuntimeState RuntimeState);
