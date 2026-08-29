using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteDeviceCatalogStore : IDeviceCatalogStore
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteDatabaseInitializer databaseInitializer;
    private readonly ISecretProtector secretProtector;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public SqliteDeviceCatalogStore(
        SqliteConnectionFactory connectionFactory,
        SqliteDatabaseInitializer databaseInitializer,
        ISecretProtector secretProtector)
    {
        this.connectionFactory = connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
        this.databaseInitializer = databaseInitializer ??
            throw new ArgumentNullException(nameof(databaseInitializer));
        this.secretProtector = secretProtector ??
            throw new ArgumentNullException(nameof(secretProtector));
    }

    public async Task<DeviceCatalogSnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await databaseInitializer.InitializeAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var connection = CreateStoreConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction(deferred: true);
        List<DeviceGroup> groups;
        List<CameraDevice> devices;
        try
        {
            groups = await ReadGroupsAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            devices = await ReadDevicesAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            await ReadChannelsAsync(connection, transaction, devices, cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original load failure.
            }

            throw;
        }

        ValidateLoadedCatalog(groups, devices);

        return new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups = groups,
            Devices = devices
        };
    }

    public async Task SaveAsync(
        DeviceCatalogSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateSnapshot(snapshot);
            var encryptedPasswords = await EncryptPasswordsAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureDatabaseInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection = CreateStoreConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await ReplaceSnapshotAsync(
                        connection,
                        transaction,
                        snapshot,
                        encryptedPasswords,
                        cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the original save failure.
                }

                throw;
            }
        }
        finally
        {
            saveGate.Release();
        }
    }

    private async Task<Dictionary<Guid, string>> EncryptPasswordsAsync(
        DeviceCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var encryptedPasswords = new Dictionary<Guid, string>(snapshot.Devices.Count);
        foreach (var device in snapshot.Devices)
        {
            if (device.Password is null)
            {
                throw new InvalidDataException("设备密码无效。");
            }

            var purpose = $"camera-password:{device.Id:N}";
            var encryptedPassword = await secretProtector.ProtectAsync(
                    device.Password,
                    purpose,
                    cancellationToken)
                .ConfigureAwait(false);
            if (encryptedPassword is null)
            {
                throw new InvalidDataException("设备密码保护结果无效。");
            }

            encryptedPasswords.Add(device.Id, encryptedPassword);
        }

        return encryptedPasswords;
    }

    private async Task EnsureDatabaseInitializedAsync(
        CancellationToken cancellationToken)
    {
        var schemaVersion = 0;
        var hasSchemaMigrationsTable = false;

        await using (var connection = CreateStoreConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type = 'table' AND name = 'schema_migrations';
                    """;
                hasSchemaMigrationsTable = Convert.ToInt32(
                        await tableCommand.ExecuteScalarAsync(cancellationToken)
                            .ConfigureAwait(false)) > 0;
            }

            if (hasSchemaMigrationsTable)
            {
                await using var versionCommand = connection.CreateCommand();
                versionCommand.CommandText = "SELECT MAX(version) FROM schema_migrations;";
                var value = await versionCommand.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);
                schemaVersion = value is null or DBNull ? 0 : Convert.ToInt32(value);
            }
        }

        if (!hasSchemaMigrationsTable
            || schemaVersion < DeviceCatalogSnapshot.CurrentSchemaVersion)
        {
            await databaseInitializer.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (schemaVersion > DeviceCatalogSnapshot.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"数据库 SchemaVersion {schemaVersion} 高于当前支持版本 {DeviceCatalogSnapshot.CurrentSchemaVersion}。");
        }
    }

    private SqliteConnection CreateStoreConnection()
    {
        var connection = connectionFactory.CreateConnection();
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString)
        {
            Cache = SqliteCacheMode.Private
        };
        connection.ConnectionString = builder.ToString();
        return connection;
    }

    private static void ValidateSnapshot(DeviceCatalogSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != DeviceCatalogSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"不支持的设备目录 SchemaVersion：{snapshot.SchemaVersion}，当前仅支持版本 {DeviceCatalogSnapshot.CurrentSchemaVersion}。");
        }

        try
        {
            _ = new InMemoryDeviceCatalog(snapshot.Groups, snapshot.Devices);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("设备目录数据校验失败。", exception);
        }

        foreach (var device in snapshot.Devices)
        {
            if (!Enum.IsDefined(typeof(TransportMode), device.TransportMode))
            {
                throw new InvalidDataException("设备目录包含无效的传输模式。");
            }

            foreach (var channel in device.Channels)
            {
                if (!Enum.IsDefined(typeof(StreamType), channel.StreamType))
                {
                    throw new InvalidDataException("设备目录包含无效的码流类型。");
                }
            }
        }

        var duplicateStreamIdentity = snapshot.Devices
            .SelectMany(device => device.Channels)
            .GroupBy(channel => new
            {
                channel.DeviceId,
                channel.ChannelNo,
                channel.StreamType
            })
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateStreamIdentity is not null)
        {
            throw new InvalidDataException(
                "同一设备、通道号和码流类型只能存在一条通道配置。");
        }
    }

    private static async Task ReplaceSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceCatalogSnapshot snapshot,
        IReadOnlyDictionary<Guid, string> encryptedPasswords,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            DELETE FROM camera_channels;
            DELETE FROM camera_devices;
            UPDATE device_groups SET parent_id = NULL;
            DELETE FROM device_groups;
            """, cancellationToken).ConfigureAwait(false);

        foreach (var group in snapshot.Groups)
        {
            await InsertGroupAsync(connection, transaction, group, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var group in snapshot.Groups.Where(group => group.ParentId is not null))
        {
            await UpdateGroupParentAsync(connection, transaction, group, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var device in snapshot.Devices)
        {
            await InsertDeviceAsync(
                    connection,
                    transaction,
                    device,
                    encryptedPasswords[device.Id],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var device in snapshot.Devices)
        {
            foreach (var channel in device.Channels)
            {
                await InsertChannelAsync(connection, transaction, channel, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task InsertGroupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceGroup group,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO device_groups (id, name, parent_id, sort, enabled)
            VALUES ($id, $name, NULL, $sort, $enabled);
            """, cancellationToken,
            ("$id", group.Id.ToString("N")),
            ("$name", group.Name),
            ("$sort", group.Sort),
            ("$enabled", ToDatabaseBoolean(group.Enabled))).ConfigureAwait(false);
    }

    private static async Task UpdateGroupParentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceGroup group,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            UPDATE device_groups
            SET parent_id = $parentId
            WHERE id = $id;
            """, cancellationToken,
            ("$parentId", group.ParentId!.Value.ToString("N")),
            ("$id", group.Id.ToString("N"))).ConfigureAwait(false);
    }

    private static async Task InsertDeviceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CameraDevice device,
        string encryptedPassword,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO camera_devices (
                id, group_id, name, ip_address, sdk_port, rtsp_port,
                username, password_ciphertext, manufacturer, model,
                transport_mode, enabled, remark)
            VALUES (
                $id, $groupId, $name, $ipAddress, $sdkPort, $rtspPort,
                $username, $passwordCiphertext, $manufacturer, $model,
                $transportMode, $enabled, $remark);
            """, cancellationToken,
            ("$id", device.Id.ToString("N")),
            ("$groupId", device.GroupId.ToString("N")),
            ("$name", device.Name),
            ("$ipAddress", device.IpAddress),
            ("$sdkPort", device.SdkPort),
            ("$rtspPort", device.RtspPort),
            ("$username", device.Username),
            ("$passwordCiphertext", encryptedPassword),
            ("$manufacturer", device.Manufacturer),
            ("$model", device.Model),
            ("$transportMode", device.TransportMode.ToString()),
            ("$enabled", ToDatabaseBoolean(device.Enabled)),
            ("$remark", device.Remark)).ConfigureAwait(false);
    }

    private static async Task InsertChannelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CameraChannel channel,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO camera_channels (
                id, device_id, channel_no, channel_name, stream_type, enabled)
            VALUES (
                $id, $deviceId, $channelNo, $channelName, $streamType, $enabled);
            """, cancellationToken,
            ("$id", channel.Id.ToString("N")),
            ("$deviceId", channel.DeviceId.ToString("N")),
            ("$channelNo", channel.ChannelNo),
            ("$channelName", channel.ChannelName),
            ("$streamType", channel.StreamType.ToString()),
            ("$enabled", ToDatabaseBoolean(channel.Enabled))).ConfigureAwait(false);
    }

    private async Task<List<DeviceGroup>> ReadGroupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, name, parent_id, sort, enabled
            FROM device_groups
            ORDER BY sort, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var groups = new List<DeviceGroup>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new DeviceGroup
            {
                Id = ReadGuid(reader, 0, "device_groups.id"),
                Name = reader.GetString(1),
                ParentId = reader.IsDBNull(2)
                    ? null
                    : ReadGuid(reader, 2, "device_groups.parent_id"),
                Sort = reader.GetInt32(3),
                Enabled = ReadBoolean(reader, 4, "device_groups.enabled")
            });
        }

        return groups;
    }

    private async Task<List<CameraDevice>> ReadDevicesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, group_id, name, ip_address, sdk_port, rtsp_port,
                   username, password_ciphertext, manufacturer, model,
                   transport_mode, enabled, remark
            FROM camera_devices
            ORDER BY id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = new List<CameraDevice>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var deviceId = ReadGuid(reader, 0, "camera_devices.id");
            var transportMode = ReadEnum<TransportMode>(
                reader.GetString(10),
                "camera_devices.transport_mode");
            var password = await secretProtector.UnprotectAsync(
                    reader.GetString(7),
                    $"camera-password:{deviceId:N}",
                    cancellationToken)
                .ConfigureAwait(false);

            devices.Add(new CameraDevice
            {
                Id = deviceId,
                GroupId = ReadGuid(reader, 1, "camera_devices.group_id"),
                Name = reader.GetString(2),
                IpAddress = reader.GetString(3),
                SdkPort = reader.GetInt32(4),
                RtspPort = reader.GetInt32(5),
                Username = reader.GetString(6),
                Password = password,
                Manufacturer = reader.GetString(8),
                Model = reader.GetString(9),
                TransportMode = transportMode,
                Status = CameraStatus.Unknown,
                Enabled = ReadBoolean(reader, 11, "camera_devices.enabled"),
                Remark = reader.GetString(12)
            });
        }

        return devices;
    }

    private static async Task ReadChannelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CameraDevice> devices,
        CancellationToken cancellationToken)
    {
        var devicesById = devices.ToDictionary(device => device.Id);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, device_id, channel_no, channel_name, stream_type, enabled
            FROM camera_channels
            ORDER BY device_id, channel_no, stream_type, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var deviceId = ReadGuid(reader, 1, "camera_channels.device_id");
            if (!devicesById.TryGetValue(deviceId, out var device))
            {
                throw new InvalidDataException(
                    "数据库通道所属设备不存在。");
            }

            device.Channels.Add(new CameraChannel
            {
                Id = ReadGuid(reader, 0, "camera_channels.id"),
                DeviceId = deviceId,
                ChannelNo = reader.GetInt32(2),
                ChannelName = reader.GetString(3),
                StreamType = ReadEnum<StreamType>(
                    reader.GetString(4),
                    "camera_channels.stream_type"),
                Enabled = ReadBoolean(reader, 5, "camera_channels.enabled")
            });
        }
    }

    private static void ValidateLoadedCatalog(
        IReadOnlyList<DeviceGroup> groups,
        IReadOnlyList<CameraDevice> devices)
    {
        try
        {
            _ = new InMemoryDeviceCatalog(groups, devices);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("数据库设备目录数据校验失败。", exception);
        }
    }

    private static Guid ReadGuid(
        System.Data.Common.DbDataReader reader,
        int ordinal,
        string fieldName)
    {
        var value = reader.GetString(ordinal);
        if (!Guid.TryParseExact(value, "N", out var result))
        {
            throw new InvalidDataException($"数据库字段 {fieldName} 的 GUID 格式无效。");
        }

        return result;
    }

    private static bool ReadBoolean(
        System.Data.Common.DbDataReader reader,
        int ordinal,
        string fieldName)
    {
        var value = reader.GetInt64(ordinal);
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException($"数据库字段 {fieldName} 的布尔值无效。")
        };
    }

    private static TEnum ReadEnum<TEnum>(string value, string fieldName)
        where TEnum : struct, Enum
    {
        if (!Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            || !Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            || !Enum.IsDefined(typeof(TEnum), result))
        {
            throw new InvalidDataException($"数据库字段 {fieldName} 的枚举值无效。");
        }

        return result;
    }

    private static async Task ExecuteNonQueryAsync(
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

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int ToDatabaseBoolean(bool value) => value ? 1 : 0;
}
