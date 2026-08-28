namespace VideoMonitor.Wpf.Playback;

public enum PlaybackFailureStage
{
    DeviceConfigurationInvalid,
    ZlmUnavailable,
    ZlmProxyRegistrationFailed,
    ZlmStreamRegistrationTimeout,
    ZlmProxyReleaseFailed
}

public sealed class PlaybackSourceException : Exception
{
    public PlaybackSourceException(
        PlaybackFailureStage stage,
        string title,
        string detail)
        : base($"{title}：{detail}")
    {
        Stage = stage;
        Title = title;
        Detail = detail;
    }

    public PlaybackFailureStage Stage { get; }

    public string Title { get; }

    public string Detail { get; }
}
