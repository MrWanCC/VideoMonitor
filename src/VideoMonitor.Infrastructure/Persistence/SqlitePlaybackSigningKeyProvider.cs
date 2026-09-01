using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqlitePlaybackSigningKeyProvider : IPlaybackSigningKeyProvider
{
    private const int SigningKeyLength = 32;
    private const string SettingKey = "playback.signing-key.v1";
    private const string ProtectionPurpose = "playback-signing-key:v1";

    private static readonly SemaphoreSlim CreationGate = new(1, 1);

    private readonly SqliteConnectionFactory connectionFactory;
    private readonly ISecretProtector secretProtector;

    public SqlitePlaybackSigningKeyProvider(
        SqliteConnectionFactory connectionFactory,
        ISecretProtector secretProtector)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.secretProtector = secretProtector
            ?? throw new ArgumentNullException(nameof(secretProtector));
    }

    public async Task<byte[]> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await CreationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var storedValue = await ReadStoredValueAsync(
                    connection,
                    transaction: null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (storedValue is not null)
            {
                return await UnprotectAndDecodeAsync(
                        storedValue,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var rawKey = RandomNumberGenerator.GetBytes(SigningKeyLength);
            byte[]? createdKey = null;
            try
            {
                var protectedValue = await secretProtector.ProtectAsync(
                        Convert.ToBase64String(rawKey),
                        ProtectionPurpose,
                        cancellationToken)
                    .ConfigureAwait(false);

                await using var transaction = (SqliteTransaction)await connection
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    storedValue = await ReadStoredValueAsync(
                            connection,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (storedValue is null)
                    {
                        var inserted = await InsertStoredValueAsync(
                                connection,
                                transaction,
                                protectedValue,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (inserted == 1)
                        {
                            createdKey = (byte[])rawKey.Clone();
                        }

                        storedValue = await ReadStoredValueAsync(
                                connection,
                                transaction,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (storedValue is null)
                    {
                        throw new InvalidDataException(
                            "播放签名密钥持久化失败。");
                    }

                    await transaction.CommitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rawKey);
            }

            if (createdKey is not null)
            {
                return createdKey;
            }

            return await UnprotectAndDecodeAsync(
                    storedValue!,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CreationGate.Release();
        }
    }

    private async Task<byte[]> UnprotectAndDecodeAsync(
        string protectedValue,
        CancellationToken cancellationToken)
    {
        var encodedKey = await secretProtector.UnprotectAsync(
                protectedValue,
                ProtectionPurpose,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var key = Convert.FromBase64String(encodedKey);
            if (key.Length != SigningKeyLength)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidDataException(
                    "播放签名密钥长度无效。");
            }

            return key;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "播放签名密钥格式无效。",
                exception);
        }
    }

    private static async Task<string?> ReadStoredValueAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT value
            FROM server_settings
            WHERE key = $key;
            """;
        command.Parameters.AddWithValue("$key", SettingKey);
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task<int> InsertStoredValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string protectedValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO server_settings (
                key,
                value,
                updated_at_utc)
            VALUES (
                $key,
                $value,
                $updatedAtUtc);
            """;
        command.Parameters.AddWithValue("$key", SettingKey);
        command.Parameters.AddWithValue("$value", protectedValue);
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
