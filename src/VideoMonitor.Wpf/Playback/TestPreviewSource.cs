namespace VideoMonitor.Wpf.Playback;

public sealed record TestPreviewSource(
    Guid? ChannelId,
    string StreamId,
    Uri PlaybackUrl)
{
    public PlaybackSource ToPlaybackSource() =>
        new(ChannelId ?? Guid.Empty, StreamId, PlaybackUrl, null, false);
}

internal sealed class LazyPlaybackEngine : IPlaybackEngine, IDisposable
{
    private readonly Func<IPlaybackEngine> factory;
    private IPlaybackEngine? inner;

    public LazyPlaybackEngine(Func<IPlaybackEngine> factory)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public PlaybackSession Start(PlaybackSource source)
    {
        inner ??= factory()
            ?? throw new InvalidOperationException("Playback engine factory returned null.");
        return inner.Start(source);
    }

    public void Stop(PlaybackSession session) => inner?.Stop(session);

    public void Dispose()
    {
        if (inner is IDisposable disposable)
        {
            disposable.Dispose();
        }

        inner = null;
    }
}
