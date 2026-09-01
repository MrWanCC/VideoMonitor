using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Playback;

public sealed class PlaybackUrlBuilderTests
{
    [Fact]
    public async Task FormalUsesPlaybackBaseUrlFormalAppAndVmStream()
    {
        var builder = CreateBuilder(
            playbackBaseUrl: "rtsp://playback.example:8554",
            zlmApiBaseUrl: "http://admin:secret@zlm.example:80");

        var result = await builder.BuildAsync(
            new PlaybackMediaIdentity("vhost", "videomonitor", "vm_formal"),
            CreateTicket("ticket-value", "videomonitor", "vm_formal"));

        Assert.Equal(
            "rtsp://playback.example:8554/videomonitor/vm_formal?ticket=ticket-value",
            result.AbsoluteUri);
    }

    [Fact]
    public async Task TestUsesPlaybackBaseUrlTestAppAndTestStream()
    {
        var builder = CreateBuilder("rtsp://playback.example:8554");

        var result = await builder.BuildAsync(
            new PlaybackMediaIdentity("vhost", "videomonitor-test", "test_stream"),
            CreateTicket("test-ticket", "videomonitor-test", "test_stream"));

        Assert.Equal(
            "rtsp://playback.example:8554/videomonitor-test/test_stream?ticket=test-ticket",
            result.AbsoluteUri);
    }

    [Fact]
    public async Task NeverUsesZlmApiBaseUrl()
    {
        var builder = CreateBuilder(
            playbackBaseUrl: "rtsp://playback.example:8554",
            zlmApiBaseUrl: "http://zlm.example:8080");

        var result = await builder.BuildAsync(
            new PlaybackMediaIdentity("vhost", "app", "stream"),
            CreateTicket("ticket", "app", "stream"));

        Assert.DoesNotContain("zlm.example", result.AbsoluteUri, StringComparison.Ordinal);
        Assert.StartsWith("rtsp://playback.example", result.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsPlaybackBaseUrlWithUserInfo()
    {
        var builder = CreateBuilder("rtsp://user:password@playback.example:8554");

        await Assert.ThrowsAsync<InvalidDataException>(() => builder.BuildAsync(
            new PlaybackMediaIdentity("vhost", "app", "stream"),
            CreateTicket("ticket", "app", "stream")));
    }

    [Fact]
    public async Task DoesNotContainCameraOrZlmCredentials()
    {
        var builder = CreateBuilder(
            playbackBaseUrl: "rtsp://playback.example:8554");

        var result = await builder.BuildAsync(
            new PlaybackMediaIdentity("vhost", "app", "stream"),
            CreateTicket("ticket-value", "app", "stream"));

        Assert.DoesNotContain("password", result.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin_params", result.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", result.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private static PlaybackUrlBuilder CreateBuilder(
        string playbackBaseUrl,
        string zlmApiBaseUrl = "http://127.0.0.1:1985") =>
        new(new FixedRuntimeSettingsProvider(new MediaRuntimeSettings(
            zlmApiBaseUrl,
            playbackBaseUrl,
            "vhost",
            "videomonitor",
            "videomonitor-test",
            "zlm-secret",
            30,
            1)));

    private static PlaybackTicket CreateTicket(
        string value,
        string app,
        string stream) =>
        new(value, "vhost", app, stream, DateTimeOffset.UtcNow.AddSeconds(60));

    private sealed class FixedRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        private readonly MediaRuntimeSettings settings;

        public FixedRuntimeSettingsProvider(MediaRuntimeSettings settings) =>
            this.settings = settings;

        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }
}
