using System.Net;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class ZlmClientTests
{
    [Fact]
    public async Task AddStreamProxy_UsesConfiguredScopeTcpAndParsesKey()
    {
        var handler = new StubHttpMessageHandler(
            """{"code":0,"data":{"key":"proxy-key"}}""");
        var client = CreateClient(handler, app: "mine", vhost: "custom");

        var result = await client.AddStreamProxyAsync(
            "stream_1",
            new Uri("rtsp://user:pass@camera/live"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("proxy-key", result.Data!.Key);
        Assert.Contains("app=mine", handler.LastRequestUri!.Query);
        Assert.Contains("vhost=custom", handler.LastRequestUri.Query);
        Assert.Contains("stream=stream_1", handler.LastRequestUri.Query);
        Assert.Contains("rtp_type=0", handler.LastRequestUri.Query);
        Assert.Contains("retry_count=1", handler.LastRequestUri.Query);
        Assert.Contains("enable_rtsp=1", handler.LastRequestUri.Query);
        Assert.Contains("enable_rtmp=0", handler.LastRequestUri.Query);
        Assert.Contains("enable_hls=0", handler.LastRequestUri.Query);
        Assert.Contains("enable_hls_fmp4=0", handler.LastRequestUri.Query);
        Assert.Contains("enable_ts=0", handler.LastRequestUri.Query);
        Assert.Contains("enable_fmp4=0", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task AddStreamProxy_EncodesQueryValues()
    {
        var handler = new StubHttpMessageHandler(
            """{"code":0,"data":{"key":"proxy-key"}}""");
        var client = CreateClient(handler, app: "mine &", vhost: "custom/vhost");

        await client.AddStreamProxyAsync(
            "stream /1",
            new Uri("rtsp://user:pass@camera/live?x=a&b=c"),
            CancellationToken.None);

        var query = handler.LastRequestUri!.Query;
        Assert.Contains($"app={Uri.EscapeDataString("mine &")}", query);
        Assert.Contains($"vhost={Uri.EscapeDataString("custom/vhost")}", query);
        Assert.Contains($"stream={Uri.EscapeDataString("stream /1")}", query);
        Assert.Contains(
            $"url={Uri.EscapeDataString("rtsp://user:pass@camera/live?x=a&b=c")}",
            query);
    }

    [Fact]
    public async Task AddStreamProxy_PropagatesZlmError()
    {
        var handler = new StubHttpMessageHandler(
            """{"code":-1,"msg":"proxy failed"}""");

        var result = await CreateClient(handler).AddStreamProxyAsync(
            "stream_1",
            CameraUri(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(-1, result.Code);
        Assert.Equal("proxy failed", result.Message);
    }

    [Fact]
    public async Task GetMediaList_UsesConfiguredScopeAndParsesMatchingStream()
    {
        var json = """
            {"code":0,"data":[{"schema":"rtsp","vhost":"custom","app":"mine","stream":"stream_1"}]}
            """;
        var handler = new StubHttpMessageHandler(json);

        var result = await CreateClient(handler, app: "mine", vhost: "custom")
            .GetMediaListAsync("stream_1", CancellationToken.None);

        var stream = Assert.Single(result.Data!);
        Assert.Equal("rtsp", stream.Schema);
        Assert.Equal("custom", stream.Vhost);
        Assert.Equal("mine", stream.App);
        Assert.Equal("stream_1", stream.Stream);
        Assert.Contains("app=mine", handler.LastRequestUri!.Query);
        Assert.Contains("vhost=custom", handler.LastRequestUri.Query);
        Assert.Contains("stream=stream_1", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task GetMediaListParsesCompleteEvidenceWithoutLoggingOriginUrl()
    {
        var handler = new StubHttpMessageHandler(
            """
            {"code":0,"data":[{"schema":"rtsp","vhost":"custom","app":"mine","stream":"stream_1","originType":4,"originTypeStr":"rtsp_pull","originUrl":"rtsp://fake-camera-user:fake-camera-password@fake-camera-host/live","createStamp":123456789,"aliveSecond":42,"totalReaderCount":3}]}
            """);

        var result = await CreateClient(handler, app: "mine", vhost: "custom")
            .GetMediaListAsync("stream_1", CancellationToken.None);

        var stream = Assert.Single(result.Data!);
        Assert.Equal(4, stream.OriginType);
        Assert.Equal("rtsp_pull", stream.OriginTypeStr);
        Assert.Equal("rtsp://fake-camera-user:fake-camera-password@fake-camera-host/live", stream.OriginUrl);
        Assert.Equal(123456789, stream.CreateStamp);
        Assert.Equal(42, stream.AliveSecond);
        Assert.Equal(3, stream.TotalReaderCount);
        Assert.DoesNotContain("fake-camera-password", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormalGetMediaListMapsCompleteEvidence()
    {
        var handler = new StubHttpMessageHandler(
            """
            {"code":0,"data":[{"schema":"rtsp","vhost":"custom","app":"mine","stream":"stream_1","originType":4,"originTypeStr":"rtsp_pull","originUrl":"rtsp://fake-camera-user:fake-camera-password@fake-camera-host/live","createStamp":123456789,"aliveSecond":42,"totalReaderCount":3}]}
            """);
        using var transport = new ZlmServerHttpTransport(handler);
        var gateway = new ZlmClient(
            transport,
            new FakeRuntimeSettingsProvider());

        var result = await gateway.GetMediaListAsync(
            "custom",
            "mine",
            "stream_1");

        var evidence = Assert.Single(result.Data!);
        Assert.Equal("rtsp", evidence.Schema);
        Assert.Equal("custom", evidence.Vhost);
        Assert.Equal("mine", evidence.App);
        Assert.Equal("stream_1", evidence.Stream);
        Assert.Equal(4, evidence.OriginType);
        Assert.Equal("rtsp_pull", evidence.OriginTypeStr);
        Assert.Equal("rtsp://fake-camera-user:fake-camera-password@fake-camera-host/live", evidence.OriginUrl);
        Assert.Equal(123456789, evidence.CreateStamp);
        Assert.Equal(42, evidence.AliveSecond);
        Assert.Equal(3, evidence.TotalReaderCount);
        Assert.Contains("secret=fake-secret", handler.LastRequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckServer_HttpFailureReturnsStatusWithoutRequestUrl()
    {
        var handler = new StubHttpMessageHandler(
            (HttpStatusCode.ServiceUnavailable, "service unavailable"));

        var result = await CreateClient(handler).CheckServerAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, result.HttpStatusCode);
        Assert.DoesNotContain("secret", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fake-secret", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteStreamProxy_SendsReturnedKey()
    {
        var handler = new StubHttpMessageHandler(
            """{"code":0,"data":{"flag":true}}""");

        var result = await CreateClient(handler)
            .DeleteStreamProxyAsync("owned-key", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.Flag);
        Assert.Contains("key=owned-key", handler.LastRequestUri!.Query);
    }

    private static ZlmClient CreateClient(
        HttpMessageHandler handler,
        string app = "live",
        string vhost = "__defaultVhost__") => new(
        new HttpClient(handler),
        new ZlmOptions
        {
            BaseUrl = "http://127.0.0.1:8080",
            Secret = "fake-secret",
            Vhost = vhost,
            App = app,
            RtspHost = "127.0.0.1",
            RtspPort = 554
        });

    private static Uri CameraUri() => new("rtsp://user:password@camera/live");

    private sealed class FakeRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://127.0.0.1:8080",
                "rtsp://127.0.0.1:8554",
                "custom",
                "mine",
                "mine-test",
                "fake-secret",
                30,
                1));
    }
}
