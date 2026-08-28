namespace VideoMonitor.Wpf.Playback;

public sealed record PlaybackSource(
    Guid CameraChannelId,
    string StreamId,
    Uri PlaybackUrl,
    string? ProxyKey,
    bool OwnsProxy);
