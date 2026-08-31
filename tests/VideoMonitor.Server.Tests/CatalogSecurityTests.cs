using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Server.Tests;

public sealed class CatalogSecurityTests
{
    private const string Secret = "stage5b-secret-P@55";

    [Fact]
    public async Task RealAesDevicePassword_IsProtectedInDatabaseAndAbsentFromApiResponses()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var deviceId = Guid.NewGuid();
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/devices",
            CreateDeviceRequest(group.Id, deviceId, Secret));
        var createBody = await createResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.DoesNotContain(Secret, createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"password\":", createBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordCiphertext", createBody, StringComparison.OrdinalIgnoreCase);
        var created = JsonSerializer.Deserialize<CameraDeviceDto>(
            createBody,
            JsonOptions)!;
        Assert.NotNull(created);
        Assert.True(created.HasPassword);

        var ciphertext = await ReadCiphertextAsync(factory.DatabasePath, deviceId);
        Assert.False(string.IsNullOrEmpty(ciphertext));
        Assert.StartsWith("aesgcm:v1:", ciphertext, StringComparison.Ordinal);
        Assert.NotEqual(Secret, ciphertext);
        Assert.DoesNotContain(Secret, ciphertext, StringComparison.Ordinal);

        var responses = new[]
        {
            await client.GetAsync("/api/v1/catalog"),
            await client.GetAsync("/api/v1/device-groups"),
            await client.GetAsync("/api/v1/devices"),
            await client.GetAsync($"/api/v1/devices/{deviceId}"),
            await client.GetAsync($"/api/v1/devices?groupId={group.Id}")
        };
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertSecretAbsent(body, ciphertext);
        }

        var device = JsonSerializer.Deserialize<CameraDeviceDto>(
            await (await client.GetAsync($"/api/v1/devices/{deviceId}"))
                .Content.ReadAsStringAsync(),
            JsonOptions)!;
        Assert.True(device.HasPassword);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/devices/{deviceId}",
            CreateUpdateRequest(device, null));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        AssertSecretAbsent(
            await updateResponse.Content.ReadAsStringAsync(),
            ciphertext);
        Assert.Equal(
            ciphertext,
            await ReadCiphertextAsync(factory.DatabasePath, deviceId));

        var updated = await client.GetFromJsonAsync<CameraDeviceDto>(
            $"/api/v1/devices/{deviceId}");
        Assert.NotNull(updated);
        Assert.True(updated.HasPassword);

        var staleUpdate = CreateUpdateRequest(updated, Secret) with
        {
            ExpectedRevision = 1
        };
        var errorResponse = await client.PutAsJsonAsync(
            $"/api/v1/devices/{deviceId}",
            staleUpdate);
        Assert.Equal(HttpStatusCode.Conflict, errorResponse.StatusCode);
        AssertSecretAbsent(
            await errorResponse.Content.ReadAsStringAsync(),
            ciphertext);

        foreach (var path in new[]
        {
            factory.DatabasePath,
            factory.DatabasePath + "-wal",
            factory.DatabasePath + "-shm"
        })
        {
            if (File.Exists(path))
            {
                Assert.False(ContainsSequence(
                    ReadBytesForScan(path),
                    Encoding.UTF8.GetBytes(Secret)));
            }
        }
    }

    [Fact]
    public async Task CatalogReads_DoNotCallUnprotectAsync()
    {
        var protector = new CountingNoUnprotectSecretProtector();
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISecretProtector>();
                services.AddSingleton<ISecretProtector>(protector);
            }));
        using var client = factory.CreateClient();
        var group = await CreateGroupAsync(client);
        var deviceId = Guid.NewGuid();
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/devices",
            CreateDeviceRequest(group.Id, deviceId, Secret));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal($"camera-password:{deviceId:N}", protector.CapturedPurpose);

        var responses = new[]
        {
            await client.GetAsync($"/api/v1/devices/{deviceId}"),
            await client.GetAsync("/api/v1/devices"),
            await client.GetAsync("/api/v1/catalog")
        };
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        }

        var device = await client.GetFromJsonAsync<CameraDeviceDto>(
            $"/api/v1/devices/{deviceId}");
        Assert.NotNull(device);
        Assert.True(device.HasPassword);
        Assert.Equal(0, protector.UnprotectCalls);
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

    private static CreateDeviceRequest CreateDeviceRequest(
        Guid groupId,
        Guid deviceId,
        string password) =>
        new(
            deviceId,
            groupId,
            "Camera",
            "192.168.1.10",
            8000,
            554,
            "operator",
            password,
            "Hikvision",
            "IPC",
            TransportMode.Tcp,
            true,
            "",
            new[]
            {
                new CameraChannelInput(
                    Guid.NewGuid(),
                    1,
                    "Main",
                    StreamType.Main,
                    true)
            });

    private static UpdateDeviceRequest CreateUpdateRequest(
        CameraDeviceDto device,
        string? newPassword) =>
        new(
            device.GroupId,
            device.Name,
            device.IpAddress,
            device.SdkPort,
            device.RtspPort,
            device.Username,
            newPassword,
            device.Manufacturer,
            device.Model,
            device.TransportMode,
            device.Enabled,
            device.Remark,
            device.Revision,
            device.Channels.Select(channel => new CameraChannelInput(
                channel.Id,
                channel.ChannelNo,
                channel.ChannelName,
                channel.StreamType,
                channel.Enabled)).ToArray());

    private static async Task<string> ReadCiphertextAsync(
        string databasePath,
        Guid deviceId)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id";
        command.Parameters.AddWithValue("$id", deviceId.ToString("N"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void AssertSecretAbsent(string body, string ciphertext)
    {
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"password\":", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordCiphertext", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ciphertext, body, StringComparison.Ordinal);
        Assert.DoesNotContain("aesgcm:v1:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("videomonitor.db", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("master-key.protected", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.InvalidOperationException", body, StringComparison.Ordinal);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] ReadBytesForScan(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class CountingNoUnprotectSecretProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }

        public int UnprotectCalls { get; private set; }

        public string? CapturedPurpose { get; private set; }

        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            ProtectCalls++;
            CapturedPurpose = purpose;
            return Task.FromResult(
                "test-protected:" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)));
        }

        public Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            UnprotectCalls++;
            throw new InvalidOperationException(
                "UnprotectAsync must never be called by Catalog reads.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);
}
