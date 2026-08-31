using System.Net;
using System.Net.Http.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Server.Tests;

public sealed class CatalogConcurrencyTests
{
    [Fact]
    public async Task TwoClients_StaleDeviceUpdate_ReturnsConflictAndPreservesFirstWriter()
    {
        using var factory = new TestServerFactory();
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        var group = await CreateGroupAsync(clientA);
        var created = await CreateDeviceAsync(clientA, group.Id);

        var dtoA = await GetDeviceAsync(clientA, created.Id);
        var dtoB = await GetDeviceAsync(clientB, created.Id);
        Assert.Equal(1, dtoA.Revision);
        Assert.Equal(1, dtoB.Revision);

        var updateA = CreateUpdateRequest(dtoA, "Writer A", "A Main");
        var updateB = CreateUpdateRequest(dtoB, "Writer B", "B Main");

        var responseA = await clientA.PutAsJsonAsync(
            $"/api/v1/devices/{created.Id}",
            updateA);
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var updatedA = await responseA.Content.ReadFromJsonAsync<CameraDeviceDto>();
        Assert.NotNull(updatedA);
        Assert.Equal(2, updatedA.Revision);

        var responseB = await clientB.PutAsJsonAsync(
            $"/api/v1/devices/{created.Id}",
            updateB);
        Assert.Equal(HttpStatusCode.Conflict, responseB.StatusCode);
        var conflict = await responseB.Content.ReadFromJsonAsync<CatalogErrorDto>();
        Assert.NotNull(conflict);
        Assert.Equal("DEVICE_REVISION_CONFLICT", conflict.Code);
        Assert.Equal(2, conflict.CurrentRevision);

        var final = await GetDeviceAsync(clientB, created.Id);
        Assert.Equal("Writer A", final.Name);
        Assert.Equal(2, final.Revision);
        Assert.Equal("A Main", final.Channels.Single(channel =>
            channel.StreamType == StreamType.Main).ChannelName);
        Assert.DoesNotContain(final.Channels, channel =>
            channel.ChannelName == "B Main");
    }

    [Fact]
    public async Task StaleDeviceDelete_ReturnsConflictWithoutDeletingAggregate()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var created = await CreateDeviceAsync(client, group.Id);
        var stale = await GetDeviceAsync(client, created.Id);

        var update = CreateUpdateRequest(stale, "Updated before stale delete", "Main");
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/devices/{created.Id}",
            update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var staleDeleteResponse = await client.DeleteAsync(
            $"/api/v1/devices/{created.Id}?expectedRevision={stale.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, staleDeleteResponse.StatusCode);
        var conflict = await staleDeleteResponse.Content
            .ReadFromJsonAsync<CatalogErrorDto>();
        Assert.NotNull(conflict);
        Assert.Equal("DEVICE_REVISION_CONFLICT", conflict.Code);
        Assert.Equal(2, conflict.CurrentRevision);

        var afterConflict = await GetDeviceAsync(client, created.Id);
        Assert.Equal(2, afterConflict.Revision);
        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/devices/{created.Id}?expectedRevision={afterConflict.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteNonEmptyGroup_ReturnsGroupNotEmptyAndPreservesData()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var device = await CreateDeviceAsync(client, group.Id);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/device-groups/{group.Id}?expectedRevision={group.Revision}");

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        var error = await deleteResponse.Content.ReadFromJsonAsync<CatalogErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("GROUP_NOT_EMPTY", error.Code);

        var groups = await client.GetFromJsonAsync<IReadOnlyList<DeviceGroupDto>>(
            "/api/v1/device-groups");
        Assert.NotNull(groups);
        Assert.Contains(groups, item => item.Id == group.Id);
        var existingDevice = await GetDeviceAsync(client, device.Id);
        Assert.Equal(device.Id, existingDevice.Id);
    }

    private static async Task<DeviceGroupDto> CreateGroupAsync(HttpClient client)
    {
        var rootId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/api/v1/device-groups",
            new CreateGroupRequest(
                rootId,
                "Root",
                null,
                0,
                true,
                MonitorGroupType.Chute));
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
        var deviceId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/api/v1/devices",
            new CreateDeviceRequest(
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
                CreateChannels()));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CameraDeviceDto>())!;
    }

    private static async Task<CameraDeviceDto> GetDeviceAsync(
        HttpClient client,
        Guid deviceId) =>
        (await client.GetFromJsonAsync<CameraDeviceDto>(
            $"/api/v1/devices/{deviceId}"))!;

    private static UpdateDeviceRequest CreateUpdateRequest(
        CameraDeviceDto device,
        string name,
        string mainChannelName) =>
        new(
            device.GroupId,
            name,
            device.IpAddress,
            device.SdkPort,
            device.RtspPort,
            device.Username,
            null,
            device.Manufacturer,
            device.Model,
            device.TransportMode,
            device.Enabled,
            device.Remark,
            device.Revision,
            device.Channels.Select(channel => new CameraChannelInput(
                channel.Id,
                channel.ChannelNo,
                channel.StreamType == StreamType.Main
                    ? mainChannelName
                    : channel.ChannelName,
                channel.StreamType,
                channel.Enabled)).ToArray());

    private static IReadOnlyList<CameraChannelInput> CreateChannels() =>
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
}
