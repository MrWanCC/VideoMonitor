using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class PlaybackRuntimeEventTests
{
    [Fact]
    public void LibVlcEventsBecomeStablePlayingStoppedFailedEvents()
    {
        var channelId = Guid.NewGuid();

        Assert.Equal(
            new PlaybackRuntimeEvent(
                channelId,
                PlaybackRuntimeEventKind.Playing,
                null),
            PlaybackRuntimeEvent.ForPlaying(channelId));
        Assert.Equal(
            new PlaybackRuntimeEvent(
                channelId,
                PlaybackRuntimeEventKind.Stopped,
                null),
            PlaybackRuntimeEvent.ForStopped(channelId));
        Assert.Equal(
            new PlaybackRuntimeEvent(
                channelId,
                PlaybackRuntimeEventKind.Failed,
                "PLAYBACK_ENGINE_FAILED"),
            PlaybackRuntimeEvent.ForFailed(channelId));
    }

    [Fact]
    public void DisposedSessionDoesNotPublishLaterRuntimeEvents()
    {
        var callbacks = new List<Action>();
        var published = 0;
        callbacks.Add(() => published++);
        using var session = new PlaybackSession(
            new PlaybackSource(
                Guid.NewGuid(),
                "stream",
                new Uri("https://server-b/live/stream"),
                null,
                false),
            null,
            null,
            () => callbacks.Clear());

        session.Dispose();
        foreach (var callback in callbacks.ToArray())
        {
            callback();
        }

        Assert.Equal(0, published);
    }
}
