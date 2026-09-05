using System.Net;
using System.Text;
using System.Text.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class MediaDiagnosticsApiClientTests
{
    private static readonly Uri BaseUri = new("https://server-b:7443/");
    private static readonly Guid DeviceId =
        Guid.Parse("a2000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("b2000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task MediaDiagnosticsApiClientReadsSafeDto()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = JsonSerializer.Serialize(CreateSnapshot())
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var result = await client.GetDiagnosticsAsync(BaseUri);

        Assert.Equal(1, result.ActiveStreamCount);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        Assert.Equal(
            "/api/v1/media/diagnostics",
            handler.Requests[0].RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task RefreshUsesCorrectEndpoint()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.Accepted
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        await client.RequestRefreshAsync(BaseUri);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/media/diagnostics/refresh", request.RequestUri.AbsolutePath);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task RetryUsesCompleteStableIdentity()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.Accepted
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        await client.RetryFaultedAsync(
            BaseUri,
            DeviceId,
            ChannelId,
            StreamType.Sub);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"/api/v1/media/diagnostics/streams/{DeviceId}/{ChannelId}/sub/retry",
            request.RequestUri.AbsolutePath);
        Assert.DoesNotContain("name", request.RequestUri.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task RefreshAccepts202()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.Accepted
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        await client.RequestRefreshAsync(BaseUri);
    }

    [Fact]
    public async Task RetryAccepts202WithEmptyBody()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.Accepted,
            ResponseBody = string.Empty
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        await client.RetryFaultedAsync(
            BaseUri,
            DeviceId,
            ChannelId,
            StreamType.Main);
    }

    [Fact]
    public async Task ServerUnavailableMapsToSafeException()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ResponseBody = "{\"code\":\"MEDIA_DIAGNOSTICS_UNAVAILABLE\","
                + "\"message\":\"Password=secret; rtsp://camera\"}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(
            () => client.GetDiagnosticsAsync(BaseUri));

        Assert.Equal("MEDIA_DIAGNOSTICS_UNAVAILABLE", exception.Code);
        Assert.Equal("Catalog API request failed.", exception.Message);
        Assert.DoesNotContain("secret", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryConflictPreservesSafeCode()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.Conflict,
            ResponseBody = "{\"code\":\"MEDIA_STREAM_NOT_FAULTED\","
                + "\"message\":\"internal detail\"}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(
            () => client.RetryFaultedAsync(BaseUri, DeviceId, ChannelId, StreamType.Main));

        Assert.Equal("MEDIA_STREAM_NOT_FAULTED", exception.Code);
        Assert.DoesNotContain("internal detail", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryNotFoundPreservesSafeCode()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.NotFound,
            ResponseBody = "{\"code\":\"MEDIA_STREAM_NOT_FOUND\","
                + "\"message\":\"internal detail\"}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(
            () => client.RetryFaultedAsync(BaseUri, DeviceId, ChannelId, StreamType.Main));

        Assert.Equal("MEDIA_STREAM_NOT_FOUND", exception.Code);
        Assert.DoesNotContain("internal detail", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedSuccessJsonIsSafeFailure()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = "not-json"
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(
            () => client.GetDiagnosticsAsync(BaseUri));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("not-json", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientDoesNotExposeRawErrorBody()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = "Password=secret; rtsp://user:secret@camera"
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(
            () => client.GetDiagnosticsAsync(BaseUri));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("secret", exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new RecordingHandler
        {
            ThrowOnCancellation = true
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetDiagnosticsAsync(BaseUri, cancellation.Token));
    }

    [Fact]
    public async Task TransportFailureDoesNotExposeRawUriOrCredentials()
    {
        var handler = new RecordingHandler
        {
            ExceptionToThrow = new HttpRequestException(
                "Password=secret; rtsp://user:secret@camera")
        };
        using var httpClient = new HttpClient(handler);
        var client = new MediaDiagnosticsApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(
            () => client.GetDiagnosticsAsync(BaseUri));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("secret", exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(exception.InnerException);
    }

    private static MediaDiagnosticsSnapshotDto CreateSnapshot() =>
        new(
            MediaServerHealth.Healthy,
            1,
            2,
            0,
            new[]
            {
                new MediaStreamDiagnosticsDto(
                    DeviceId,
                    ChannelId,
                    StreamType.Main,
                    StreamRuntimeState.Ready,
                    2,
                    StreamOwnership.OwnedCurrentProcess,
                    null,
                    SourceObservation.Reachable,
                    null,
                    null,
                    null,
                    null,
                    false)
            });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = string.Empty;
        public Exception? ExceptionToThrow { get; init; }
        public bool ThrowOnCancellation { get; init; }
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (ThrowOnCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

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
