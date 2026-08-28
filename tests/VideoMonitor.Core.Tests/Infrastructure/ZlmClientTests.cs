using System.Net;
using VideoMonitor.Infrastructure.ZLMediaKit;

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
}
