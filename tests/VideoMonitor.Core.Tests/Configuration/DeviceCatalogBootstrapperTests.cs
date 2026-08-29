using System.Security.Cryptography;
using System.Text.Json;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Core.Tests.Configuration;

public sealed class DeviceCatalogBootstrapperTests
{
    private static readonly Guid DeviceId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task InitializeAsync_WhenStoreHasData_UsesStoreWithoutSeedingOrSaving()
    {
        using var directory = new TemporaryDirectory();
        var storedSnapshot = CreateSnapshot("Store设备", "198.51.100.10", "stored-password");
        var store = new ScriptedStore(storedSnapshot);
        var seedInvoked = false;
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            directory.PathOf("local-device.json"),
            () =>
            {
                seedInvoked = true;
                throw new InvalidOperationException("不应执行 Mock seed。");
            });

        var catalog = await bootstrapper.InitializeAsync();

        Assert.False(seedInvoked);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal("198.51.100.10", catalog.GetDevice(DeviceId)!.IpAddress);
    }

    [Fact]
    public async Task InitializeAsync_WhenStoreHasDataAndLegacyExists_DoesNotReadOrDeleteLegacy()
    {
        using var directory = new TemporaryDirectory();
        var legacyPath = directory.PathOf("local-device.json");
        await WriteLegacyAsync(legacyPath, "legacy-password", "192.0.2.99");
        var store = new ScriptedStore(CreateSnapshot("Store设备", "198.51.100.10", "stored-password"));
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            legacyPath,
            () => throw new InvalidOperationException("不应执行 Mock seed。") );

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("198.51.100.10", catalog.GetDevice(DeviceId)!.IpAddress);
        Assert.True(File.Exists(legacyPath));
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenStoreMissingAndLegacyMissing_SavesMockAndReloadsIt()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var legacyPath = directory.PathOf("local-device.json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            legacyPath,
            MockDeviceData.Create,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.All(loaded.Devices, device => Assert.NotNull(catalog.GetDevice(device.Id)));
        Assert.Equal(
            "mock-password",
            catalog.GetDevice(Guid.Parse("50000000-0000-0000-0000-000000000001"))!.Password);
        Assert.Equal(
            CameraStatus.Online,
            catalog.GetDevice(Guid.Parse("50000000-0000-0000-0000-000000000001"))!.Status);
        Assert.True(File.Exists(catalogPath));
        Assert.False(File.Exists(legacyPath));
    }

    [Fact]
    public async Task InitializeAsync_WhenStoreMissingAndLegacyExists_SavesReloadsAndDeletesLegacy()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var legacyPath = directory.PathOf("local-device.json");
        await WriteLegacyAsync(legacyPath, "legacy-password", "192.0.2.20");
        var store = new JsonDeviceCatalogStore(catalogPath);
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            legacyPath,
            MockDeviceData.Create,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();
        var device = catalog.GetDevice(DeviceId)!;
        var channel = Assert.Single(device.Channels);

        Assert.Equal("192.0.2.20", device.IpAddress);
        Assert.Equal("legacy-password", device.Password);
        Assert.Equal(8554, device.RtspPort);
        Assert.Equal(2, channel.ChannelNo);
        Assert.Equal(StreamType.Sub, channel.StreamType);
        Assert.False(File.Exists(legacyPath));

        var reloaded = await store.LoadAsync();
        Assert.NotNull(reloaded);
        Assert.Equal("legacy-password", reloaded.Devices.Single(item => item.Id == DeviceId).Password);
    }

    [Fact]
    public async Task InitializeAsync_WhenProgramDataCatalogExists_DoesNotReadOldCatalogOrLegacy()
    {
        using var directory = new TemporaryDirectory();
        var newPath = directory.PathOf("program-data", "device-catalog.json");
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        var legacyPath = directory.PathOf("local-device.json");
        await new JsonDeviceCatalogStore(
            newPath,
            DataProtectionScope.LocalMachine)
            .SaveAsync(CreateSnapshot("ProgramData设备", "198.51.100.20", "new-password"));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(oldPath)!);
        await File.WriteAllTextAsync(oldPath, "not valid json");
        await File.WriteAllTextAsync(legacyPath, "not valid json");

        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine),
            legacyPath,
            () => throw new InvalidOperationException("不应执行 Mock seed。"),
            oldCatalogPath: oldPath);

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("ProgramData设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.True(File.Exists(oldPath));
        Assert.True(File.Exists(legacyPath));
        Assert.False(File.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenProgramDataMissingAndOldCatalogExists_MigratesToLocalMachine()
    {
        using var directory = new TemporaryDirectory();
        var newPath = directory.PathOf("program-data", "device-catalog.json");
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        var oldStore = new JsonDeviceCatalogStore(oldPath, DataProtectionScope.CurrentUser);
        await oldStore.SaveAsync(CreateSnapshot("旧用户设备", "198.51.100.21", "old-password"));
        var newStore = new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine);
        var bootstrapper = new DeviceCatalogBootstrapper(
            newStore,
            directory.PathOf("local-device.json"),
            () => throw new InvalidOperationException("不应执行 Mock seed。"),
            oldCatalogPath: oldPath);

        var catalog = await bootstrapper.InitializeAsync();
        var loaded = await newStore.LoadAsync();
        var json = await File.ReadAllTextAsync(newPath);

        Assert.Equal("旧用户设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.Equal("198.51.100.21", catalog.GetDevice(DeviceId)!.IpAddress);
        Assert.Equal("old-password", catalog.GetDevice(DeviceId)!.Password);
        Assert.NotNull(loaded);
        Assert.Equal("old-password", loaded.Devices.Single().Password);
        Assert.Contains("dpapi:v1:", json, StringComparison.Ordinal);
        Assert.DoesNotContain("old-password", json, StringComparison.Ordinal);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenOldCatalogAndLegacyExist_UsesOldCatalogWithoutLegacyOverride()
    {
        using var directory = new TemporaryDirectory();
        var newPath = directory.PathOf("program-data", "device-catalog.json");
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        var legacyPath = directory.PathOf("local-device.json");
        await new JsonDeviceCatalogStore(oldPath, DataProtectionScope.CurrentUser)
            .SaveAsync(CreateSnapshot("旧用户设备", "198.51.100.24", "old-password"));
        await WriteLegacyAsync(legacyPath, "legacy-password", "192.0.2.99");
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine),
            legacyPath,
            () => throw new InvalidOperationException("不应执行 Mock seed。"),
            oldCatalogPath: oldPath);

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("旧用户设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.Equal("198.51.100.24", catalog.GetDevice(DeviceId)!.IpAddress);
        Assert.Equal("old-password", catalog.GetDevice(DeviceId)!.Password);
        Assert.True(File.Exists(legacyPath));
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenOldCatalogJsonIsCorrupt_FailsWithoutMockOrDeletingOldFile()
    {
        using var directory = new TemporaryDirectory();
        var newPath = directory.PathOf("program-data", "device-catalog.json");
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(oldPath)!);
        await File.WriteAllTextAsync(oldPath, "not valid json");
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine),
            directory.PathOf("local-device.json"),
            () => throw new InvalidOperationException("不应退回 Mock。"),
            oldCatalogPath: oldPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => bootstrapper.InitializeAsync());

        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(newPath));
        Assert.False(File.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenOldCatalogDpapiCannotBeDecrypted_FailsWithoutDeletingOldFile()
    {
        using var directory = new TemporaryDirectory();
        var newPath = directory.PathOf("program-data", "device-catalog.json");
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        await WritePasswordJsonAsync(oldPath, "dpapi:v1:not-valid-base64");
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine),
            directory.PathOf("local-device.json"),
            () => throw new InvalidOperationException("不应退回 Mock。"),
            oldCatalogPath: oldPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => bootstrapper.InitializeAsync());

        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(newPath));
        Assert.False(File.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenNewCatalogReloadFails_DoesNotRenameOldCatalog()
    {
        using var directory = new TemporaryDirectory();
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        await new JsonDeviceCatalogStore(oldPath, DataProtectionScope.CurrentUser)
            .SaveAsync(CreateSnapshot("旧用户设备", "198.51.100.22", "old-password"));
        var store = new ScriptedStore(null, null);
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            directory.PathOf("local-device.json"),
            MockDeviceData.Create,
            oldCatalogPath: oldPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => bootstrapper.InitializeAsync());

        Assert.Equal(1, store.SaveCount);
        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenOldCatalogRenameFails_ReturnsNewCatalogAndKeepsBothFiles()
    {
        using var directory = new TemporaryDirectory();
        var newPath = directory.PathOf("program-data", "device-catalog.json");
        var oldPath = directory.PathOf("local-data", "device-catalog.json");
        await new JsonDeviceCatalogStore(oldPath, DataProtectionScope.CurrentUser)
            .SaveAsync(CreateSnapshot("旧用户设备", "198.51.100.23", "old-password"));
        Directory.CreateDirectory(GetMigratedCatalogPath(oldPath));
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine),
            directory.PathOf("local-device.json"),
            () => throw new InvalidOperationException("不应执行 Mock seed。"),
            oldCatalogPath: oldPath);

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("旧用户设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.Contains("迁移", bootstrapper.LastMigrationWarning, StringComparison.Ordinal);
        Assert.DoesNotContain("old-password", bootstrapper.LastMigrationWarning, StringComparison.Ordinal);
        Assert.True(File.Exists(newPath));
        Assert.True(File.Exists(oldPath));
        Assert.True(Directory.Exists(GetMigratedCatalogPath(oldPath)));
    }

    [Fact]
    public async Task InitializeAsync_WhenProgramDataDirectoryCannotBeCreated_FailsWithoutFallback()
    {
        using var directory = new TemporaryDirectory();
        var blockedDirectory = directory.PathOf("blocked");
        await File.WriteAllTextAsync(blockedDirectory, "not a directory");
        var newPath = System.IO.Path.Combine(blockedDirectory, "device-catalog.json");
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(newPath, DataProtectionScope.LocalMachine),
            directory.PathOf("local-device.json"),
            MockDeviceData.Create,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        await Assert.ThrowsAnyAsync<Exception>(() => bootstrapper.InitializeAsync());

        Assert.False(File.Exists(newPath));
        Assert.False(File.Exists(directory.PathOf("old-device-catalog.json")));
    }

    [Fact]
    public async Task InitializeAsync_WhenLegacyJsonIsCorrupt_FailsWithoutCreatingCatalogOrDeletingLegacy()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var legacyPath = directory.PathOf("local-device.json");
        await File.WriteAllTextAsync(legacyPath, "not valid json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            legacyPath,
            MockDeviceData.Create,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bootstrapper.InitializeAsync());

        Assert.Contains("local-device.json", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(catalogPath));
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public async Task InitializeAsync_WhenStoreJsonIsCorrupt_FailsWithoutFallingBackOrModifyingFiles()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var legacyPath = directory.PathOf("local-device.json");
        await File.WriteAllTextAsync(catalogPath, "not valid json");
        await WriteLegacyAsync(legacyPath, "legacy-password", "192.0.2.20");
        var original = await File.ReadAllTextAsync(catalogPath);
        var seedInvoked = false;
        var store = new JsonDeviceCatalogStore(catalogPath);
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            legacyPath,
            () =>
            {
                seedInvoked = true;
                throw new InvalidOperationException("不应退回 Mock。");
            });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.False(seedInvoked);
        Assert.Contains("没有可用备份", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllTextAsync(catalogPath));
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalCatalogIsCorruptAndBackupIsValid_RecoversFromBackup()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var backupPath = catalogPath + ".bak";
        var corruptContent = "not valid formal json";
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.40", "backup-password"));
        await store.SaveAsync(CreateSnapshot("正式设备", "198.51.100.41", "formal-password"));
        await File.WriteAllTextAsync(catalogPath, corruptContent);

        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            directory.PathOf("local-device.json"),
            () => throw new InvalidOperationException("不应退回 Mock。"),
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("备份设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.Equal("198.51.100.40", catalog.GetDevice(DeviceId)!.IpAddress);
        Assert.Equal(corruptContent, await File.ReadAllTextAsync(
            Directory.GetFiles(
                directory.Path,
                "device-catalog.corrupt-*.json").Single()));
        Assert.Equal("备份设备", (await new JsonDeviceCatalogStore(backupPath).LoadAsync())!
            .Devices.Single().Name);
        Assert.Equal("备份设备", (await store.LoadAsync())!.Devices.Single().Name);
        Assert.False(File.Exists(catalogPath + ".recovery.tmp"));
        Assert.True(bootstrapper.RecoveryOccurred);

        var nextBootstrapper = new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));
        _ = await nextBootstrapper.InitializeAsync();

        Assert.False(nextBootstrapper.RecoveryOccurred);
        Assert.Single(Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalAndBackupAreValid_UsesFormalWithoutRecovery()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.52", "backup-password"));
        await store.SaveAsync(CreateSnapshot("正式设备", "198.51.100.53", "formal-password"));

        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("正式设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.False(bootstrapper.RecoveryOccurred);
        Assert.Empty(Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalSchemaIsUnsupportedAndBackupIsValid_RecoversFromBackup()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.42", "backup-password"));
        await store.SaveAsync(CreateSnapshot("当前设备", "198.51.100.43", "current-password"));
        await File.WriteAllTextAsync(
            catalogPath,
            "{\"schemaVersion\":999,\"groups\":[],\"devices\":[]}");

        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("备份设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.True(bootstrapper.RecoveryOccurred);
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalDpapiIsInvalidAndBackupIsValid_RecoversFromBackup()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.44", "backup-password"));
        await store.SaveAsync(CreateSnapshot("当前设备", "198.51.100.45", "current-password"));
        await File.WriteAllTextAsync(
            catalogPath,
            """
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [{
                "id": "50000000-0000-0000-0000-000000000001",
                "password": "dpapi:v1:not-valid-base64",
                "channels": []
              }]
            }
            """);

        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("备份设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.True(bootstrapper.RecoveryOccurred);
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalCatalogFailsBusinessValidationAndBackupIsValid_RecoversFromBackup()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.46", "backup-password"));
        await store.SaveAsync(CreateSnapshot("当前设备", "198.51.100.47", "current-password"));
        await File.WriteAllTextAsync(
            catalogPath,
            $$"""
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [{
                "id": "{{DeviceId}}",
                "groupId": "72000000-0000-0000-0000-000000000001",
                "password": "",
                "channels": []
              }]
            }
            """);

        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();

        Assert.Equal("备份设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.True(bootstrapper.RecoveryOccurred);
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalIsCorruptAndBackupSchemaIsUnsupported_FailsWithoutChangingFiles()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var backupPath = catalogPath + ".bak";
        var formalContent = "corrupt formal content";
        var backupContent = "{\"schemaVersion\":999,\"groups\":[],\"devices\":[]}";
        await File.WriteAllTextAsync(catalogPath, formalContent);
        await File.WriteAllTextAsync(backupPath, backupContent);
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(catalogPath),
            mockDataFactory: () => throw new InvalidOperationException("不应退回 Mock。"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.Equal(formalContent, await File.ReadAllTextAsync(catalogPath));
        Assert.Equal(backupContent, await File.ReadAllTextAsync(backupPath));
        Assert.Empty(Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalIsCorruptAndBackupDpapiIsInvalid_FailsWithoutChangingFiles()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var backupPath = catalogPath + ".bak";
        var formalContent = "corrupt formal content";
        var backupContent = $$"""
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [{
                "id": "{{DeviceId}}",
                "password": "dpapi:v1:not-valid-base64",
                "channels": []
              }]
            }
            """;
        await File.WriteAllTextAsync(catalogPath, formalContent);
        await File.WriteAllTextAsync(backupPath, backupContent);
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(catalogPath),
            mockDataFactory: () => throw new InvalidOperationException("不应退回 Mock。"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.Equal(formalContent, await File.ReadAllTextAsync(catalogPath));
        Assert.Equal(backupContent, await File.ReadAllTextAsync(backupPath));
        Assert.Empty(Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalIsCorruptAndBackupBusinessDataIsInvalid_FailsWithoutChangingFiles()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var backupPath = catalogPath + ".bak";
        var formalContent = "corrupt formal content";
        var backupContent = $$"""
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [{
                "id": "{{DeviceId}}",
                "groupId": "72000000-0000-0000-0000-000000000001",
                "password": "",
                "channels": []
              }]
            }
            """;
        await File.WriteAllTextAsync(catalogPath, formalContent);
        await File.WriteAllTextAsync(backupPath, backupContent);
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(catalogPath),
            mockDataFactory: () => throw new InvalidOperationException("不应退回 Mock。"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.Equal(formalContent, await File.ReadAllTextAsync(catalogPath));
        Assert.Equal(backupContent, await File.ReadAllTextAsync(backupPath));
        Assert.Empty(Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalIsCorruptAndBackupIsInvalid_KeepsBothFilesAndDoesNotSeedMock()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var backupPath = catalogPath + ".bak";
        var formalContent = "corrupt formal content";
        var backupContent = "corrupt backup content";
        await File.WriteAllTextAsync(catalogPath, formalContent);
        await File.WriteAllTextAsync(backupPath, backupContent);
        var seedInvoked = false;
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(catalogPath),
            mockDataFactory: () =>
            {
                seedInvoked = true;
                throw new InvalidOperationException("不应退回 Mock。");
            });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.False(seedInvoked);
        Assert.Equal(formalContent, await File.ReadAllTextAsync(catalogPath));
        Assert.Equal(backupContent, await File.ReadAllTextAsync(backupPath));
        Assert.Empty(Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json"));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalAndBackupAreRecoveredTwice_DoesNotOverwriteCorruptEvidenceOrBackup()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var backupPath = catalogPath + ".bak";
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.48", "backup-password"));
        await store.SaveAsync(CreateSnapshot("当前设备", "198.51.100.49", "current-password"));
        var healthyBackup = await File.ReadAllTextAsync(backupPath);

        await File.WriteAllTextAsync(catalogPath, "first corrupt content");
        await new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"))
            .InitializeAsync();

        await File.WriteAllTextAsync(catalogPath, "second corrupt content");
        await new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"))
            .InitializeAsync();

        var corruptFiles = Directory.GetFiles(
            directory.Path,
            "device-catalog.corrupt-*.json");
        Assert.Equal(2, corruptFiles.Length);
        Assert.Contains(
            corruptFiles,
            path => File.ReadAllText(path) == "first corrupt content");
        Assert.Contains(
            corruptFiles,
            path => File.ReadAllText(path) == "second corrupt content");
        Assert.Equal(healthyBackup, await File.ReadAllTextAsync(backupPath));
    }

    [Fact]
    public async Task InitializeAsync_WhenFormalIsMissing_DoesNotUseBackupAsInitialData()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var store = new JsonDeviceCatalogStore(catalogPath);
        await store.SaveAsync(CreateSnapshot("备份设备", "198.51.100.50", "backup-password"));
        await store.SaveAsync(CreateSnapshot("当前设备", "198.51.100.51", "current-password"));
        File.Delete(catalogPath);

        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        var catalog = await bootstrapper.InitializeAsync();

        Assert.NotEqual("备份设备", catalog.GetDevice(DeviceId)!.Name);
        Assert.False(bootstrapper.RecoveryOccurred);
        Assert.True(File.Exists(catalogPath));
    }

    [Fact]
    public async Task InitializeAsync_WhenDpapiCannotBeDecrypted_FailsWithoutFallingBackToMock()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        var payload = Convert.ToBase64String([1, 2, 3, 4]);
        await File.WriteAllTextAsync(
            catalogPath,
            $$"""
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [{
                "id": "73000000-0000-0000-0000-000000000001",
                "password": "dpapi:v1:{{payload}}",
                "channels": []
              }]
            }
            """);
        var seedInvoked = false;
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(catalogPath),
            directory.PathOf("local-device.json"),
            () =>
            {
                seedInvoked = true;
                throw new InvalidOperationException("不应退回 Mock。");
            });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.False(seedInvoked);
        Assert.Contains("没有可用备份", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenSchemaVersionIsUnsupported_FailsWithoutFallingBackToMock()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = directory.PathOf("device-catalog.json");
        await File.WriteAllTextAsync(
            catalogPath,
            "{\"schemaVersion\":999,\"groups\":[],\"devices\":[]}");
        var seedInvoked = false;
        var bootstrapper = new DeviceCatalogBootstrapper(
            new JsonDeviceCatalogStore(catalogPath),
            directory.PathOf("local-device.json"),
            () =>
            {
                seedInvoked = true;
                throw new InvalidOperationException("不应退回 Mock。");
            });

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => bootstrapper.InitializeAsync());

        Assert.False(seedInvoked);
        Assert.Contains("没有可用备份", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenLoadedCatalogFailsValidation_ThrowsInvalidData()
    {
        var invalidSnapshot = new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups = [],
            Devices =
            [
                new CameraDevice
                {
                    Id = DeviceId,
                    GroupId = Guid.Parse("72000000-0000-0000-0000-000000000001")
                }
            ]
        };
        var bootstrapper = new DeviceCatalogBootstrapper(
            new ScriptedStore(invalidSnapshot),
            mockDataFactory: () => throw new InvalidOperationException("不应执行 Mock seed。"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => bootstrapper.InitializeAsync());

        Assert.Contains("数据校验", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenReloadAfterSaveFails_DoesNotDeleteLegacy()
    {
        using var directory = new TemporaryDirectory();
        var legacyPath = directory.PathOf("local-device.json");
        await WriteLegacyAsync(legacyPath, "legacy-password", "192.0.2.20");
        var store = new ScriptedStore(null, null);
        var bootstrapper = new DeviceCatalogBootstrapper(
            store,
            legacyPath,
            MockDeviceData.Create,
            oldCatalogPath: directory.PathOf("old-device-catalog.json"));

        await Assert.ThrowsAsync<InvalidDataException>(() => bootstrapper.InitializeAsync());

        Assert.Equal(1, store.SaveCount);
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public void App_UsesBootstrapperCatalogForAllConsumers()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var appPath = Path.Combine(repositoryRoot, "src", "VideoMonitor.Wpf", "App.xaml.cs");
        var source = File.ReadAllText(appPath);

        Assert.Contains("new DeviceCatalogBootstrapper", source, StringComparison.Ordinal);
        Assert.Contains("LastMigrationWarning", source, StringComparison.Ordinal);
        Assert.Contains("迁移提示", source, StringComparison.Ordinal);
        Assert.Contains("RecoveryOccurred", source, StringComparison.Ordinal);
        Assert.Contains("设备配置文件损坏，系统已从最近的有效备份恢复", source, StringComparison.Ordinal);
        Assert.Contains("InvalidDataException => exception.Message", source, StringComparison.Ordinal);
        Assert.Contains("var deviceCatalogStore = new JsonDeviceCatalogStore();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryDeviceCatalog", source, StringComparison.Ordinal);
        Assert.Contains("new DeviceCatalogPersistenceCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("persistenceCoordinator.PersistenceFailed +=", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.Contains("ShutdownCleanupCoordinator.ExecuteAsync", source, StringComparison.Ordinal);
        Assert.Contains("persistenceCoordinator.DisposeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("new MonitorViewModel(switchService, groups, deviceCatalog)", source, StringComparison.Ordinal);
        Assert.Contains("new DeviceManagementViewModel(deviceCatalog)", source, StringComparison.Ordinal);
        Assert.Contains("new SecondaryMonitorViewModel(switchService, groups, deviceCatalog)", source, StringComparison.Ordinal);
        Assert.Contains("deviceCatalog,\r\n                    zlmClient", source, StringComparison.Ordinal);
    }

    private static DeviceCatalogSnapshot CreateSnapshot(
        string deviceName,
        string ipAddress,
        string password)
    {
        var groupId = Guid.Parse("72000000-0000-0000-0000-000000000001");
        var group = new DeviceGroup
        {
            Id = groupId,
            Name = "Store分组",
            Enabled = true
        };
        var device = new CameraDevice
        {
            Id = DeviceId,
            Name = deviceName,
            GroupId = groupId,
            IpAddress = ipAddress,
            Password = password
        };
        device.Channels.Add(new CameraChannel
        {
            Id = ChannelId,
            DeviceId = DeviceId,
            ChannelNo = 1,
            StreamType = StreamType.Main
        });
        return new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups = [group],
            Devices = [device]
        };
    }

    private static async Task WriteLegacyAsync(
        string path,
        string password,
        string ipAddress)
    {
        var options = new LocalDeviceOptions
        {
            DeviceId = DeviceId,
            ChannelId = ChannelId,
            LocalIdentifier = "camera001",
            IpAddress = ipAddress,
            RtspPort = 8554,
            Username = "legacy-user",
            Password = password,
            ChannelNo = 2,
            StreamType = StreamType.Sub
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(options));
    }

    private static async Task WritePasswordJsonAsync(string path, string password)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var json = $$"""
            {
              "schemaVersion": 1,
              "groups": [],
              "devices": [{
                "id": "73000000-0000-0000-0000-000000000001",
                "password": "{{password}}",
                "channels": []
              }]
            }
            """;
        await File.WriteAllTextAsync(path, json);
    }

    private static string GetMigratedCatalogPath(string catalogPath)
    {
        var directory = System.IO.Path.GetDirectoryName(catalogPath)!;
        var fileName = System.IO.Path.GetFileNameWithoutExtension(catalogPath);
        return System.IO.Path.Combine(
            directory,
            $"{fileName}.currentuser.migrated.json");
    }

    private sealed class ScriptedStore : IDeviceCatalogStore
    {
        private readonly Queue<DeviceCatalogSnapshot?> loads;

        public ScriptedStore(params DeviceCatalogSnapshot?[] loads)
        {
            this.loads = new Queue<DeviceCatalogSnapshot?>(loads);
        }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task<DeviceCatalogSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(loads.Count == 0 ? null : loads.Dequeue());
        }

        public Task SaveAsync(
            DeviceCatalogSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"VideoMonitor.Bootstrapper.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathOf(params string[] segments)
        {
            var paths = new string[segments.Length + 1];
            paths[0] = Path;
            Array.Copy(segments, 0, paths, 1, segments.Length);
            return System.IO.Path.Combine(paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
