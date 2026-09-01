using System.Net;
using System.Text;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Tests.Media;

public sealed class ZlmTransportSecurityTests
{
    [Fact]
    public async Task AddStreamProxyDoesNotLogZlmSecret()
    {
        var handler = new RecordingHandler("{\"code\":0,\"data\":{\"key\":\"proxy-key\"}}");
        using var transport = new ZlmServerHttpTransport(handler);
        var gateway = new ZlmClient(transport, new FakeRuntimeSettingsProvider());

        var result = await gateway.AddStreamProxyAsync(
            "custom-vhost",
            "videomonitor",
            "stream-1",
            new Uri("rtsp://camera-user:fake-camera-password@fake-camera-host/live"));

        Assert.True(result.IsSuccess);
        Assert.Contains("fake-zlm-secret", handler.LastRequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-zlm-secret", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddStreamProxyDoesNotLogCameraSourceUri()
    {
        var handler = new RecordingHandler("{\"code\":0,\"data\":{\"key\":\"proxy-key\"}}");
        using var transport = new ZlmServerHttpTransport(handler);
        var gateway = new ZlmClient(transport, new FakeRuntimeSettingsProvider());

        var result = await gateway.AddStreamProxyAsync(
            "custom-vhost",
            "videomonitor",
            "stream-1",
            new Uri("rtsp://camera-user:fake-camera-password@fake-camera-host/live"));

        Assert.True(result.IsSuccess);
        Assert.Contains("fake-camera-password", handler.LastRequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("fake-camera-host", handler.LastRequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-camera-password", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-camera-host", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpFailureDoesNotExposeRequestUri()
    {
        var handler = new RecordingHandler((request, _) =>
            throw new HttpRequestException($"request failed: {request.RequestUri}"));
        using var transport = new ZlmServerHttpTransport(handler);
        var gateway = new ZlmClient(transport, new FakeRuntimeSettingsProvider());

        var result = await gateway.AddStreamProxyAsync(
            "custom-vhost",
            "videomonitor",
            "stream-1",
            new Uri("rtsp://camera-user:fake-camera-password@fake-camera-host/live"));

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("fake-zlm-secret", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-camera-password", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-camera-host", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("http://127.0.0.1", result.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://127.0.0.1:8080",
                "rtsp://127.0.0.1:8554",
                "configured-vhost",
                "configured-app",
                "configured-test-app",
                "fake-zlm-secret",
                30,
                1));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory;

        public RecordingHandler(string responseBody)
            : this((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            })
        {
        }

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request, cancellationToken));
        }
    }
}
