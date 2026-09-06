namespace VideoMonitor.Wpf.Playback;

public sealed record TestPreviewSource(
    Guid? ChannelId,
    string StreamId,
    Uri PlaybackUrl)
{
    public PlaybackSource ToPlaybackSource() =>
        new(ChannelId ?? Guid.Empty, StreamId, PlaybackUrl, null, false);
}

internal sealed class LazyPlaybackEngine : IPlaybackEngine, IAsyncPlaybackStopper, IDisposable, IAsyncDisposable
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

    public async ValueTask StopAsync(PlaybackSession session)
    {
        if (inner is IAsyncPlaybackStopper asyncStopper)
        {
            await asyncStopper.StopAsync(session).ConfigureAwait(false);
            return;
        }

        inner?.Stop(session);
    }

    public void Dispose()
    {
        try
        {
            if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        finally
        {
            inner = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (inner is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        finally
        {
            inner = null;
        }
    }
}
