using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Tests;

public sealed class CatalogApiTests
{
    [Fact]
    public async Task GetCatalog_ReturnsSnapshot()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CatalogSnapshotDto>();
        Assert.NotNull(body);
        Assert.Empty(body.Groups);
        Assert.Empty(body.Devices);
    }

    [Fact]
    public async Task GetCatalog_SerializesRootKindAndChildNullKind()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var rootResponse = await client.PostAsJsonAsync(
            "/api/v1/device-groups",
            new CreateGroupRequest(
                rootId,
                "Chute Root",
                null,
                0,
                true,
                MonitorGroupType.Chute));
        rootResponse.EnsureSuccessStatusCode();
        var childResponse = await client.PostAsJsonAsync(
            "/api/v1/device-groups",
            new CreateGroupRequest(childId, "Child", rootId, 0, true, null));
        childResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CatalogSnapshotDto>();
        Assert.NotNull(body);
        var root = Assert.Single(body.Groups, group => group.Id == rootId);
        var child = Assert.Single(body.Groups, group => group.Id == childId);
        Assert.Equal(MonitorGroupType.Chute, root.Kind);
        Assert.Null(child.Kind);
        Assert.Equal(rootId, child.ParentId);
    }

    [Fact]
    public async Task GetGroupsAndDevices_ReturnsCollectionsAndSupportsGroupFilter()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var device = await CreateDeviceAsync(client, group.Id);

        var groupsResponse = await client.GetAsync("/api/v1/device-groups");
        var devicesResponse = await client.GetAsync("/api/v1/devices");
        var filteredResponse = await client.GetAsync(
            $"/api/v1/devices?groupId={group.Id}");

        Assert.Equal(HttpStatusCode.OK, groupsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, devicesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var groups = await groupsResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<DeviceGroupDto>>();
        var devices = await devicesResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<CameraDeviceDto>>();
        var filtered = await filteredResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<CameraDeviceDto>>();
        Assert.NotNull(groups);
        Assert.NotNull(devices);
        Assert.NotNull(filtered);
        Assert.Contains(groups, item => item.Id == group.Id);
        Assert.Contains(devices, item => item.Id == device.Id);
        Assert.Single(filtered);
        Assert.Equal(device.Id, filtered[0].Id);
    }

    [Fact]
    public async Task GetDevice_ReturnsSafeDto_AndMissingDeviceReturnsNotFound()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var device = await CreateDeviceAsync(client, group.Id);

        var response = await client.GetAsync($"/api/v1/devices/{device.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CameraDeviceDto>();
        Assert.NotNull(body);
        Assert.Equal(device.Id, body.Id);
        Assert.False(body.HasPassword);
        Assert.Equal(2, body.Channels.Count);
        await AssertSafeResponseBodyAsync(response);

        var missingResponse = await client.GetAsync(
            $"/api/v1/devices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        await AssertErrorAsync(missingResponse, "DEVICE_NOT_FOUND");
    }

    [Fact]
    public async Task GroupCrud_ReturnsRevisionsAndDeletesGroup()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var groupId = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/device-groups",
            new CreateGroupRequest(
                groupId,
                "Group A",
                null,
                0,
                true,
                MonitorGroupType.Chute));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<DeviceGroupDto>();
        Assert.NotNull(created);
        Assert.Equal(groupId, created.Id);
        Assert.Equal(1, created.Revision);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/device-groups/{groupId}",
            new UpdateGroupRequest(
                "Group B",
                null,
                1,
                true,
                MonitorGroupType.Chute,
                1));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DeviceGroupDto>();
        Assert.NotNull(updated);
        Assert.Equal("Group B", updated.Name);
        Assert.Equal(2, updated.Revision);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/device-groups/{groupId}?expectedRevision=2");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(string.Empty, await deleteResponse.Content.ReadAsStringAsync());

        var groupsResponse = await client.GetAsync("/api/v1/device-groups");
        var groups = await groupsResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<DeviceGroupDto>>();
        Assert.NotNull(groups);
        Assert.DoesNotContain(groups, item => item.Id == groupId);
    }

    [Fact]
    public async Task DeviceCrud_ReturnsRevisionsAndDeletesDevice()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var device = await CreateDeviceAsync(client, group.Id);

        var updateRequest = new UpdateDeviceRequest(
            group.Id,
            "Camera Updated",
            "192.168.1.20",
            8001,
            554,
            "operator",
            null,
            "Hikvision",
            "IPC",
            TransportMode.Tcp,
            true,
            "updated",
            1,
            CreateChannels(device.Id));
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/devices/{device.Id}",
            updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CameraDeviceDto>();
        Assert.NotNull(updated);
        Assert.Equal("Camera Updated", updated.Name);
        Assert.Equal(2, updated.Revision);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/devices/{device.Id}?expectedRevision=2");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/devices/{device.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        await AssertErrorAsync(getResponse, "DEVICE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateDevice_WithEmptyNewPassword_ReturnsValidationErrorWithoutChangingRevision()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var device = await CreateDeviceAsync(client, group.Id);

        var updateRequest = new UpdateDeviceRequest(
            group.Id,
            "Must Not Persist",
            "192.168.1.20",
            8001,
            554,
            "operator",
            string.Empty,
            "Hikvision",
            "IPC",
            TransportMode.Tcp,
            true,
            "updated",
            1,
            CreateChannels(device.Id));
        var response = await client.PutAsJsonAsync(
            $"/api/v1/devices/{device.Id}",
            updateRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
        var unchanged = await client.GetFromJsonAsync<CameraDeviceDto>(
            $"/api/v1/devices/{device.Id}");
        Assert.NotNull(unchanged);
        Assert.Equal(1, unchanged.Revision);
        Assert.Equal(device.Name, unchanged.Name);
    }

    [Fact]
    public async Task MalformedJson_ReturnsStableValidationError()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(
            "{ \"id\":",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/devices", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task PostDeviceGroup_WithoutContentType_ReturnsStableValidationError()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/device-groups");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task PostDevice_WithNonJsonContentType_ReturnsStableValidationError()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"id\":\"not-json\"}");

        var response = await client.PostAsync("/api/v1/devices", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task InvalidEnum_ReturnsStableValidationError()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var payload = $$"""
            {
              "id": "{{deviceId}}",
              "groupId": "{{groupId}}",
              "name": "Camera",
              "ipAddress": "192.168.1.10",
              "sdkPort": 8000,
              "rtspPort": 554,
              "username": "operator",
              "password": "",
              "manufacturer": "Hikvision",
              "model": "IPC",
              "transportMode": 999,
              "enabled": true,
              "remark": "",
              "channels": []
            }
            """;
        using var content = new StringContent(
            payload,
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/devices", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("GET", "/api/v1/devices/not-a-guid")]
    [InlineData("PUT", "/api/v1/devices/not-a-guid")]
    [InlineData("DELETE", "/api/v1/device-groups/not-a-guid?expectedRevision=1")]
    public async Task InvalidRouteGuid_ReturnsStableValidationError(
        string method,
        string uri)
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(
                new UpdateDeviceRequest(
                    Guid.NewGuid(),
                    "Group",
                    "192.168.1.10",
                    8000,
                    554,
                    "operator",
                    null,
                    "Hikvision",
                    "IPC",
                    TransportMode.Tcp,
                    true,
                    "",
                    1,
                    Array.Empty<CameraChannelInput>()));
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("/api/v1/devices?groupId=")]
    [InlineData("/api/v1/devices?groupId=not-a-guid")]
    public async Task InvalidGroupIdQuery_ReturnsStableValidationError(string uri)
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("/api/v1/devices/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/v1/devices/00000000-0000-0000-0000-000000000001?expectedRevision=abc")]
    [InlineData("/api/v1/device-groups/00000000-0000-0000-0000-000000000001?expectedRevision=")]
    public async Task MissingOrInvalidExpectedRevision_ReturnsStableValidationError(
        string uri)
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync(uri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task NotReadyCatalogEndpoints_Return503BeforeCallingRepository()
    {
        var repository = new CountingRepository();
        using var baseFactory = new TestServerFactory(failMachineProtection: true);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICentralCatalogRepository>();
                services.AddSingleton<ICentralCatalogRepository>(repository);
            }));
        using var client = factory.CreateClient();
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog"),
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/device-groups"),
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/device-groups")
            {
                Content = JsonContent.Create(
                    new CreateGroupRequest(groupId, "Group", null, 0, true))
            },
            new HttpRequestMessage(HttpMethod.Put, $"/api/v1/device-groups/{groupId}")
            {
                Content = JsonContent.Create(
                    new UpdateGroupRequest("Group", null, 0, true, 1))
            },
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/v1/device-groups/{groupId}?expectedRevision=1"),
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/devices"),
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/devices?groupId={groupId}"),
            new HttpRequestMessage(HttpMethod.Get, $"/api/v1/devices/{deviceId}"),
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices")
            {
                Content = JsonContent.Create(CreateDeviceRequest(groupId, deviceId))
            },
            new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{deviceId}")
            {
                Content = JsonContent.Create(
                    CreateUpdateDeviceRequest(groupId, deviceId, null))
            },
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/v1/devices/{deviceId}?expectedRevision=1")
        };

        foreach (var request in requests)
        {
            using (request)
            {
                var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertErrorAsync(response, "CATALOG_UNAVAILABLE");
            }
        }

        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task NotReady_InvalidGuidAndMalformedJson_Return503BeforeParsing()
    {
        var repository = new CountingRepository();
        using var baseFactory = new TestServerFactory(failMachineProtection: true);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICentralCatalogRepository>();
                services.AddSingleton<ICentralCatalogRepository>(repository);
            }));
        using var client = factory.CreateClient();

        var invalidGuidResponse = await client.GetAsync(
            "/api/v1/devices/not-a-guid");
        using var malformedContent = new StringContent(
            "{ \"id\":",
            Encoding.UTF8,
            "application/json");
        var malformedResponse = await client.PostAsync(
            "/api/v1/devices",
            malformedContent);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, invalidGuidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, malformedResponse.StatusCode);
        await AssertErrorAsync(invalidGuidResponse, "CATALOG_UNAVAILABLE");
        await AssertErrorAsync(malformedResponse, "CATALOG_UNAVAILABLE");
        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task NotReady_NonJsonBody_Returns503BeforeContentTypeValidation()
    {
        var repository = new CountingRepository();
        using var baseFactory = new TestServerFactory(failMachineProtection: true);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICentralCatalogRepository>();
                services.AddSingleton<ICentralCatalogRepository>(repository);
            }));
        using var client = factory.CreateClient();
        using var content = new StringContent("not-json");

        var response = await client.PostAsync("/api/v1/devices", content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertErrorAsync(response, "CATALOG_UNAVAILABLE");
        Assert.Equal(0, repository.CallCount);
    }

    private static async Task<DeviceGroupDto> CreateGroupAsync(HttpClient client)
    {
        var rootId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/api/v1/device-groups",
            new CreateGroupRequest(rootId, "Root", null, 0, true, MonitorGroupType.Chute));
        response.EnsureSuccessStatusCode();
        var childResponse = await client.PostAsJsonAsync(
            "/api/v1/device-groups",
            new CreateGroupRequest(Guid.NewGuid(), "Group", rootId, 0, true, null));
        childResponse.EnsureSuccessStatusCode();
        return (await childResponse.Content.ReadFromJsonAsync<DeviceGroupDto>())!;
    }

    private static async Task<CameraDeviceDto> CreateDeviceAsync(
        HttpClient client,
        Guid groupId)
    {
        var request = CreateDeviceRequest(groupId, Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/api/v1/devices", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CameraDeviceDto>())!;
    }

    private static CreateDeviceRequest CreateDeviceRequest(Guid groupId, Guid deviceId) =>
        new(
            deviceId,
            groupId,
            "Camera",
            "192.168.1.10",
            8000,
            554,
            "operator",
            string.Empty,
            "Hikvision",
            "IPC",
            TransportMode.Tcp,
            true,
            "",
            CreateChannels(deviceId));

    private static UpdateDeviceRequest CreateUpdateDeviceRequest(
        Guid groupId,
        Guid deviceId,
        string? newPassword) =>
        new(
            groupId,
            "Camera",
            "192.168.1.10",
            8000,
            554,
            "operator",
            newPassword,
            "Hikvision",
            "IPC",
            TransportMode.Tcp,
            true,
            "",
            1,
            CreateChannels(deviceId));

    private static IReadOnlyList<CameraChannelInput> CreateChannels(Guid deviceId) =>
        new[]
        {
            new CameraChannelInput(
                Guid.NewGuid(),
                1,
                "Main",
                StreamType.Main,
                true),
            new CameraChannelInput(
                Guid.NewGuid(),
                1,
                "Sub",
                StreamType.Sub,
                true)
        };

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        var body = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();
        Assert.NotNull(body);
        Assert.Equal(expectedCode, body.Code);
        Assert.NotEqual(string.Empty, body.Message);
        await AssertSafeResponseBodyAsync(response);
    }

    private static async Task AssertSafeResponseBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"password\":", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordCiphertext", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"status\":", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("streamId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProblemDetails", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("traceId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CountingRepository : ICentralCatalogRepository
    {
        private int callCount;

        public int CallCount => callCount;

        public Task<CatalogSnapshotDto> GetCatalogAsync(
            CancellationToken cancellationToken = default) => ThrowAsync<CatalogSnapshotDto>();

        public Task<DeviceGroupDto?> GetGroupAsync(
            Guid id,
            CancellationToken cancellationToken = default) => ThrowAsync<DeviceGroupDto?>();

        public Task<CameraDeviceDto?> GetDeviceAsync(
            Guid id,
            CancellationToken cancellationToken = default) => ThrowAsync<CameraDeviceDto?>();

        public Task<CatalogRepositoryResult<DeviceGroupDto>> CreateGroupAsync(
            DeviceGroup group,
            CancellationToken cancellationToken = default) =>
            ThrowAsync<CatalogRepositoryResult<DeviceGroupDto>>();

        public Task<CatalogRepositoryResult<CameraDeviceDto>> CreateDeviceAsync(
            CameraDevice device,
            CancellationToken cancellationToken = default) =>
            ThrowAsync<CatalogRepositoryResult<CameraDeviceDto>>();

        public Task<CatalogRepositoryResult<DeviceGroupDto>> UpdateGroupAsync(
            DeviceGroup group,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            ThrowAsync<CatalogRepositoryResult<DeviceGroupDto>>();

        public Task<CatalogRepositoryDeleteResult> DeleteGroupAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            ThrowAsync<CatalogRepositoryDeleteResult>();

        public Task<CatalogRepositoryResult<CameraDeviceDto>> UpdateDeviceAsync(
            CameraDevice device,
            string? newPassword,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            ThrowAsync<CatalogRepositoryResult<CameraDeviceDto>>();

        public Task<CatalogRepositoryDeleteResult> DeleteDeviceAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            ThrowAsync<CatalogRepositoryDeleteResult>();

        private Task<T> ThrowAsync<T>()
        {
            Interlocked.Increment(ref callCount);
            throw new InvalidOperationException("Repository must not be called before readiness.");
        }
    }
}
