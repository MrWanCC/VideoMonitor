using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteBackupServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_CreatesConsistentSnapshotAndManifest()
    {
        using var context = TestContext.Create();
        await context.CreateStore().SaveAsync(CreateSnapshot());

        var result = await context.CreateBackupService().CreateBackupAsync();

        Assert.True(File.Exists(result.DatabasePath));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.Equal("videomonitor.db", Path.GetFileName(result.DatabasePath));
        Assert.Equal("manifest.json", Path.GetFileName(result.ManifestPath));
        Assert.StartsWith(
            Path.GetFullPath(context.Provider.BackupsDirectory),
            Path.GetFullPath(result.DirectoryPath),
            StringComparison.OrdinalIgnoreCase);

        var manifestText = await File.ReadAllTextAsync(result.ManifestPath);
        using var manifestDocument = JsonDocument.Parse(manifestText);
        var manifestRoot = manifestDocument.RootElement;
        Assert.True(manifestRoot.TryGetProperty("schemaVersion", out _));
        Assert.True(manifestRoot.TryGetProperty("createdAtUtc", out _));
        Assert.True(manifestRoot.TryGetProperty("applicationVersion", out _));
        Assert.True(manifestRoot.TryGetProperty("databaseSha256", out _));
        Assert.False(manifestRoot.TryGetProperty("SchemaVersion", out _));
        Assert.False(manifestRoot.TryGetProperty("CreatedAtUtc", out _));

        var manifest = JsonSerializer.Deserialize<SqliteBackupManifest>(
            manifestText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(manifest);
        Assert.Equal(SqliteDatabaseInitializer.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(manifest.ApplicationVersion));

        Assert.Equal(2, await ReadIntAsync(
            result.DatabasePath,
            "SELECT COUNT(*) FROM device_groups;"));
        Assert.Equal(1, await ReadIntAsync(
            result.DatabasePath,
            "SELECT COUNT(*) FROM camera_devices WHERE name = 'Backup Camera';"));
        Assert.Equal(2, await ReadIntAsync(
            result.DatabasePath,
            "SELECT COUNT(*) FROM camera_channels;"));

        Assert.Empty(Directory.EnumerateFiles(result.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ManifestSha256_MatchesBackupDatabase()
    {
        using var context = TestContext.Create();
        await context.CreateStore().SaveAsync(CreateSnapshot());

        var result = await context.CreateBackupService().CreateBackupAsync();
        var manifestText = await File.ReadAllTextAsync(result.ManifestPath);
        var manifest = JsonSerializer.Deserialize<SqliteBackupManifest>(
            manifestText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var actualHash = await ComputeSha256Async(result.DatabasePath);

        Assert.NotNull(manifest);
        Assert.Equal(actualHash, result.DatabaseSha256);
        Assert.Equal(actualHash, manifest.DatabaseSha256);
        Assert.Equal(64, actualHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", actualHash);
    }

    [Fact]
    public async Task CreateBackupAsync_DoesNotCopySecretsOrMasterKey()
    {
        using var context = TestContext.Create();
        await context.CreateStore().SaveAsync(CreateSnapshot());
        await File.WriteAllTextAsync(context.Provider.MasterKeyPath, "TEST-MASTER-KEY");

        var result = await context.CreateBackupService().CreateBackupAsync();

        var databaseText = Encoding.UTF8.GetString(
            await File.ReadAllBytesAsync(result.DatabasePath));
        var manifestText = await File.ReadAllTextAsync(result.ManifestPath);
        Assert.DoesNotContain(
            "Password-Should-Never-Appear-In-Backup",
            databaseText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Password-Should-Never-Appear-In-Backup",
            manifestText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("password_ciphertext", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("aesgcm:v1:", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-MASTER-KEY", manifestText, StringComparison.Ordinal);
        Assert.Empty(
            Directory.EnumerateFiles(result.DirectoryPath, "master-key.protected", SearchOption.AllDirectories));

        var ciphertext = await ReadStringAsync(
            result.DatabasePath,
            "SELECT password_ciphertext FROM camera_devices LIMIT 1;");
        Assert.StartsWith("aesgcm:v1:", ciphertext, StringComparison.Ordinal);

        var files = Directory.EnumerateFiles(
                result.DirectoryPath,
                "*",
                SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["manifest.json", "videomonitor.db"], files);
    }

    [Fact]
    public async Task CreateBackupAsync_ConcurrentCallsCreateDistinctCompleteSnapshots()
    {
        using var context = TestContext.Create();
        await context.CreateStore().SaveAsync(CreateSnapshot());
        var service = context.CreateBackupService();

        var results = await Task.WhenAll(
            service.CreateBackupAsync(),
            service.CreateBackupAsync());

        Assert.Equal(2, results.Select(result => result.DirectoryPath).Distinct().Count());
        foreach (var result in results)
        {
            Assert.True(File.Exists(result.DatabasePath));
            Assert.True(File.Exists(result.ManifestPath));
            Assert.Equal(result.DatabaseSha256, await ComputeSha256Async(result.DatabasePath));
            Assert.Empty(Directory.EnumerateFiles(result.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
            Assert.Equal(1, await ReadIntAsync(
                result.DatabasePath,
                "SELECT COUNT(*) FROM camera_devices WHERE name = 'Backup Camera';"));
        }
    }

    [Fact]
    public async Task CreateBackupAsync_FailureDoesNotDeleteExistingBackupsOrLiveDatabase()
    {
        using var context = TestContext.Create();
        await context.CreateStore().SaveAsync(CreateSnapshot());
        ClearSqliteConnectionPools();

        var existingBackupDirectory = Path.Combine(
            context.Provider.BackupsDirectory,
            "existing-backup");
        Directory.CreateDirectory(existingBackupDirectory);
        var keepFile = Path.Combine(existingBackupDirectory, "keep.txt");
        await File.WriteAllTextAsync(keepFile, "keep");
        var liveDatabaseBefore = await File.ReadAllBytesAsync(context.Provider.DatabasePath);

        var blockingBackupsPath = Path.Combine(
            context.Provider.RootDirectory,
            "backups-blocker");
        await File.WriteAllTextAsync(blockingBackupsPath, "not-a-directory");
        var failingPaths = new BackupFailurePathProvider(
            context.Provider,
            blockingBackupsPath);

        await Assert.ThrowsAsync<IOException>(
            () => context.CreateBackupService(failingPaths).CreateBackupAsync());
        ClearSqliteConnectionPools();

        Assert.Equal("keep", await File.ReadAllTextAsync(keepFile));
        Assert.Equal(liveDatabaseBefore, await File.ReadAllBytesAsync(context.Provider.DatabasePath));
        Assert.True(File.Exists(blockingBackupsPath));
    }

    private static DeviceCatalogSnapshot CreateSnapshot()
    {
        var rootId = Guid.Parse("91000000-0000-0000-0000-000000000001");
        var childId = Guid.Parse("92000000-0000-0000-0000-000000000001");
        var deviceId = Guid.Parse("93000000-0000-0000-0000-000000000001");

        var device = new CameraDevice
        {
            Id = deviceId,
            Name = "Backup Camera",
            GroupId = childId,
            IpAddress = "192.0.2.20",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "backup-user",
            Password = "Password-Should-Never-Appear-In-Backup",
            Manufacturer = "Hikvision",
            Model = "DS-2CD",
            TransportMode = TransportMode.Tcp,
            Status = CameraStatus.Online,
            Enabled = true,
            Remark = "Backup test camera"
        };
        device.Channels.Add(new CameraChannel
        {
            Id = Guid.Parse("94000000-0000-0000-0000-000000000001"),
            DeviceId = deviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-main",
            Enabled = true
        });
        device.Channels.Add(new CameraChannel
        {
            Id = Guid.Parse("95000000-0000-0000-0000-000000000001"),
            DeviceId = deviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Sub",
            StreamType = StreamType.Sub,
            StreamId = "runtime-sub",
            Enabled = false
        });

        return new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups =
            [
                new DeviceGroup
                {
                    Id = rootId,
                    Name = "Backup Root",
                    Sort = 1,
                    Enabled = true
                },
                new DeviceGroup
                {
                    Id = childId,
                    Name = "Backup Child",
                    ParentId = rootId,
                    Sort = 2,
                    Enabled = true
                }
            ],
            Devices = [device]
        };
    }

    private static async Task<int> ReadIntAsync(string databasePath, string sql)
    {
        var value = await ReadScalarAsync(databasePath, sql);
        return Convert.ToInt32(value);
    }

    private static async Task<string> ReadStringAsync(string databasePath, string sql) =>
        Convert.ToString(await ReadScalarAsync(databasePath, sql)) ?? string.Empty;

    private static async Task<object?> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class TestContext : IDisposable
    {
        private TestContext(string root)
        {
            Provider = new DefaultAppPathProvider(new ServerStorageOptions { RootPath = root });
            new ServerStorageLayout(Provider).EnsureCreated();
            Factory = new SqliteConnectionFactory(Provider);
            Initializer = new SqliteDatabaseInitializer(Factory);
        }

        public DefaultAppPathProvider Provider { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteDatabaseInitializer Initializer { get; }

        public static TestContext Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VideoMonitorSqliteBackupServiceTests",
                Guid.NewGuid().ToString("N"));
            return new TestContext(root);
        }

        public SqliteDeviceCatalogStore CreateStore() =>
            new(Factory, Initializer, new AesGcmSecretProtector(new FixedMasterKeyProvider()));

        public SqliteBackupService CreateBackupService(
            IAppPathProvider? servicePaths = null) =>
            new(servicePaths ?? Provider, Factory, Initializer);

        public void Dispose()
        {
            ClearSqliteConnectionPools();
            if (!Directory.Exists(Provider.RootDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(Provider.RootDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Microsoft.Data.Sqlite may retain a pooled native handle briefly.
            }
        }
    }

    private sealed class FixedMasterKeyProvider : IMasterKeyProvider
    {
        private static readonly byte[] Key = Enumerable.Range(1, 32)
            .Select(value => (byte)value)
            .ToArray();

        public Task<byte[]> GetOrCreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Key.ToArray());
    }

    private sealed class BackupFailurePathProvider : IAppPathProvider
    {
        private readonly IAppPathProvider inner;
        private readonly string blockedBackupsDirectory;

        public BackupFailurePathProvider(
            IAppPathProvider inner,
            string blockedBackupsDirectory)
        {
            this.inner = inner;
            this.blockedBackupsDirectory = blockedBackupsDirectory;
        }

        public string RootDirectory => inner.RootDirectory;
        public string DataDirectory => inner.DataDirectory;
        public string DatabasePath => inner.DatabasePath;
        public string SecurityDirectory => inner.SecurityDirectory;
        public string MasterKeyPath => inner.MasterKeyPath;
        public string BackupsDirectory => blockedBackupsDirectory;
        public string LogsDirectory => inner.LogsDirectory;
        public string SettingsPath => inner.SettingsPath;
    }

    private static void ClearSqliteConnectionPools()
    {
        var sqliteConnectionType = typeof(SqliteConnection);
        sqliteConnectionType
            .GetMethod(
                "ClearAllPools",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
