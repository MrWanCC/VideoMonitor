using System.Net;
using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class TestStreamApiClientTests
{
    [Fact]
    public async Task StartPostsDraftToServerAndParsesSafeSession()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = JsonSerializer.Serialize(new TestSessionDto(
                Guid.NewGuid(),
                null,
                null,
                "videomonitor-test",
                "test_0123456789abcdef0123456789abcdef",
                new Uri("rtsp://playback.example/live"),
                DateTimeOffset.UtcNow.AddMinutes(2)))
        };
        using var client = new HttpClient(handler);
        var api = new TestStreamApiClient(client);

        var session = await api.StartAsync(
            new Uri("https://server/"),
            new TestStreamStartRequest(
                null,
                null,
                new CameraDeviceDraftDto(
                    "10.0.0.5", 554, "admin", "transient-secret", 1, StreamType.Main, TransportMode.Auto),
                DateTimeOffset.UtcNow));

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/v1/test-streams", handler.RequestUri!.AbsolutePath);
        Assert.Contains("transient-secret", handler.RequestBody);
        Assert.Equal("videomonitor-test", session.App);
        Assert.DoesNotContain("Password", JsonSerializer.Serialize(session));
    }

    [Fact]
    public async Task StopDeletesExactSession()
    {
        var sessionId = Guid.NewGuid();
        var handler = new RecordingHandler { StatusCode = HttpStatusCode.NoContent };
        using var client = new HttpClient(handler);
        var api = new TestStreamApiClient(client);

        await api.StopAsync(new Uri("https://server/"), sessionId);

        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal($"/api/v1/test-streams/{sessionId}", handler.RequestUri!.AbsolutePath);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public string ResponseBody { get; init; } = string.Empty;

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody)
            };
        }
    }
}
