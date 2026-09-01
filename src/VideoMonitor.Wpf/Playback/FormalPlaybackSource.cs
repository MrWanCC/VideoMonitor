namespace VideoMonitor.Wpf.Playback;

public sealed record FormalPlaybackSource(
    Guid DeviceId,
    Guid ChannelId,
    string StreamId,
    Uri PlaybackUrl,
    DateTimeOffset TicketExpiresUtc);
