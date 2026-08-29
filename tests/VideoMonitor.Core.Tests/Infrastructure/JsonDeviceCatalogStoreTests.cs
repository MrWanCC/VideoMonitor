using System.Security.Cryptography;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class JsonDeviceCatalogStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSnapshotGroupsDevicesChannelsAndGuids()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var snapshot = CreateSnapshot("第一份设备");
            var store = new JsonDeviceCatalogStore(path);

            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(DeviceCatalogSnapshot.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal(snapshot.Groups.Count, loaded.Groups.Count);
            var group = loaded.Groups.Single(item => item.Name == "测试分组");
            var device = Assert.Single(loaded.Devices);
            var channel = Assert.Single(device.Channels);
            Assert.Equal(
                snapshot.Groups.Select(item => item.Id),
                loaded.Groups.Select(item => item.Id));
            Assert.Equal(
                snapshot.Groups.Select(item => item.Name),
                loaded.Groups.Select(item => item.Name));
            Assert.Equal(
                snapshot.Groups.Select(item => item.ParentId),
                loaded.Groups.Select(item => item.ParentId));
            Assert.Equal(
                snapshot.Groups.Select(item => item.Sort),
                loaded.Groups.Select(item => item.Sort));
            Assert.Equal(
                snapshot.Groups.Select(item => item.Enabled),
                loaded.Groups.Select(item => item.Enabled));
            Assert.Equal(snapshot.Groups[1].Id, group.Id);
            Assert.Equal(snapshot.Groups[1].ParentId, group.ParentId);
            Assert.Equal(snapshot.Groups[1].Name, group.Name);
            Assert.Equal(snapshot.Groups[1].Sort, group.Sort);
            Assert.Equal(snapshot.Groups[1].Enabled, group.Enabled);
            Assert.Equal(snapshot.Devices[0].Id, device.Id);
            Assert.Equal(snapshot.Devices[0].Name, device.Name);
            Assert.Equal(snapshot.Devices[0].GroupId, device.GroupId);
            Assert.Equal(snapshot.Devices[0].IpAddress, device.IpAddress);
            Assert.Equal(snapshot.Devices[0].SdkPort, device.SdkPort);
            Assert.Equal(snapshot.Devices[0].RtspPort, device.RtspPort);
            Assert.Equal(snapshot.Devices[0].Username, device.Username);
            Assert.Equal(snapshot.Devices[0].Password, device.Password);
            Assert.Equal(snapshot.Devices[0].Manufacturer, device.Manufacturer);
            Assert.Equal(snapshot.Devices[0].Model, device.Model);
            Assert.Equal(snapshot.Devices[0].TransportMode, device.TransportMode);
            Assert.Equal(CameraStatus.Unknown, device.Status);
            Assert.Equal(snapshot.Devices[0].Enabled, device.Enabled);
            Assert.Equal(snapshot.Devices[0].Remark, device.Remark);
            Assert.Equal(snapshot.Devices[0].Channels[0].Id, channel.Id);
            Assert.Equal(snapshot.Devices[0].Channels[0].DeviceId, channel.DeviceId);
            Assert.Equal(snapshot.Devices[0].Channels[0].ChannelNo, channel.ChannelNo);
            Assert.Equal(snapshot.Devices[0].Channels[0].ChannelName, channel.ChannelName);
            Assert.Equal(snapshot.Devices[0].Channels[0].StreamType, channel.StreamType);
            Assert.Equal(string.Empty, channel.StreamId);
            Assert.Equal(snapshot.Devices[0].Channels[0].Enabled, channel.Enabled);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
            Assert.Contains('\n', json);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAndLoad_DoesNotPersistRuntimeStatusOrDerivedStreamId()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var snapshot = CreateSnapshot("运行状态设备");
            var store = new JsonDeviceCatalogStore(path);

            await store.SaveAsync(snapshot);
            var json = await File.ReadAllTextAsync(path);
            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.DoesNotContain("\"status\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("test-stream", json, StringComparison.Ordinal);
            Assert.Equal(CameraStatus.Warning, snapshot.Devices[0].Status);
            Assert.Equal("test-stream", snapshot.Devices[0].Channels[0].StreamId);
            Assert.Equal(CameraStatus.Unknown, loaded.Devices[0].Status);
            Assert.Equal(string.Empty, loaded.Devices[0].Channels[0].StreamId);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_IgnoresLegacyRuntimeStatusAndStreamId()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "groups": [{
                    "id": "71000000-0000-0000-0000-000000000001",
                    "name": "测试根分类",
                    "parentId": null,
                    "sort": 1,
                    "enabled": true
                  }],
                  "devices": [{
                    "id": "73000000-0000-0000-0000-000000000001",
                    "name": "测试设备",
                    "groupId": "71000000-0000-0000-0000-000000000001",
                    "password": "",
                    "status": "Offline",
                    "channels": [{
                      "id": "74000000-0000-0000-0000-000000000001",
                      "deviceId": "73000000-0000-0000-0000-000000000001",
                      "channelNo": 1,
                      "channelName": "通道1",
                      "streamType": "Main",
                      "streamId": "legacy-stream-id",
                      "enabled": true
                    }]
                  }]
                }
                """);

            var loaded = await new JsonDeviceCatalogStore(path).LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(CameraStatus.Unknown, loaded.Devices[0].Status);
            Assert.Equal(string.Empty, loaded.Devices[0].Channels[0].StreamId);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new JsonDeviceCatalogStore(
                Path.Combine(directory, "missing", "device-catalog.json"));

            var loaded = await store.LoadAsync();

            Assert.Null(loaded);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_ThrowsInvalidDataException_WhenJsonIsMalformed()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            await File.WriteAllTextAsync(path, "{ not valid json");
            var store = new JsonDeviceCatalogStore(path);
            var original = await File.ReadAllTextAsync(path);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => store.LoadAsync());

            Assert.Contains("JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_ThrowsNotSupportedException_WhenSchemaVersionIsUnsupported()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            await File.WriteAllTextAsync(
                path,
                "{\"schemaVersion\":999,\"groups\":[],\"devices\":[]}");
            var store = new JsonDeviceCatalogStore(path);

            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                () => store.LoadAsync());

            Assert.Contains("SchemaVersion", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_ThrowsNotSupportedException_WhenSchemaVersionIsMissing()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            await File.WriteAllTextAsync(
                path,
                "{\"groups\":[],\"devices\":[]}");
            var store = new JsonDeviceCatalogStore(path);

            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                () => store.LoadAsync());

            Assert.Contains("SchemaVersion", exception.Message, StringComparison.Ordinal);
            Assert.Contains("0", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_SecondSaveReplacesFormalFileAndKeepsPreviousFileAsBackup()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var store = new JsonDeviceCatalogStore(path);

            await store.SaveAsync(CreateSnapshot("第一份设备"));
            await store.SaveAsync(CreateSnapshot("第二份设备"));
            await store.SaveAsync(CreateSnapshot("第三份设备"));

            var current = await File.ReadAllTextAsync(path);
            var backup = await File.ReadAllTextAsync(path + ".bak");
            Assert.Contains("第三份设备", current, StringComparison.Ordinal);
            Assert.Contains("第二份设备", backup, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_SerializesConcurrentCallsOnTheSameStore()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var store = new JsonDeviceCatalogStore(path);
            var names = Enumerable.Range(1, 8)
                .Select(index => $"并发设备{index}")
                .ToArray();

            await Task.WhenAll(names.Select(name => store.SaveAsync(
                CreateSnapshot(name, largePayload: true))));

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Contains(loaded.Devices.Single().Name, names);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenReplaceFails_PreservesExistingFormalFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var store = new JsonDeviceCatalogStore(path);
            await store.SaveAsync(CreateSnapshot("原始设备"));
            var original = await File.ReadAllTextAsync(path);
            Directory.CreateDirectory(path + ".bak");

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => store.SaveAsync(CreateSnapshot("新设备")));

            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_DoesNotModifyInputSnapshotObjects()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var snapshot = CreateSnapshot("原始设备");
            var group = snapshot.Groups[1];
            var device = snapshot.Devices[0];
            var channel = device.Channels[0];

            await new JsonDeviceCatalogStore(path).SaveAsync(snapshot);

            Assert.Same(group, snapshot.Groups[1]);
            Assert.Same(device, snapshot.Devices[0]);
            Assert.Same(channel, device.Channels[0]);
            Assert.Equal("原始设备", device.Name);
            Assert.Equal(2, channel.ChannelNo);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_DoesNotLeaveTemporaryFileAfterSuccessfulSave()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            var store = new JsonDeviceCatalogStore(path);

            await store.SaveAsync(CreateSnapshot("设备"));

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void DefaultFilePath_UsesCommonApplicationDataDataDirectory()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VideoMonitor",
            "data",
            "device-catalog.json");

        Assert.Equal(expected, JsonDeviceCatalogStore.DefaultFilePath);
    }

    [Fact]
    public async Task DefaultStore_UsesLocalMachineProtectionScope()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "device-catalog.json");
            await new JsonDeviceCatalogStore(path).SaveAsync(
                CreateSnapshot("LocalMachine设备"));

            var loaded = await new JsonDeviceCatalogStore(
                path,
                DataProtectionScope.LocalMachine).LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal("test-password", loaded.Devices.Single().Password);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static DeviceCatalogSnapshot CreateSnapshot(
        string deviceName,
        bool largePayload = false)
    {
        var rootId = Guid.Parse("71000000-0000-0000-0000-000000000001");
        var groupId = Guid.Parse("72000000-0000-0000-0000-000000000001");
        var deviceId = Guid.Parse("73000000-0000-0000-0000-000000000001");
        var channelId = Guid.Parse("74000000-0000-0000-0000-000000000001");
        var group = new DeviceGroup
        {
            Id = groupId,
            Name = "测试分组",
            ParentId = rootId,
            Sort = 2,
            Enabled = true
        };
        var device = new CameraDevice
        {
            Id = deviceId,
            Name = deviceName,
            GroupId = groupId,
            IpAddress = "192.0.2.10",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "test-user",
            Password = "test-password",
            Manufacturer = "测试厂商",
            Model = "测试型号",
            TransportMode = TransportMode.Tcp,
            Status = CameraStatus.Warning,
            Enabled = true,
            Remark = largePayload
                ? new string('x', 1024 * 1024)
                : "测试备注"
        };
        device.Channels.Add(new CameraChannel
        {
            Id = channelId,
            DeviceId = deviceId,
            ChannelNo = 2,
            ChannelName = "测试通道",
            StreamType = StreamType.Sub,
            StreamId = "test-stream",
            Enabled = true
        });

        return new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups =
            [
                new DeviceGroup
                {
                    Id = rootId,
                    Name = "测试根分类",
                    Sort = 1,
                    Enabled = true
                },
                group
            ],
            Devices = [device]
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
