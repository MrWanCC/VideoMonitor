using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Wpf.Configuration;

public sealed class DeviceCatalogBootstrapper
{
    private const string LegacyDeviceFileName = "local-device.json";
    private const string MigratedCatalogSuffix = ".currentuser.migrated.json";
    private static readonly JsonSerializerOptions LegacySerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDeviceCatalogStore store;
    private readonly string legacyDevicePath;
    private readonly string oldCatalogPath;
    private readonly Func<MockDeviceDataSet> mockDataFactory;

    public DeviceCatalogBootstrapper(
        IDeviceCatalogStore store,
        string? legacyDevicePath = null,
        Func<MockDeviceDataSet>? mockDataFactory = null,
        string? oldCatalogPath = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.legacyDevicePath = Path.GetFullPath(
            legacyDevicePath
                ?? Path.Combine(AppContext.BaseDirectory, LegacyDeviceFileName));
        this.oldCatalogPath = Path.GetFullPath(
            oldCatalogPath ?? DefaultOldCatalogPath);
        this.mockDataFactory = mockDataFactory ?? MockDeviceData.Create;
    }

    public string? LastMigrationWarning { get; private set; }

    private static string DefaultOldCatalogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoMonitor",
        "data",
        "device-catalog.json");

    public async Task<InMemoryDeviceCatalog> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        LastMigrationWarning = null;
        var existingSnapshot = await store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existingSnapshot is not null)
        {
            return CreateCatalog(existingSnapshot);
        }

        if (File.Exists(oldCatalogPath))
        {
            return await MigrateOldCatalogAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var mockData = mockDataFactory()
            ?? throw new InvalidDataException("Mock设备初始数据为空。");
        var initialCatalog = new InMemoryDeviceCatalog(
            mockData.Groups,
            mockData.Devices);
        var initialStatuses = CaptureRuntimeStatuses(initialCatalog);
        var hasLegacyDeviceFile = File.Exists(legacyDevicePath);
        if (hasLegacyDeviceFile)
        {
            var legacyOptions = LoadLegacyDeviceOptions();
            LocalDeviceCatalogOverride.Apply(initialCatalog, legacyOptions);
        }

        var snapshot = DeviceCatalogSnapshotFactory.Create(initialCatalog);
        var reloadedCatalog = await SaveAndReloadAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);
        RestoreRuntimeStatuses(initialStatuses, reloadedCatalog);

        if (hasLegacyDeviceFile)
        {
            DeleteLegacyDeviceFile();
        }

        return reloadedCatalog;
    }

    private async Task<InMemoryDeviceCatalog> MigrateOldCatalogAsync(
        CancellationToken cancellationToken)
    {
        var oldStore = new JsonDeviceCatalogStore(
            oldCatalogPath,
            DataProtectionScope.CurrentUser);
        var oldSnapshot = await oldStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("旧用户设备目录为空。");

        // Validate the old catalog before writing anything to ProgramData.
        _ = CreateCatalog(oldSnapshot);
        var reloadedCatalog = await SaveAndReloadAsync(
                oldSnapshot,
                cancellationToken)
            .ConfigureAwait(false);

        RenameOldCatalog();
        return reloadedCatalog;
    }

    private async Task<InMemoryDeviceCatalog> SaveAndReloadAsync(
        DeviceCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

        var reloadedSnapshot = await store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("设备目录保存后无法重新加载。");
        return CreateCatalog(reloadedSnapshot);
    }

    private static IReadOnlyDictionary<Guid, CameraStatus> CaptureRuntimeStatuses(
        InMemoryDeviceCatalog catalog) => catalog
        .GetGroups()
        .SelectMany(group => catalog.GetDevices(group.Id))
        .ToDictionary(device => device.Id, device => device.Status);

    private static void RestoreRuntimeStatuses(
        IReadOnlyDictionary<Guid, CameraStatus> statuses,
        InMemoryDeviceCatalog catalog)
    {
        foreach (var (deviceId, status) in statuses)
        {
            var device = catalog.GetDevice(deviceId);
            if (device is not null)
            {
                device.Status = status;
            }
        }
    }

    private InMemoryDeviceCatalog CreateCatalog(DeviceCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            return new InMemoryDeviceCatalog(snapshot.Groups, snapshot.Devices);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("设备目录数据校验失败。", exception);
        }
    }

    private LocalDeviceOptions LoadLegacyDeviceOptions()
    {
        try
        {
            var json = File.ReadAllText(legacyDevicePath);
            return JsonSerializer.Deserialize<LocalDeviceOptions>(
                    json,
                    LegacySerializerOptions)
                ?? throw new InvalidOperationException("旧 local-device.json 为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("旧 local-device.json 格式无效。", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("旧 local-device.json 读取失败。", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException("旧 local-device.json 读取失败。", exception);
        }
    }

    private void DeleteLegacyDeviceFile()
    {
        try
        {
            File.Delete(legacyDevicePath);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "旧 local-device.json 删除失败，安全设备目录已保留。",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                "旧 local-device.json 删除失败，安全设备目录已保留。",
                exception);
        }
    }

    private void RenameOldCatalog()
    {
        var migratedPath = GetMigratedCatalogPath(oldCatalogPath);
        try
        {
            File.Move(oldCatalogPath, migratedPath);
        }
        catch (IOException exception)
        {
            LastMigrationWarning = "旧用户设备目录迁移标记失败，新的机器级设备目录已保留。";
            System.Diagnostics.Debug.WriteLine(exception.GetType().Name);
        }
        catch (UnauthorizedAccessException exception)
        {
            LastMigrationWarning = "旧用户设备目录迁移标记失败，新的机器级设备目录已保留。";
            System.Diagnostics.Debug.WriteLine(exception.GetType().Name);
        }
    }

    private static string GetMigratedCatalogPath(string catalogPath)
    {
        var directory = Path.GetDirectoryName(catalogPath)!;
        var fileName = Path.GetFileNameWithoutExtension(catalogPath);
        return Path.Combine(directory, fileName + MigratedCatalogSuffix);
    }

}
