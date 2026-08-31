using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class CatalogApiClientTests
{
    [Fact]
    public async Task CheckReadyAsync_Ready200_Completes()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.OK,
            ResponseBody = "{\"status\":\"ready\"}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        await client.CheckReadyAsync(new Uri("https://server-b/"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/health/ready", request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task CheckReadyAsync_NonSuccess_MapsUnavailableSafely()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ResponseBody = "{\"status\":\"not-ready\",\"databaseReady\":false}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.CheckReadyAsync(new Uri("https://server-b/")));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("not-ready", exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetCatalogAsync_DeserializesRootKindAndChildNullKind()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.OK,
            ResponseBody = JsonSerializer.Serialize(new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(rootId, "Root", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)
                ],
                []))
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        var snapshot = await client.GetCatalogAsync(new Uri("https://server-b/"));

        var root = Assert.Single(snapshot.Groups, group => group.Id == rootId);
        var child = Assert.Single(snapshot.Groups, group => group.Id == childId);
        Assert.Equal(MonitorGroupType.Chute, root.Kind);
        Assert.Null(child.Kind);
        Assert.Equal(rootId, child.ParentId);
        Assert.Equal("/api/v1/catalog", Assert.Single(handler.Requests).RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task ConflictResponse_MapsCodeAndRevision()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Conflict,
            ResponseBody = "{\"code\":\"GROUP_REVISION_CONFLICT\",\"message\":\"DO NOT EXPOSE SERVER MESSAGE\",\"currentRevision\":7}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.UpdateGroupAsync(
                new Uri("https://server-b/"),
                Guid.NewGuid(),
                new UpdateGroupRequest("Group", null, 0, true, MonitorGroupType.Chute, 6)));

        Assert.Equal("GROUP_REVISION_CONFLICT", exception.Code);
        Assert.Equal(7, exception.CurrentRevision);
        Assert.DoesNotContain("DO NOT EXPOSE SERVER MESSAGE", exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task MalformedErrorResponse_MapsCatalogUnavailableWithoutBodyDisclosure()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = "TOP-SECRET-PASSWORD\nC:\\server\\catalog.db\nnot-json"
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.GetCatalogAsync(new Uri("https://server-b/")));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("TOP-SECRET-PASSWORD", exception.Message);
        Assert.DoesNotContain("catalog.db", exception.Message);
        Assert.DoesNotContain("not-json", exception.Message);
    }

    [Fact]
    public async Task UnknownErrorCode_MapsCatalogUnavailable()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Conflict,
            ResponseBody = "{\"code\":\"SOME_UNKNOWN_SERVER_CODE\",\"message\":\"secret\"}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.DeleteDeviceAsync(new Uri("https://server-b/"), Guid.NewGuid(), 3));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("secret", exception.Message);
    }

    [Fact]
    public async Task TransportFailure_MapsCatalogUnavailable()
    {
        var handler = new RecordingHttpMessageHandler
        {
            ExceptionToThrow = new HttpRequestException("SECRET host/path detail")
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.GetCatalogAsync(new Uri("https://server-b/")));

        Assert.Equal("CATALOG_UNAVAILABLE", exception.Code);
        Assert.DoesNotContain("SECRET", exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new RecordingHttpMessageHandler
        {
            ThrowOnCancellation = true
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetCatalogAsync(new Uri("https://server-b/"), cancellation.Token));
    }

    [Fact]
    public async Task WriteFailures_AreNotRetried()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ResponseBody = "{}"
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);

        await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.CreateGroupAsync(
                new Uri("https://server-b/"),
                new CreateGroupRequest(Guid.NewGuid(), "Group", null, 0, true, MonitorGroupType.Chute)));
        await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.UpdateGroupAsync(
                new Uri("https://server-b/"),
                Guid.NewGuid(),
                new UpdateGroupRequest("Group", null, 0, true, MonitorGroupType.Chute, 1)));
        await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.DeleteDeviceAsync(new Uri("https://server-b/"), Guid.NewGuid(), 1));

        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(
            [HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete],
            handler.Requests.Select(request => request.Method));
    }

    [Fact]
    public async Task ExplicitBaseUri_IsUsedPerRequestWithoutMutatingHttpClientBaseAddress()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.OK,
            ResponseBody = JsonSerializer.Serialize(new CatalogSnapshotDto([], []))
        };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://should-not-be-used/")
        };
        var client = new CatalogApiClient(httpClient);

        await client.GetCatalogAsync(new Uri("https://server-b:7443/"));

        Assert.Equal(
            new Uri("https://server-b:7443/api/v1/catalog"),
            Assert.Single(handler.Requests).RequestUri);
        Assert.Equal(new Uri("https://should-not-be-used/"), httpClient.BaseAddress);
    }

    [Fact]
    public async Task DeleteRequests_SendExpectedRevisionAsQuery()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.NoContent
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        await client.DeleteGroupAsync(new Uri("https://server-b/"), groupId, 17);
        await client.DeleteDeviceAsync(new Uri("https://server-b/"), deviceId, 19);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Equal(
            $"/api/v1/device-groups/{groupId}?expectedRevision=17",
            handler.Requests[0].RequestUri.PathAndQuery);
        Assert.Equal(
            $"/api/v1/devices/{deviceId}?expectedRevision=19",
            handler.Requests[1].RequestUri.PathAndQuery);
    }

    [Fact]
    public async Task WriteRequests_SerializeKindAndExpectedRevision()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.OK,
            ResponseBody = JsonSerializer.Serialize(
                new DeviceGroupDto(
                    Guid.NewGuid(),
                    "Group",
                    null,
                    0,
                    true,
                    MonitorGroupType.Chute,
                    2))
        };
        using var httpClient = new HttpClient(handler);
        var client = new CatalogApiClient(httpClient);
        var groupId = Guid.NewGuid();

        await client.CreateGroupAsync(
            new Uri("https://server-b/"),
            new CreateGroupRequest(groupId, "Group", null, 0, true, MonitorGroupType.Chute));
        await client.UpdateGroupAsync(
            new Uri("https://server-b/"),
            groupId,
            new UpdateGroupRequest("Updated", null, 1, true, MonitorGroupType.Tunnel, 7));

        using var createJson = JsonDocument.Parse(handler.Requests[0].Body!);
        using var updateJson = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(
            (int)MonitorGroupType.Chute,
            createJson.RootElement.GetProperty("kind").GetInt32());
        Assert.Equal(groupId.ToString(), createJson.RootElement.GetProperty("id").GetString());
        Assert.Equal(
            (int)MonitorGroupType.Tunnel,
            updateJson.RootElement.GetProperty("kind").GetInt32());
        Assert.Equal(7, updateJson.RootElement.GetProperty("expectedRevision").GetInt64());
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = string.Empty;
        public Exception? ExceptionToThrow { get; init; }
        public bool ThrowOnCancellation { get; init; }
        public List<RecordedRequest> Requests { get; } = [];
        public int RequestCount => Requests.Count;

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
