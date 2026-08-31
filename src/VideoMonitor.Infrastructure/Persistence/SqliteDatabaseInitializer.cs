using Microsoft.Data.Sqlite;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer
{
    public const int CurrentSchemaVersion = 3;

    private static readonly SemaphoreSlim InitializationGate = new(1, 1);

    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteDatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA synchronous = NORMAL;", cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );
                """, cancellationToken).ConfigureAwait(false);

            var version = await ReadCurrentVersionAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            if (version > CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"数据库 SchemaVersion {version} 高于当前支持版本 {CurrentSchemaVersion}。");
            }

            if (version < 1)
            {
                await ApplyV1SchemaAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);

                await InsertV1MigrationAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                version = 1;
            }

            if (version < 2)
            {
                await ApplyV2SchemaAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);

                await InsertV2MigrationAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (version < 3)
            {
                await ApplyV3SchemaAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);

                await InsertV3MigrationAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original initialization failure.
            }

            throw;
        }
    }

    private static async Task ApplyV1SchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS device_groups (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                parent_id TEXT NULL,
                sort INTEGER NOT NULL,
                enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
                FOREIGN KEY (parent_id)
                    REFERENCES device_groups(id)
                    ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS camera_devices (
                id TEXT NOT NULL PRIMARY KEY,
                group_id TEXT NOT NULL,
                name TEXT NOT NULL,
                ip_address TEXT NOT NULL,
                sdk_port INTEGER NOT NULL
                    CHECK (sdk_port BETWEEN 1 AND 65535),
                rtsp_port INTEGER NOT NULL
                    CHECK (rtsp_port BETWEEN 1 AND 65535),
                username TEXT NOT NULL,
                password_ciphertext TEXT NOT NULL,
                manufacturer TEXT NOT NULL,
                model TEXT NOT NULL,
                transport_mode TEXT NOT NULL,
                enabled INTEGER NOT NULL
                    CHECK (enabled IN (0, 1)),
                remark TEXT NOT NULL,
                FOREIGN KEY (group_id)
                    REFERENCES device_groups(id)
                    ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS camera_channels (
                id TEXT NOT NULL PRIMARY KEY,
                device_id TEXT NOT NULL,
                channel_no INTEGER NOT NULL
                    CHECK (channel_no > 0),
                channel_name TEXT NOT NULL,
                stream_type TEXT NOT NULL,
                enabled INTEGER NOT NULL
                    CHECK (enabled IN (0, 1)),
                FOREIGN KEY (device_id)
                    REFERENCES camera_devices(id)
                    ON DELETE CASCADE,
                UNIQUE (device_id, channel_no, stream_type)
            );

            CREATE TABLE IF NOT EXISTS server_settings (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertV1MigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES (1, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyV2SchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            ALTER TABLE device_groups
            ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

            ALTER TABLE camera_devices
            ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertV2MigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES (2, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyV3SchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            ALTER TABLE device_groups
            ADD COLUMN group_kind TEXT NULL;

            UPDATE device_groups
            SET group_kind = 'UnloadingStation'
            WHERE parent_id IS NULL AND name = '卸矿站监控';

            UPDATE device_groups
            SET group_kind = 'Chute'
            WHERE parent_id IS NULL AND name = '溜井监控';

            UPDATE device_groups
            SET group_kind = 'Tunnel'
            WHERE parent_id IS NULL AND name = '巷道监控';
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertV3MigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES (3, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadCurrentVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
