using System.Net;
using System.Text;
using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class MediaSettingsApiClientTests
{
    [Fact]
    public async Task GetAndPutUseVersionedMediaSettingsPaths()
    {
        var handler = new RecordingHttpMessageHandler
        {
            ResponseBody = JsonSerializer.Serialize(new MediaSettingsDto(
                "http://127.0.0.1:8080",
                "rtsp://media.example.test:554",
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                true,
                30,
                2))
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaSettingsApiClient(httpClient);

        await client.GetAsync(new Uri("https://server-b/"));
        await client.UpdateAsync(
            new Uri("https://server-b/"),
            new UpdateMediaSettingsRequest(
                "http://127.0.0.1:8080",
                "rtsp://media.example.test:554",
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                "Candidate-Secret",
                30,
                1));
        handler.ResponseBody = JsonSerializer.Serialize(
            new MediaSettingsTestResult(true, null));
        await client.TestAsync(
            new Uri("https://server-b/"),
            new TestMediaSettingsRequest(
                "http://127.0.0.1:8080",
                "rtsp://media.example.test:554",
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                "Candidate-Secret",
                30));

        Assert.Equal(
            [HttpMethod.Get, HttpMethod.Put, HttpMethod.Post],
            handler.Requests.Select(request => request.Method));
        Assert.Equal(
            "/api/v1/media/settings",
            handler.Requests[0].RequestUri.AbsolutePath);
        Assert.Equal(
            "/api/v1/media/settings",
            handler.Requests[1].RequestUri.AbsolutePath);
        Assert.Equal(
            "/api/v1/media/settings/test",
            handler.Requests[2].RequestUri.AbsolutePath);
        Assert.Contains("Candidate-Secret", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("Candidate-Secret", handler.Requests[2].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConflictResponseMapsMediaSettingsCodeAndRevision()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Conflict,
            ResponseBody = "{\"code\":\"MEDIA_SETTINGS_REVISION_CONFLICT\",\"message\":\"secret detail\",\"currentRevision\":7}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaSettingsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.UpdateAsync(
                new Uri("https://server-b/"),
                new UpdateMediaSettingsRequest(
                    "http://127.0.0.1:8080",
                    "rtsp://media.example.test:554",
                    "__defaultVhost__",
                    "videomonitor",
                    "videomonitor-test",
                    "Candidate-Secret",
                    30,
                    6)));

        Assert.Equal("MEDIA_SETTINGS_REVISION_CONFLICT", exception.Code);
        Assert.Equal(7, exception.CurrentRevision);
        Assert.DoesNotContain("secret detail", exception.Message);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = string.Empty;
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? Body);
}
