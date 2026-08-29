using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VideoMonitor.Infrastructure.Paths;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteBackupService : ISqliteBackupService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IAppPathProvider paths;
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteDatabaseInitializer databaseInitializer;
    private readonly SemaphoreSlim backupGate = new(1, 1);

    public SqliteBackupService(
        IAppPathProvider paths,
        SqliteConnectionFactory connectionFactory,
        SqliteDatabaseInitializer databaseInitializer)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.connectionFactory = connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
        this.databaseInitializer = databaseInitializer ??
            throw new ArgumentNullException(nameof(databaseInitializer));
    }

    public async Task<SqliteBackupResult> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        await backupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var backupDirectory = string.Empty;
        var backupDirectoryCreated = false;
        try
        {
            await databaseInitializer.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(paths.BackupsDirectory);
            var directoryName =
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
            backupDirectory = Path.Combine(paths.BackupsDirectory, directoryName);
            if (Directory.Exists(backupDirectory) || File.Exists(backupDirectory))
            {
                throw new IOException("备份目录已存在。");
            }

            Directory.CreateDirectory(backupDirectory);
            backupDirectoryCreated = true;

            var temporaryDatabasePath = Path.Combine(
                backupDirectory,
                "videomonitor.db.tmp");
            var databasePath = Path.Combine(backupDirectory, "videomonitor.db");
            var temporaryManifestPath = Path.Combine(
                backupDirectory,
                "manifest.json.tmp");
            var manifestPath = Path.Combine(backupDirectory, "manifest.json");

            await using (var sourceConnection = connectionFactory.CreateConnection())
            await using (var destinationConnection = CreateDestinationConnection(
                temporaryDatabasePath))
            {
                await sourceConnection.OpenAsync(cancellationToken)
                    .ConfigureAwait(false);
                destinationConnection.Open();
                cancellationToken.ThrowIfCancellationRequested();
                sourceConnection.BackupDatabase(destinationConnection);
                cancellationToken.ThrowIfCancellationRequested();
                FinalizeDestinationDatabase(destinationConnection);
            }

            var databaseSha256 = await ComputeSha256Async(
                    temporaryDatabasePath,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryDatabasePath, databasePath, overwrite: false);

            var manifest = new SqliteBackupManifest(
                SqliteDatabaseInitializer.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                GetApplicationVersion(),
                databaseSha256);
            await WriteManifestAsync(
                    temporaryManifestPath,
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryManifestPath, manifestPath, overwrite: false);

            return new SqliteBackupResult(
                backupDirectory,
                databasePath,
                manifestPath,
                databaseSha256);
        }
        catch
        {
            if (backupDirectoryCreated)
            {
                try
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
                catch
                {
                    // Preserve the original backup failure.
                }
            }

            throw;
        }
        finally
        {
            backupGate.Release();
        }
    }

    private static SqliteConnection CreateDestinationConnection(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
        return new SqliteConnection(connectionString);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void FinalizeDestinationDatabase(SqliteConnection connection)
    {
        using var checkpointCommand = connection.CreateCommand();
        checkpointCommand.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        checkpointCommand.ExecuteNonQuery();

        using var journalModeCommand = connection.CreateCommand();
        journalModeCommand.CommandText = "PRAGMA journal_mode = DELETE;";
        journalModeCommand.ExecuteScalar();
    }

    private static async Task WriteManifestAsync(
        string path,
        SqliteBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(
                stream,
                manifest,
                ManifestJsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static string GetApplicationVersion()
    {
        return typeof(SqliteBackupService).Assembly.GetName().Version?.ToString()
            is { Length: > 0 } version
            ? version
            : "unknown";
    }
}
