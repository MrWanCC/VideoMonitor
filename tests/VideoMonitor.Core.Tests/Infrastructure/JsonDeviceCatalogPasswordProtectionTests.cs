using System.Security.Cryptography;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class JsonDeviceCatalogPasswordProtectionTests
{
    [Fact]
    public async Task SaveAsync_ProtectsNonEmptyPasswordsWithDpapiEnvelope()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            const string password = "test-password-one";
            await new JsonDeviceCatalogStore(path).SaveAsync(CreateSnapshot(password));

            var json = await File.ReadAllTextAsync(path);

            Assert.Contains("dpapi:v1:", json, StringComparison.Ordinal);
            Assert.DoesNotContain(password, json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsDifferentPasswordsForMultipleDevices()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var snapshot = CreateSnapshot("password-one", "password-two");
            var store = new JsonDeviceCatalogStore(path);

            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(
                snapshot.Devices.Select(device => device.Password),
                loaded.Devices.Select(device => device.Password));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExplicitProtectionScopes_RoundTripIndependently()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var store = new JsonDeviceCatalogStore(
                path,
                DataProtectionScope.LocalMachine);

            await store.SaveAsync(CreateSnapshot("machine-password"));
            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal("machine-password", loaded.Devices.Single().Password);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsEmptyPasswordWithoutProtectionEnvelope()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var store = new JsonDeviceCatalogStore(path);

            await store.SaveAsync(CreateSnapshot(string.Empty));
            var loaded = await store.LoadAsync();
            var json = await File.ReadAllTextAsync(path);

            Assert.NotNull(loaded);
            Assert.Equal(string.Empty, Assert.Single(loaded.Devices).Password);
            Assert.Contains("\"password\": \"\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("dpapi:v1:", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_DoesNotModifyInputPasswordOrSnapshotObjects()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var snapshot = CreateSnapshot("original-password");
            var device = snapshot.Devices[0];

            await new JsonDeviceCatalogStore(path).SaveAsync(snapshot);

            Assert.Same(device, snapshot.Devices[0]);
            Assert.Equal("original-password", device.Password);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidDpapiBase64WithoutExposingIt()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            const string malformedBase64 = "not-base64";
            await WritePasswordJsonAsync(path, $"dpapi:v1:{malformedBase64}");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonDeviceCatalogStore(path).LoadAsync());

            Assert.DoesNotContain(malformedBase64, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsPlaintextPassword()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            const string plaintext = "plain-password";
            await WritePasswordJsonAsync(path, plaintext);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonDeviceCatalogStore(path).LoadAsync());

            Assert.DoesNotContain(plaintext, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsUnknownProtectionVersion()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            await WritePasswordJsonAsync(path, "dpapi:v2:encoded-value");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonDeviceCatalogStore(path).LoadAsync());

            Assert.Contains("dpapi:v2", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsDpapiDataThatCannotBeUnprotected()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var ciphertext = Convert.ToBase64String([1, 2, 3, 4]);
            await WritePasswordJsonAsync(path, $"dpapi:v1:{ciphertext}");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonDeviceCatalogStore(path).LoadAsync());

            Assert.DoesNotContain(ciphertext, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static async Task WritePasswordJsonAsync(string path, string password)
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [
                {
                  "id": "73000000-0000-0000-0000-000000000001",
                  "name": "测试设备",
                  "groupId": "72000000-0000-0000-0000-000000000001",
                  "password": "{{password}}",
                  "channels": []
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, json);
    }

    private static DeviceCatalogSnapshot CreateSnapshot(params string[] passwords)
    {
        var groupId = Guid.Parse("72000000-0000-0000-0000-000000000001");
        var devices = passwords
            .Select((password, index) => new CameraDevice
            {
                Id = Guid.Parse($"73000000-0000-0000-0000-{index + 1:000000000000}"),
                Name = $"测试设备{index + 1}",
                GroupId = groupId,
                IpAddress = $"192.0.2.{index + 10}",
                Username = $"test-user-{index + 1}",
                Password = password,
                Manufacturer = "测试厂商",
                Model = "测试型号"
            })
            .ToArray();

        return new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups =
            [
                new DeviceGroup
                {
                    Id = groupId,
                    Name = "测试分组",
                    Enabled = true
                }
            ],
            Devices = devices
        };
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "VideoMonitorTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
