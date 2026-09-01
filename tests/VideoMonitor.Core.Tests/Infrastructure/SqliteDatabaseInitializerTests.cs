using System.Data.Common;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesCurrentSchema()
    {
        using var context = TestContext.Create();
        var initializer = context.CreateInitializer();

        await initializer.InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        var tables = await ReadColumnAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

        Assert.Equal(
            new[]
            {
                "camera_channels",
                "camera_devices",
                "device_groups",
                "media_settings",
                "schema_migrations",
                "server_settings"
            },
            tables);

        Assert.Equal(4L, await ReadScalarAsync<long>(
            connection,
            "SELECT MAX(version) FROM schema_migrations;"));

        await AssertRevisionColumnAsync(connection, "device_groups");
        await AssertRevisionColumnAsync(connection, "camera_devices");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        using var context = TestContext.Create();
        var initializer = context.CreateInitializer();

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 1;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 2;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 3;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 4;"));
        Assert.Equal(6, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
    }

    [Fact]
    public async Task InitializeAsync_UpgradesV1RowsToRevisionOne()
    {
        using var context = TestContext.Create();
        await context.CreateV1DatabaseAsync();

        await context.CreateInitializer().InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(4L, await ReadScalarAsync<long>(
            connection,
            "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Equal("Legacy Group", await ReadScalarAsync<string>(
            connection,
            "SELECT name FROM device_groups WHERE id = 'legacy-group';"));
        Assert.Equal("Legacy Camera", await ReadScalarAsync<string>(
            connection,
            "SELECT name FROM camera_devices WHERE id = 'legacy-device';"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT revision FROM device_groups WHERE id = 'legacy-group';"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT revision FROM camera_devices WHERE id = 'legacy-device';"));
    }

    [Fact]
    public async Task InitializeAsync_UpgradesV2KnownRootKinds()
    {
        using var context = TestContext.Create();
        await context.CreateV2DatabaseAsync();
        Assert.Equal(2, await context.ReadMaxSchemaVersionAsync());
        await context.InsertRootAsync("卸矿站监控");
        await context.InsertRootAsync("溜井监控");
        await context.InsertRootAsync("巷道监控");

        await context.CreateInitializer().InitializeAsync();

        Assert.Equal("UnloadingStation", await context.ReadGroupKindAsync("卸矿站监控"));
        Assert.Equal("Chute", await context.ReadGroupKindAsync("溜井监控"));
        Assert.Equal("Tunnel", await context.ReadGroupKindAsync("巷道监控"));
    }

    [Fact]
    public async Task InitializeAsync_LeavesUnknownRootKindNull()
    {
        using var context = TestContext.Create();
        await context.CreateV2DatabaseAsync();
        Assert.Equal(2, await context.ReadMaxSchemaVersionAsync());
        await context.InsertRootAsync("现场自定义分类");

        await context.CreateInitializer().InitializeAsync();

        Assert.Null(await context.ReadGroupKindAsync("现场自定义分类"));
        Assert.Equal(4, await context.ReadMaxSchemaVersionAsync());
    }

    [Fact]
    public async Task V3DatabaseUpgradesToV4MediaSettings()
    {
        using var context = TestContext.Create();
        await context.CreateV3DatabaseAsync();

        await context.CreateInitializer().InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(4L, await ReadScalarAsync<long>(
            connection,
            "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM media_settings;"));
        Assert.Equal(string.Empty, await ReadScalarAsync<string>(
            connection,
            "SELECT zlm_api_base_url FROM media_settings WHERE id = 1;"));
        Assert.Equal(string.Empty, await ReadScalarAsync<string>(
            connection,
            "SELECT playback_base_url FROM media_settings WHERE id = 1;"));
        Assert.Equal("__defaultVhost__", await ReadScalarAsync<string>(
            connection,
            "SELECT vhost FROM media_settings WHERE id = 1;"));
        Assert.Equal("videomonitor", await ReadScalarAsync<string>(
            connection,
            "SELECT formal_app FROM media_settings WHERE id = 1;"));
        Assert.Equal("videomonitor-test", await ReadScalarAsync<string>(
            connection,
            "SELECT test_app FROM media_settings WHERE id = 1;"));
        Assert.Equal(string.Empty, await ReadScalarAsync<string>(
            connection,
            "SELECT zlm_secret_ciphertext FROM media_settings WHERE id = 1;"));
        Assert.Equal(30L, await ReadScalarAsync<long>(
            connection,
            "SELECT no_reader_grace_seconds FROM media_settings WHERE id = 1;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT revision FROM media_settings WHERE id = 1;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM device_groups;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM camera_devices;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM camera_channels;"));
    }

    [Fact]
    public async Task V4InitializationIsIdempotent()
    {
        using var context = TestContext.Create();

        await context.CreateInitializer().InitializeAsync();
        await context.CreateInitializer().InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM media_settings WHERE id = 1;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 4;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT revision FROM media_settings WHERE id = 1;"));
    }

    [Fact]
    public async Task Schema_DoesNotPersistRuntimeFields()
    {
        using var context = TestContext.Create();
        await context.CreateInitializer().InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        var channelColumns = await ReadColumnAsync(
            connection,
            "PRAGMA table_info(camera_channels);",
            columnIndex: 1);
        var groupColumns = await ReadColumnAsync(
            connection,
            "PRAGMA table_info(device_groups);",
            columnIndex: 1);
        var deviceColumns = await ReadColumnAsync(
            connection,
            "PRAGMA table_info(camera_devices);",
            columnIndex: 1);

        Assert.Contains("group_kind", groupColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("stream_id", channelColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("status", deviceColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera_status", deviceColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'stream_profiles';"));
    }

    [Fact]
    public async Task CameraChannel_HasCompositeUniqueConstraint()
    {
        using var context = TestContext.Create();
        await context.CreateInitializer().InitializeAsync();

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            INSERT INTO device_groups (id, name, parent_id, sort, enabled)
            VALUES ('group-1', 'Group 1', NULL, 0, 1);
            INSERT INTO camera_devices (
                id, group_id, name, ip_address, sdk_port, rtsp_port,
                username, password_ciphertext, manufacturer, model,
                transport_mode, enabled, remark)
            VALUES (
                'device-1', 'group-1', 'Camera 1', '192.0.2.1', 8000, 554,
                'user', 'ciphertext', 'Vendor', 'Model', 'Tcp', 1, '');
            """);

        await InsertChannelAsync(connection, "channel-main", "Main");
        await InsertChannelAsync(connection, "channel-sub", "Sub");

        await Assert.ThrowsAnyAsync<DbException>(() =>
            InsertChannelAsync(connection, "channel-main-duplicate", "Main"));
    }

    [Fact]
    public async Task NewerSchemaVersion_IsRejected()
    {
        using var context = TestContext.Create();
        await using (var connection = context.CreateConnection())
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                CREATE TABLE schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL);
                INSERT INTO schema_migrations (version, applied_at_utc)
                VALUES (5, '2099-01-01T00:00:00.0000000+00:00');
                """);
        }

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.CreateInitializer().InitializeAsync());

        Assert.Contains("SchemaVersion", exception.Message, StringComparison.Ordinal);

        await using var verifyConnection = context.CreateConnection();
        await verifyConnection.OpenAsync();
        Assert.Equal(5L, await ReadScalarAsync<long>(
            verifyConnection,
            "SELECT MAX(version) FROM schema_migrations;"));
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCallsRemainConsistent()
    {
        using var context = TestContext.Create();
        var initializers = Enumerable.Range(0, 4)
            .Select(_ => context.CreateInitializer())
            .ToArray();

        await Task.WhenAll(initializers.Select(initializer => initializer.InitializeAsync()));

        await using var connection = context.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 1;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 2;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 3;"));
        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 4;"));
        Assert.Equal(6L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
    }

    private static async Task AssertRevisionColumnAsync(
        DbConnection connection,
        string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        var found = false;
        while (await reader.ReadAsync())
        {
            if (!string.Equals(reader.GetString(1), "revision", StringComparison.Ordinal))
            {
                continue;
            }

            found = true;
            Assert.Equal("INTEGER", reader.GetString(2));
            Assert.Equal(1, reader.GetInt32(3));
            Assert.Equal("1", Convert.ToString(reader.GetValue(4)));
        }

        Assert.True(found, $"Table '{tableName}' does not contain a revision column.");
    }

    private static async Task InsertChannelAsync(
        DbConnection connection,
        string id,
        string streamType)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO camera_channels (
                id, device_id, channel_no, channel_name, stream_type, enabled)
            VALUES ('{id}', 'device-1', 1, 'Channel 1', '{streamType}', 1);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadColumnAsync(
        DbConnection connection,
        string sql,
        int columnIndex = 0)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(columnIndex));
        }

        return values;
    }

    private static async Task<T> ReadScalarAsync<T>(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private sealed class TestContext : IDisposable
    {
        private TestContext(string root)
        {
            Provider = new DefaultAppPathProvider(new ServerStorageOptions { RootPath = root });
            new ServerStorageLayout(Provider).EnsureCreated();
        }

        public DefaultAppPathProvider Provider { get; }

        public static TestContext Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "VideoMonitorSqliteTests", Guid.NewGuid().ToString("N"));
            return new TestContext(root);
        }

        public DbConnection CreateConnection()
        {
            return new SqliteConnectionFactory(Provider).CreateConnection();
        }

        public async Task CreateV1DatabaseAsync()
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                CREATE TABLE schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );

                CREATE TABLE device_groups (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT NOT NULL,
                    parent_id TEXT NULL,
                    sort INTEGER NOT NULL,
                    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
                    FOREIGN KEY (parent_id)
                        REFERENCES device_groups(id)
                        ON DELETE RESTRICT
                );

                CREATE TABLE camera_devices (
                    id TEXT NOT NULL PRIMARY KEY,
                    group_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    ip_address TEXT NOT NULL,
                    sdk_port INTEGER NOT NULL CHECK (sdk_port BETWEEN 1 AND 65535),
                    rtsp_port INTEGER NOT NULL CHECK (rtsp_port BETWEEN 1 AND 65535),
                    username TEXT NOT NULL,
                    password_ciphertext TEXT NOT NULL,
                    manufacturer TEXT NOT NULL,
                    model TEXT NOT NULL,
                    transport_mode TEXT NOT NULL,
                    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
                    remark TEXT NOT NULL,
                    FOREIGN KEY (group_id)
                        REFERENCES device_groups(id)
                        ON DELETE RESTRICT
                );

                CREATE TABLE camera_channels (
                    id TEXT NOT NULL PRIMARY KEY,
                    device_id TEXT NOT NULL,
                    channel_no INTEGER NOT NULL CHECK (channel_no > 0),
                    channel_name TEXT NOT NULL,
                    stream_type TEXT NOT NULL,
                    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
                    FOREIGN KEY (device_id)
                        REFERENCES camera_devices(id)
                        ON DELETE CASCADE,
                    UNIQUE (device_id, channel_no, stream_type)
                );

                CREATE TABLE server_settings (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                INSERT INTO schema_migrations(version, applied_at_utc)
                VALUES (1, '2026-08-30T00:00:00.0000000+00:00');
                INSERT INTO device_groups(id, name, parent_id, sort, enabled)
                VALUES ('legacy-group', 'Legacy Group', NULL, 1, 1);
                INSERT INTO camera_devices(
                    id, group_id, name, ip_address, sdk_port, rtsp_port,
                    username, password_ciphertext, manufacturer, model,
                    transport_mode, enabled, remark)
                VALUES (
                    'legacy-device', 'legacy-group', 'Legacy Camera', '192.0.2.20',
                    8000, 554, 'legacy-user', 'legacy-ciphertext', 'Vendor',
                    'Model', 'Tcp', 1, 'legacy remark');
                """);
        }

        public async Task CreateV2DatabaseAsync()
        {
            await CreateV1DatabaseAsync();
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                ALTER TABLE device_groups
                ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

                ALTER TABLE camera_devices
                ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

                INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (2, $appliedAtUtc);
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$appliedAtUtc";
            parameter.Value = DateTimeOffset.UtcNow.ToString("O");
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public async Task CreateV3DatabaseAsync()
        {
            await CreateV2DatabaseAsync();
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                ALTER TABLE device_groups
                ADD COLUMN group_kind TEXT NULL;

                INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (3, $appliedAtUtc);

                INSERT INTO camera_channels (
                    id, device_id, channel_no, channel_name, stream_type, enabled)
                VALUES (
                    'legacy-channel', 'legacy-device', 1, 'Main', 'Main', 1);
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$appliedAtUtc";
            parameter.Value = DateTimeOffset.UtcNow.ToString("O");
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public async Task InsertRootAsync(string name)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO device_groups(id, name, parent_id, sort, enabled)
                VALUES ($id, $name, NULL, 0, 1);
                """;
            var id = command.CreateParameter();
            id.ParameterName = "$id";
            id.Value = Guid.NewGuid().ToString("N");
            command.Parameters.Add(id);
            var nameParameter = command.CreateParameter();
            nameParameter.ParameterName = "$name";
            nameParameter.Value = name;
            command.Parameters.Add(nameParameter);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string?> ReadGroupKindAsync(string name)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT group_kind FROM device_groups WHERE name = $name ORDER BY rowid DESC LIMIT 1;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = name;
            command.Parameters.Add(parameter);
            var value = await command.ExecuteScalarAsync();
            return value is null || Convert.IsDBNull(value) ? null : Convert.ToString(value);
        }

        public async Task<int> ReadMaxSchemaVersionAsync()
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public SqliteDatabaseInitializer CreateInitializer()
        {
            return new SqliteDatabaseInitializer(new SqliteConnectionFactory(Provider));
        }

        public void Dispose()
        {
            if (Directory.Exists(Provider.RootDirectory))
            {
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
    }
}
