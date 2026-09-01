using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteMediaSettingsRepository : IMediaSettingsRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly ISecretProtector secretProtector;

    public SqliteMediaSettingsRepository(
        SqliteConnectionFactory connectionFactory,
        ISecretProtector secretProtector)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.secretProtector = secretProtector
            ?? throw new ArgumentNullException(nameof(secretProtector));
    }

    public async Task<MediaSettingsDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var stored = await ReadStorageAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(stored);
    }

    public async Task<MediaSettingsStorageRecord> ReadStorageAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT zlm_api_base_url,
                   playback_base_url,
                   vhost,
                   formal_app,
                   test_app,
                   zlm_secret_ciphertext,
                   no_reader_grace_seconds,
                   revision
            FROM media_settings
            WHERE id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Media settings row is missing.");
        }

        return new MediaSettingsStorageRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt64(7));
    }

    public async Task<CatalogRepositoryResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var replaceSecret = !string.IsNullOrWhiteSpace(request.ZlmSecret);
        var replacementCiphertext = string.Empty;
        if (replaceSecret)
        {
            replacementCiphertext = await secretProtector.ProtectAsync(
                    request.ZlmSecret!,
                    SqliteMediaRuntimeSettingsProvider.MediaSecretPurpose,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var affectedRows = await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    """
                    UPDATE media_settings
                    SET zlm_api_base_url = $zlmApiBaseUrl,
                        playback_base_url = $playbackBaseUrl,
                        vhost = $vhost,
                        formal_app = $formalApp,
                        test_app = $testApp,
                        zlm_secret_ciphertext = CASE
                            WHEN $replaceSecret = 1 THEN $zlmSecretCiphertext
                            ELSE zlm_secret_ciphertext
                        END,
                        no_reader_grace_seconds = $noReaderGraceSeconds,
                        revision = revision + 1
                    WHERE id = 1 AND revision = $expectedRevision;
                    """,
                    cancellationToken,
                    ("$zlmApiBaseUrl", request.ZlmApiBaseUrl),
                    ("$playbackBaseUrl", request.PlaybackBaseUrl),
                    ("$vhost", request.Vhost),
                    ("$formalApp", request.FormalApp),
                    ("$testApp", request.TestApp),
                    ("$replaceSecret", replaceSecret ? 1 : 0),
                    ("$zlmSecretCiphertext", replacementCiphertext),
                    ("$noReaderGraceSeconds", request.NoReaderGraceSeconds),
                    ("$expectedRevision", request.ExpectedRevision))
                .ConfigureAwait(false);

            if (affectedRows == 0)
            {
                var currentRevision = await ReadRevisionAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
                await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                return new CatalogRepositoryResult<MediaSettingsDto>(
                    CatalogRepositoryStatus.RevisionConflict,
                    CurrentRevision: currentRevision);
            }

            var stored = await ReadStorageAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryResult<MediaSettingsDto>(
                CatalogRepositoryStatus.Success,
                ToDto(stored));
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private SqliteConnection CreateConnection()
    {
        var connection = connectionFactory.CreateConnection();
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString)
        {
            Cache = SqliteCacheMode.Private
        };
        connection.ConnectionString = builder.ToString();
        return connection;
    }

    private static MediaSettingsDto ToDto(MediaSettingsStorageRecord stored) =>
        new(
            stored.ZlmApiBaseUrl,
            stored.PlaybackBaseUrl,
            stored.Vhost,
            stored.FormalApp,
            stored.TestApp,
            !string.IsNullOrEmpty(stored.ZlmSecretCiphertext),
            stored.NoReaderGraceSeconds,
            stored.Revision);

    private static async Task<MediaSettingsStorageRecord> ReadStorageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT zlm_api_base_url,
                   playback_base_url,
                   vhost,
                   formal_app,
                   test_app,
                   zlm_secret_ciphertext,
                   no_reader_grace_seconds,
                   revision
            FROM media_settings
            WHERE id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Media settings row is missing.");
        }

        return new MediaSettingsStorageRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt64(7));
    }

    private static async Task<long?> ReadRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM media_settings WHERE id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
