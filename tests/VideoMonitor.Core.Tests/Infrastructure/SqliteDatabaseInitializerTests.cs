using System.Data.Common;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesV1Schema()
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
                "schema_migrations",
                "server_settings"
            },
            tables);

        Assert.Equal(1L, await ReadScalarAsync<long>(
            connection,
            "SELECT MAX(version) FROM schema_migrations;"));
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
        Assert.Equal(5, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
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
        var deviceColumns = await ReadColumnAsync(
            connection,
            "PRAGMA table_info(camera_devices);",
            columnIndex: 1);

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
                VALUES (2, '2099-01-01T00:00:00.0000000+00:00');
                """);
        }

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.CreateInitializer().InitializeAsync());

        Assert.Contains("SchemaVersion", exception.Message, StringComparison.Ordinal);

        await using var verifyConnection = context.CreateConnection();
        await verifyConnection.OpenAsync();
        Assert.Equal(2L, await ReadScalarAsync<long>(
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
        Assert.Equal(5L, await ReadScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
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
