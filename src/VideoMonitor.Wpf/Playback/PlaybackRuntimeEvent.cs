namespace VideoMonitor.Wpf.Playback;

public sealed record PlaybackRuntimeEvent(
    Guid ChannelId,
    PlaybackRuntimeEventKind Kind,
    string? SafeFailureCode)
{
    public static PlaybackRuntimeEvent ForPlaying(Guid channelId) =>
        new(channelId, PlaybackRuntimeEventKind.Playing, null);

    public static PlaybackRuntimeEvent ForStopped(Guid channelId) =>
        new(channelId, PlaybackRuntimeEventKind.Stopped, null);

    public static PlaybackRuntimeEvent ForFailed(Guid channelId) =>
        new(channelId, PlaybackRuntimeEventKind.Failed, "PLAYBACK_ENGINE_FAILED");
}

public enum PlaybackRuntimeEventKind
{
    Playing,
    Stopped,
    Failed
}
