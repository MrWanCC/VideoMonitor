using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class SingleCameraPlaybackCoordinatorTests
{
    [Fact]
    public void ShowError_ExposesSpecificTitleAndDetail()
    {
        var tile = new VideoTileViewModel();

        tile.ShowError("拉流失败", "摄像头RTSP连接超时");

        Assert.Equal(PlaybackState.Error, tile.PlaybackState);
        Assert.Equal("拉流失败", tile.PlaybackErrorTitle);
        Assert.Equal("摄像头RTSP连接超时", tile.PlaybackErrorDetail);
    }

    [Fact]
    public async Task StartAndDispose_PreparesPlaysAndReleasesExactlyOnce()
    {
        var source = new PlaybackSource(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            "device_1_channel_1",
            new Uri("rtsp://127.0.0.1/live/device_1_channel_1"),
            "owned-key",
            true);
        var provider = new FakePlaybackSourceProvider(source);
        var engine = new FakePlaybackEngine();
        var coordinator = new SingleCameraPlaybackCoordinator(provider, engine);
        var tile = new VideoTileViewModel();

        await coordinator.StartAsync(Device(), Channel(), tile, CancellationToken.None);

        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, engine.StartCount);
        Assert.Equal(PlaybackState.Playing, tile.PlaybackState);
        Assert.Same(coordinator.CurrentSession, tile.PlaybackSession);

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, engine.StopCount);
        Assert.Equal(1, provider.ReleaseCount);
    }

    [Fact]
    public async Task Start_WhenProviderFails_ShowsStageSpecificError()
    {
        var provider = new FakePlaybackSourceProvider(
            new PlaybackSourceException(
                PlaybackFailureStage.ZlmUnavailable,
                "ZLMediaKit不可连接",
                "连接被拒绝"));
        var coordinator = new SingleCameraPlaybackCoordinator(
            provider,
            new FakePlaybackEngine());
        var tile = new VideoTileViewModel();

        await coordinator.StartAsync(Device(), Channel(), tile, CancellationToken.None);

        Assert.Equal(PlaybackState.Error, tile.PlaybackState);
        Assert.Equal("ZLMediaKit不可连接", tile.PlaybackErrorTitle);
        Assert.Equal("连接被拒绝", tile.PlaybackErrorDetail);
    }

    private static CameraDevice Device() => new()
    {
        Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
        Name = "西401溜井 · 通道1"
    };

    private static CameraChannel Channel() => new()
    {
        Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
        DeviceId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
        ChannelNo = 1
    };

    private sealed class FakePlaybackSourceProvider : IPlaybackSourceProvider
    {
        private readonly PlaybackSource? source;
        private readonly PlaybackSourceException? exception;

        public FakePlaybackSourceProvider(PlaybackSource source)
        {
            this.source = source;
        }

        public FakePlaybackSourceProvider(PlaybackSourceException exception)
        {
            this.exception = exception;
        }

        public int PrepareCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task<PlaybackSource> PrepareAsync(
            CameraDevice device,
            CameraChannel channel,
            CancellationToken cancellationToken)
        {
            PrepareCount++;
            return exception is null
                ? Task.FromResult(source!)
                : Task.FromException<PlaybackSource>(exception);
        }

        public Task ReleaseAsync(
            PlaybackSource playbackSource,
            CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaybackEngine : IPlaybackEngine
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public PlaybackSession Start(PlaybackSource source)
        {
            StartCount++;
            return new PlaybackSession(source, media: null, mediaPlayer: null);
        }

        public void Stop(PlaybackSession session)
        {
            StopCount++;
            session.Dispose();
        }
    }
}
