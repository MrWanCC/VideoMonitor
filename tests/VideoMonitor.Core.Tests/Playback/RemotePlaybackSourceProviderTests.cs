using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class RemotePlaybackSourceProviderTests
{
    [Fact]
    public async Task PrepareUsesIdsAndServerTicketWithoutZlmDependency()
    {
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var response = new EnsurePlaybackStreamResponse(
            "formal-stream",
            new Uri("https://server-b/api/v1/playback/media/formal-stream?ticket=safe"),
            DateTimeOffset.UtcNow.AddSeconds(60),
            StreamRuntimeState.Ready);
        var handler = new RecordingHandler(JsonSerializer.Serialize(response));
        using var httpClient = new HttpClient(handler);
        var apiClient = new CatalogApiClient(httpClient);
        var provider = new RemotePlaybackSourceProvider(
            apiClient,
            () => new Uri("https://server-b/"));

        var source = await provider.PrepareAsync(
            deviceId,
            channelId,
            StreamType.Sub);

        Assert.Equal(deviceId, source.DeviceId);
        Assert.Equal(channelId, source.ChannelId);
        Assert.Equal(response.StreamId, source.StreamId);
        Assert.Equal(response.PlaybackUrl, source.PlaybackUrl);
        Assert.Equal(response.ExpiresAtUtc, source.TicketExpiresUtc);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/playback/streams/ensure", request.RequestUri.AbsolutePath);
        using var payload = JsonDocument.Parse(request.Body!);
        Assert.Equal(deviceId.ToString(), payload.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal(channelId.ToString(), payload.RootElement.GetProperty("channelId").GetString());
        Assert.Equal((int)StreamType.Sub, payload.RootElement.GetProperty("streamType").GetInt32());
    }

    [Fact]
    public async Task ReleaseDoesNotDeleteFormalServerStream()
    {
        var handler = new RecordingHandler("{}");
        using var httpClient = new HttpClient(handler);
        var provider = new RemotePlaybackSourceProvider(
            new CatalogApiClient(httpClient),
            () => new Uri("https://server-b/"));

        await provider.ReleaseAsync(new FormalPlaybackSource(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "formal-stream",
            new Uri("https://server-b/playback/formal-stream?ticket=safe"),
            DateTimeOffset.UtcNow.AddMinutes(1)));

        Assert.Empty(handler.Requests);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string responseBody;

        public RecordingHandler(string responseBody)
        {
            this.responseBody = responseBody;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? Body);
}
