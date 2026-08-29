using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteDeviceCatalogStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsConfigurationAndResetsRuntimeFields()
    {
        using var context = TestContext.Create();
        var source = CreateSnapshot();
        var store = context.CreateStore();

        await store.SaveAsync(source);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(DeviceCatalogSnapshot.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(source.Groups.Count, loaded.Groups.Count);
        Assert.Equal(source.Devices.Count, loaded.Devices.Count);

        for (var index = 0; index < source.Groups.Count; index++)
        {
            AssertGroupEqual(source.Groups[index], loaded.Groups[index]);
        }

        var sourceDevice = Assert.Single(source.Devices);
        var loadedDevice = Assert.Single(loaded.Devices);
        AssertDeviceConfigurationEqual(sourceDevice, loadedDevice);
        Assert.Equal(CameraStatus.Unknown, loadedDevice.Status);
        Assert.Equal(sourceDevice.Channels.Count, loadedDevice.Channels.Count);

        for (var index = 0; index < sourceDevice.Channels.Count; index++)
        {
            var sourceChannel = sourceDevice.Channels[index];
            var loadedChannel = loadedDevice.Channels[index];
            AssertChannelConfigurationEqual(sourceChannel, loadedChannel);
            Assert.Equal(string.Empty, loadedChannel.StreamId);
        }
    }

    [Fact]
    public async Task SaveAsync_StoresOnlyEncryptedPasswordCiphertext()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();

        await store.SaveAsync(CreateSnapshot());

        string ciphertext;
        await using (var connection = context.CreateConnection())
        {
            await connection.OpenAsync();
            ciphertext = await ReadScalarAsync(
                connection,
                "SELECT password_ciphertext FROM camera_devices LIMIT 1;");
        }
        ClearSqliteConnectionPools();

        Assert.StartsWith("aesgcm:v1:", ciphertext, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Password-Should-Never-Appear-In-Db",
            ciphertext,
            StringComparison.Ordinal);

        var databaseBytes = await File.ReadAllBytesAsync(context.Provider.DatabasePath);
        var databaseText = Encoding.UTF8.GetString(databaseBytes);
        Assert.DoesNotContain(
            "Password-Should-Never-Appear-In-Db",
            databaseText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_EmptyInitializedDatabaseReturnsEmptySnapshot()
    {
        using var context = TestContext.Create();
        await context.Initializer.InitializeAsync();

        var snapshot = await context.CreateStore().LoadAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(DeviceCatalogSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Empty(snapshot.Groups);
        Assert.Empty(snapshot.Devices);
    }

    [Fact]
    public async Task SaveAsync_DoesNotModifyRuntimeFieldsOnInputSnapshot()
    {
        using var context = TestContext.Create();
        var source = CreateSnapshot();

        await context.CreateStore().SaveAsync(source);

        Assert.Equal(CameraStatus.Online, source.Devices[0].Status);
        Assert.Equal("runtime-main", source.Devices[0].Channels[0].StreamId);
        Assert.Equal("runtime-sub", source.Devices[0].Channels[1].StreamId);
    }

    [Fact]
    public async Task SaveAsync_DuplicateStreamIdentityPreservesExistingSnapshot()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        var source = CreateSnapshot();
        await store.SaveAsync(source);

        var invalid = CreateSnapshot();
        invalid.Devices[0].Name = "Snapshot B";
        invalid.Devices[0].Channels.Add(new CameraChannel
        {
            Id = Guid.Parse("86000000-0000-0000-0000-000000000001"),
            DeviceId = invalid.Devices[0].Id,
            ChannelNo = 1,
            ChannelName = "Duplicate Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-duplicate",
            Enabled = true
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        AssertDeviceConfigurationEqual(source.Devices[0], Assert.Single(loaded.Devices));
    }

    [Fact]
    public async Task SaveAsync_InvalidGroupRelationshipPreservesExistingSnapshot()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        var source = CreateSnapshot();
        await store.SaveAsync(source);

        var invalid = CreateSnapshot();
        invalid.Devices[0].GroupId = Guid.Parse("87000000-0000-0000-0000-000000000001");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        AssertDeviceConfigurationEqual(source.Devices[0], Assert.Single(loaded.Devices));
    }

    [Fact]
    public async Task SaveAsync_EncryptionFailurePreservesExistingSnapshot()
    {
        using var context = TestContext.Create();
        var normalStore = context.CreateStore();
        var source = CreateSnapshot();
        await normalStore.SaveAsync(source);

        var invalidStore = context.CreateStore(new FailingSecretProtector());
        await Assert.ThrowsAsync<CryptographicException>(
            () => invalidStore.SaveAsync(CreateSnapshot("Snapshot B")));

        var loaded = await normalStore.LoadAsync();
        Assert.NotNull(loaded);
        AssertDeviceConfigurationEqual(source.Devices[0], Assert.Single(loaded.Devices));
    }

    [Fact]
    public async Task LoadAsync_InvalidCiphertextFailsSafely()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        await store.SaveAsync(CreateSnapshot());

        await using (var connection = context.CreateConnection())
        {
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                "UPDATE camera_devices SET password_ciphertext = $value;",
                ("$value", "not-valid"));
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync());

        Assert.DoesNotContain("not-valid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password-Should-Never-Appear-In-Db", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("purpose", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsNumericEnumValues()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        await store.SaveAsync(CreateSnapshot());

        await using (var connection = context.CreateConnection())
        {
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                "UPDATE camera_devices SET transport_mode = $value;",
                ("$value", "0"));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_RejectsUnsupportedSnapshotVersionBeforeDatabaseReplacement()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        var source = CreateSnapshot();
        await store.SaveAsync(source);

        var invalid = CreateSnapshot("Snapshot B");
        invalid = new DeviceCatalogSnapshot
        {
            SchemaVersion = 0,
            Groups = invalid.Groups,
            Devices = invalid.Devices
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        AssertDeviceConfigurationEqual(source.Devices[0], Assert.Single(loaded.Devices));
    }

    [Fact]
    public async Task LoadAsync_ConcurrentSaveReturnsSingleConsistentSnapshot()
    {
        using var context = TestContext.Create();
        var snapshotA = CreateSnapshot("Snapshot A");
        snapshotA.Groups[0].Name = "Root A";
        snapshotA.Groups[1].Name = "Child A";
        await context.CreateStore().SaveAsync(snapshotA);

        var snapshotB = CreateSnapshot("Snapshot B");
        snapshotB.Groups[0].Name = "Root B";
        snapshotB.Groups[1].Name = "Child B";
        var blockingProtector = new BlockingSecretProtector(context.CreateProtector());
        var loadingStore = context.CreateStore(blockingProtector);

        var loadTask = loadingStore.LoadAsync();
        await blockingProtector.Entered.Task;

        await context.CreateStore().SaveAsync(snapshotB);
        blockingProtector.Release.TrySetResult(true);

        var loaded = await loadTask;
        Assert.NotNull(loaded);
        Assert.Equal("Root A", loaded.Groups[0].Name);
        Assert.Equal("Child A", loaded.Groups[1].Name);
        Assert.Equal("Snapshot A", Assert.Single(loaded.Devices).Name);
    }

    [Fact]
    public async Task SaveAsync_InvalidTransportModePreservesExistingSnapshot()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        var source = CreateSnapshot();
        await store.SaveAsync(source);

        var invalid = CreateSnapshot("Snapshot B");
        invalid.Devices[0].TransportMode = (TransportMode)999;

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        AssertDeviceConfigurationEqual(source.Devices[0], Assert.Single(loaded.Devices));

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();
        var persistedMode = await ReadScalarAsync(
            connection,
            "SELECT transport_mode FROM camera_devices LIMIT 1;");
        Assert.Equal("Tcp", persistedMode);
        Assert.DoesNotContain("999", persistedMode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_InvalidStreamTypePreservesExistingSnapshot()
    {
        using var context = TestContext.Create();
        var store = context.CreateStore();
        var source = CreateSnapshot();
        await store.SaveAsync(source);

        var invalid = CreateSnapshot("Snapshot B");
        invalid.Devices[0].Channels[0].StreamType = (StreamType)999;

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        AssertDeviceConfigurationEqual(source.Devices[0], Assert.Single(loaded.Devices));

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();
        var persistedType = await ReadScalarAsync(
            connection,
            "SELECT stream_type FROM camera_channels WHERE id = $id;",
            ("$id", source.Devices[0].Channels[0].Id.ToString("N")));
        Assert.Equal("Main", persistedType);
        Assert.DoesNotContain("999", persistedType, StringComparison.Ordinal);
    }

    private static DeviceCatalogSnapshot CreateSnapshot(string deviceName = "Camera A")
    {
        var rootId = Guid.Parse("81000000-0000-0000-0000-000000000001");
        var childId = Guid.Parse("82000000-0000-0000-0000-000000000001");
        var deviceId = Guid.Parse("83000000-0000-0000-0000-000000000001");
        var mainId = Guid.Parse("84000000-0000-0000-0000-000000000001");
        var subId = Guid.Parse("85000000-0000-0000-0000-000000000001");

        var device = new CameraDevice
        {
            Id = deviceId,
            Name = deviceName,
            GroupId = childId,
            IpAddress = "192.0.2.10",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "camera-user",
            Password = "Password-Should-Never-Appear-In-Db",
            Manufacturer = "Hikvision",
            Model = "DS-2CD",
            TransportMode = TransportMode.Tcp,
            Status = CameraStatus.Online,
            Enabled = true,
            Remark = "Camera configuration"
        };
        device.Channels.Add(new CameraChannel
        {
            Id = mainId,
            DeviceId = deviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-main",
            Enabled = true
        });
        device.Channels.Add(new CameraChannel
        {
            Id = subId,
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
                    Name = "Root Group",
                    Sort = 1,
                    Enabled = true
                },
                new DeviceGroup
                {
                    Id = childId,
                    Name = "Child Group",
                    ParentId = rootId,
                    Sort = 2,
                    Enabled = false
                }
            ],
            Devices = [device]
        };
    }

    private static void AssertGroupEqual(DeviceGroup expected, DeviceGroup actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.ParentId, actual.ParentId);
        Assert.Equal(expected.Sort, actual.Sort);
        Assert.Equal(expected.Enabled, actual.Enabled);
    }

    private static void AssertDeviceConfigurationEqual(CameraDevice expected, CameraDevice actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.GroupId, actual.GroupId);
        Assert.Equal(expected.IpAddress, actual.IpAddress);
        Assert.Equal(expected.SdkPort, actual.SdkPort);
        Assert.Equal(expected.RtspPort, actual.RtspPort);
        Assert.Equal(expected.Username, actual.Username);
        Assert.Equal(expected.Password, actual.Password);
        Assert.Equal(expected.Manufacturer, actual.Manufacturer);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.TransportMode, actual.TransportMode);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.Remark, actual.Remark);
    }

    private static void AssertChannelConfigurationEqual(CameraChannel expected, CameraChannel actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.DeviceId, actual.DeviceId);
        Assert.Equal(expected.ChannelNo, actual.ChannelNo);
        Assert.Equal(expected.ChannelName, actual.ChannelName);
        Assert.Equal(expected.StreamType, actual.StreamType);
        Assert.Equal(expected.Enabled, actual.Enabled);
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, parameterValue) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = parameterValue;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadScalarAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, parameterValue) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = parameterValue;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result) ?? string.Empty;
    }

    private static void ClearSqliteConnectionPools()
    {
        var sqliteConnectionType = System.Reflection.Assembly
            .Load("Microsoft.Data.Sqlite")
            .GetType("Microsoft.Data.Sqlite.SqliteConnection");
        sqliteConnectionType?
            .GetMethod(
                "ClearAllPools",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null);
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
                "VideoMonitorSqliteDeviceCatalogTests",
                Guid.NewGuid().ToString("N"));
            return new TestContext(root);
        }

        public DbConnection CreateConnection() => Factory.CreateConnection();

        public ISecretProtector CreateProtector() =>
            new AesGcmSecretProtector(new FixedMasterKeyProvider());

        public SqliteDeviceCatalogStore CreateStore(ISecretProtector? protector = null) =>
            new(Factory, Initializer, protector ?? CreateProtector());

        public void Dispose()
        {
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

    private sealed class BlockingSecretProtector : ISecretProtector
    {
        private readonly ISecretProtector inner;

        public BlockingSecretProtector(ISecretProtector inner)
        {
            this.inner = inner;
        }

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default) =>
            inner.ProtectAsync(plaintext, purpose, cancellationToken);

        public async Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return await inner.UnprotectAsync(
                    protectedValue,
                    purpose,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class FailingSecretProtector : ISecretProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default) =>
            throw new CryptographicException("test encryption failure");

        public Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default) =>
            throw new CryptographicException("test decryption failure");
    }
}
