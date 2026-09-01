using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteCameraMediaCredentialReader : ICameraMediaCredentialReader
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly ISecretProtector secretProtector;

    public SqliteCameraMediaCredentialReader(
        SqliteConnectionFactory connectionFactory,
        ISecretProtector secretProtector)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.secretProtector = secretProtector
            ?? throw new ArgumentNullException(nameof(secretProtector));
    }

    public async Task<CameraMediaCredential> ReadAsync(
        Guid deviceId,
        Guid channelId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id,
                   c.id,
                   d.ip_address,
                   d.rtsp_port,
                   d.username,
                   d.password_ciphertext,
                   c.channel_no,
                   c.stream_type,
                   d.transport_mode,
                   c.device_id
            FROM camera_devices d
            INNER JOIN camera_channels c ON c.device_id = d.id
            WHERE d.id = $deviceId AND c.id = $channelId;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId.ToString("N"));
        command.Parameters.AddWithValue("$channelId", channelId.ToString("N"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("设备与通道关系无效。");
        }

        var storedDeviceId = ReadGuid(reader, 0);
        var storedChannelId = ReadGuid(reader, 1);
        var relationDeviceId = ReadGuid(reader, 9);
        if (storedDeviceId != deviceId
            || storedChannelId != channelId
            || relationDeviceId != deviceId)
        {
            throw new InvalidDataException("设备与通道关系无效。");
        }

        var ipAddress = reader.GetString(2);
        var rtspPort = reader.GetInt32(3);
        var username = reader.GetString(4);
        var passwordCiphertext = reader.GetString(5);
        var channelNo = reader.GetInt32(6);
        if (channelNo < 1 || rtspPort is < 1 or > 65535)
        {
            throw new InvalidDataException("设备媒体配置无效。");
        }

        var streamType = ReadEnum<StreamType>(reader.GetString(7));
        var transportMode = ReadEnum<TransportMode>(reader.GetString(8));

        var password = string.Empty;
        if (!string.IsNullOrEmpty(passwordCiphertext))
        {
            try
            {
                password = await secretProtector.UnprotectAsync(
                        passwordCiphertext,
                        $"camera-password:{deviceId:N}",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                throw new InvalidDataException("设备媒体凭据无效。");
            }

            if (password is null)
            {
                throw new InvalidDataException("设备媒体凭据无效。");
            }
        }

        return new CameraMediaCredential(
            deviceId,
            channelId,
            ipAddress,
            rtspPort,
            username,
            password,
            channelNo,
            streamType,
            transportMode);
    }

    private static Guid ReadGuid(
        System.Data.Common.DbDataReader reader,
        int ordinal)
    {
        var value = reader.GetString(ordinal);
        if (!Guid.TryParseExact(value, "N", out var result))
        {
            throw new InvalidDataException("设备媒体配置无效。");
        }

        return result;
    }

    private static TEnum ReadEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            || !Enum.IsDefined(typeof(TEnum), result)
            || !string.Equals(result.ToString(), value, StringComparison.Ordinal))
        {
            throw new InvalidDataException("设备媒体配置无效。");
        }

        return result;
    }
}
