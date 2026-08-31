using System.Data.Common;
using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteCentralCatalogRepository : ICentralCatalogRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly ISecretProtector secretProtector;

    public SqliteCentralCatalogRepository(
        SqliteConnectionFactory connectionFactory,
        ISecretProtector secretProtector)
    {
        this.connectionFactory = connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
        this.secretProtector = secretProtector ??
            throw new ArgumentNullException(nameof(secretProtector));
    }

    public async Task<CatalogSnapshotDto> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateStoreConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            var groups = await ReadGroupsAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
            var devices = await ReadDevicesAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
            await ReadChannelsAsync(
                    connection,
                    transaction,
                    devices,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogSnapshotDto(
                groups,
                devices.Select(ToDeviceDto).ToArray());
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<DeviceGroupDto?> GetGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateStoreConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            var group = await ReadGroupAsync(
                    connection,
                    transaction,
                    id,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return group;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CameraDeviceDto?> GetDeviceAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateStoreConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            var device = await ReadDeviceAsync(
                    connection,
                    transaction,
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
            if (device is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await ReadChannelsForDeviceAsync(
                    connection,
                    transaction,
                    device,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToDeviceDto(device);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogRepositoryResult<DeviceGroupDto>> CreateGroupAsync(
        DeviceGroup group,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        await using var connection = CreateStoreConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO device_groups (id, name, parent_id, sort, enabled, group_kind, revision)
                    VALUES ($id, $name, $parentId, $sort, $enabled, $groupKind, 1);
                    """,
                    cancellationToken,
                    ("$id", group.Id.ToString("N")),
                    ("$name", group.Name),
                    ("$parentId", group.ParentId?.ToString("N")),
                    ("$sort", group.Sort),
                    ("$enabled", ToDatabaseBoolean(group.Enabled)),
                    ("$groupKind", group.Kind?.ToString()))
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.Success,
                ToGroupDto(group, revision: 1));
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogRepositoryResult<CameraDeviceDto>> CreateDeviceAsync(
        CameraDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.Password is null)
        {
            throw new InvalidDataException("设备密码无效。");
        }

        if (device.Channels
            .GroupBy(channel => new { channel.ChannelNo, channel.StreamType })
            .Any(group => group.Count() > 1))
        {
            return new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.ChannelConflict);
        }

        var passwordCiphertext = string.Empty;
        if (!string.IsNullOrEmpty(device.Password))
        {
            passwordCiphertext = await secretProtector.ProtectAsync(
                    device.Password,
                    $"camera-password:{device.Id:N}",
                    cancellationToken)
                .ConfigureAwait(false);
            if (passwordCiphertext is null)
            {
                throw new InvalidDataException("设备密码保护结果无效。");
            }
        }

        await using var connection = CreateStoreConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO camera_devices (
                        id, group_id, name, ip_address, sdk_port, rtsp_port,
                        username, password_ciphertext, manufacturer, model,
                        transport_mode, enabled, remark, revision)
                    VALUES (
                        $id, $groupId, $name, $ipAddress, $sdkPort, $rtspPort,
                        $username, $passwordCiphertext, $manufacturer, $model,
                        $transportMode, $enabled, $remark, 1);
                    """,
                    cancellationToken,
                    ("$id", device.Id.ToString("N")),
                    ("$groupId", device.GroupId.ToString("N")),
                    ("$name", device.Name),
                    ("$ipAddress", device.IpAddress),
                    ("$sdkPort", device.SdkPort),
                    ("$rtspPort", device.RtspPort),
                    ("$username", device.Username),
                    ("$passwordCiphertext", passwordCiphertext),
                    ("$manufacturer", device.Manufacturer),
                    ("$model", device.Model),
                    ("$transportMode", device.TransportMode.ToString()),
                    ("$enabled", ToDatabaseBoolean(device.Enabled)),
                    ("$remark", device.Remark))
                .ConfigureAwait(false);

            foreach (var channel in device.Channels)
            {
                await ExecuteNonQueryAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO camera_channels (
                            id, device_id, channel_no, channel_name, stream_type, enabled)
                        VALUES (
                            $id, $deviceId, $channelNo, $channelName, $streamType, $enabled);
                        """,
                        cancellationToken,
                        ("$id", channel.Id.ToString("N")),
                        ("$deviceId", channel.DeviceId.ToString("N")),
                        ("$channelNo", channel.ChannelNo),
                        ("$channelName", channel.ChannelName),
                        ("$streamType", channel.StreamType.ToString()),
                        ("$enabled", ToDatabaseBoolean(channel.Enabled)))
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.Success,
                ToDeviceDto(device, revision: 1, hasPassword: !string.IsNullOrEmpty(passwordCiphertext)));
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogRepositoryResult<DeviceGroupDto>> UpdateGroupAsync(
        DeviceGroup group,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        await using var connection = CreateStoreConnection();
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
                    UPDATE device_groups
                    SET name = $name,
                        parent_id = $parentId,
                        sort = $sort,
                        enabled = $enabled,
                        group_kind = $groupKind,
                        revision = revision + 1
                    WHERE id = $id AND revision = $expectedRevision;
                    """,
                    cancellationToken,
                    ("$id", group.Id.ToString("N")),
                    ("$name", group.Name),
                    ("$parentId", group.ParentId?.ToString("N")),
                    ("$sort", group.Sort),
                    ("$enabled", ToDatabaseBoolean(group.Enabled)),
                    ("$groupKind", group.Kind?.ToString()),
                    ("$expectedRevision", expectedRevision))
                .ConfigureAwait(false);

            if (affectedRows == 0)
            {
                var currentRevision = await ReadGroupRevisionAsync(
                        connection,
                        transaction,
                        group.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                return currentRevision is null
                    ? new CatalogRepositoryResult<DeviceGroupDto>(
                        CatalogRepositoryStatus.NotFound)
                    : new CatalogRepositoryResult<DeviceGroupDto>(
                        CatalogRepositoryStatus.RevisionConflict,
                        CurrentRevision: currentRevision);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.Success,
                ToGroupDto(group, expectedRevision + 1));
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogRepositoryDeleteResult> DeleteGroupAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateStoreConnection();
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
                    DELETE FROM device_groups
                    WHERE id = $id
                      AND revision = $expectedRevision
                      AND NOT EXISTS (
                          SELECT 1 FROM device_groups child
                          WHERE child.parent_id = device_groups.id)
                      AND NOT EXISTS (
                          SELECT 1 FROM camera_devices device
                          WHERE device.group_id = device_groups.id);
                    """,
                    cancellationToken,
                    ("$id", id.ToString("N")),
                    ("$expectedRevision", expectedRevision))
                .ConfigureAwait(false);

            if (affectedRows == 0)
            {
                var state = await ReadGroupStateAsync(
                        connection,
                        transaction,
                        id,
                        cancellationToken)
                    .ConfigureAwait(false);
                await RollbackQuietlyAsync(transaction).ConfigureAwait(false);

                if (state is null)
                {
                    return new CatalogRepositoryDeleteResult(
                        CatalogRepositoryStatus.NotFound);
                }

                if (state.Revision != expectedRevision)
                {
                    return new CatalogRepositoryDeleteResult(
                        CatalogRepositoryStatus.RevisionConflict,
                        state.Revision);
                }

                return new CatalogRepositoryDeleteResult(
                    CatalogRepositoryStatus.GroupNotEmpty);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryDeleteResult(CatalogRepositoryStatus.Success);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogRepositoryResult<CameraDeviceDto>> UpdateDeviceAsync(
        CameraDevice device,
        string? newPassword,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (newPassword is not null && newPassword.Length == 0)
        {
            throw new ArgumentException(
                "更新设备密码不能为空。",
                nameof(newPassword));
        }

        if (device.Channels
            .GroupBy(channel => new { channel.ChannelNo, channel.StreamType })
            .Any(group => group.Count() > 1))
        {
            return new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.ChannelConflict);
        }

        var passwordCiphertext = string.Empty;
        var replacePassword = newPassword is not null;
        if (replacePassword)
        {
            passwordCiphertext = await secretProtector.ProtectAsync(
                    newPassword!,
                    $"camera-password:{device.Id:N}",
                    cancellationToken)
                .ConfigureAwait(false);
            if (passwordCiphertext is null)
            {
                throw new InvalidDataException("设备密码保护结果无效。");
            }
        }

        await using var connection = CreateStoreConnection();
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
                    UPDATE camera_devices
                    SET group_id = $groupId,
                        name = $name,
                        ip_address = $ipAddress,
                        sdk_port = $sdkPort,
                        rtsp_port = $rtspPort,
                        username = $username,
                        password_ciphertext = CASE
                            WHEN $replacePassword = 1 THEN $passwordCiphertext
                            ELSE password_ciphertext
                        END,
                        manufacturer = $manufacturer,
                        model = $model,
                        transport_mode = $transportMode,
                        enabled = $enabled,
                        remark = $remark,
                        revision = revision + 1
                    WHERE id = $id AND revision = $expectedRevision;
                    """,
                    cancellationToken,
                    ("$id", device.Id.ToString("N")),
                    ("$groupId", device.GroupId.ToString("N")),
                    ("$name", device.Name),
                    ("$ipAddress", device.IpAddress),
                    ("$sdkPort", device.SdkPort),
                    ("$rtspPort", device.RtspPort),
                    ("$username", device.Username),
                    ("$replacePassword", replacePassword ? 1 : 0),
                    ("$passwordCiphertext", passwordCiphertext),
                    ("$manufacturer", device.Manufacturer),
                    ("$model", device.Model),
                    ("$transportMode", device.TransportMode.ToString()),
                    ("$enabled", ToDatabaseBoolean(device.Enabled)),
                    ("$remark", device.Remark),
                    ("$expectedRevision", expectedRevision))
                .ConfigureAwait(false);

            if (affectedRows == 0)
            {
                var currentRevision = await ReadDeviceRevisionAsync(
                        connection,
                        transaction,
                        device.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                return currentRevision is null
                    ? new CatalogRepositoryResult<CameraDeviceDto>(
                        CatalogRepositoryStatus.NotFound)
                    : new CatalogRepositoryResult<CameraDeviceDto>(
                        CatalogRepositoryStatus.RevisionConflict,
                        CurrentRevision: currentRevision);
            }

            await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    "DELETE FROM camera_channels WHERE device_id = $deviceId;",
                    cancellationToken,
                    ("$deviceId", device.Id.ToString("N")))
                .ConfigureAwait(false);
            await InsertChannelsAsync(
                    connection,
                    transaction,
                    device.Channels,
                    cancellationToken)
                .ConfigureAwait(false);
            var hasPassword = await ReadHasPasswordAsync(
                    connection,
                    transaction,
                    device.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.Success,
                ToDeviceDto(device, expectedRevision + 1, hasPassword));
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogRepositoryDeleteResult> DeleteDeviceAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateStoreConnection();
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
                    DELETE FROM camera_devices
                    WHERE id = $id AND revision = $expectedRevision;
                    """,
                    cancellationToken,
                    ("$id", id.ToString("N")),
                    ("$expectedRevision", expectedRevision))
                .ConfigureAwait(false);

            if (affectedRows == 0)
            {
                var currentRevision = await ReadDeviceRevisionAsync(
                        connection,
                        transaction,
                        id,
                        cancellationToken)
                    .ConfigureAwait(false);
                await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                return currentRevision is null
                    ? new CatalogRepositoryDeleteResult(
                        CatalogRepositoryStatus.NotFound)
                    : new CatalogRepositoryDeleteResult(
                        CatalogRepositoryStatus.RevisionConflict,
                        currentRevision);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CatalogRepositoryDeleteResult(CatalogRepositoryStatus.Success);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<long?> ReadGroupRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM device_groups WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<GroupState?> ReadGroupStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision,
                   EXISTS (
                       SELECT 1 FROM device_groups child
                       WHERE child.parent_id = device_groups.id),
                   EXISTS (
                       SELECT 1 FROM camera_devices device
                       WHERE device.group_id = device_groups.id)
            FROM device_groups
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new GroupState(
            reader.GetInt64(0),
            reader.GetInt64(1) == 1,
            reader.GetInt64(2) == 1);
    }

    private static async Task<long?> ReadDeviceRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM camera_devices WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task InsertChannelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CameraChannel> channels,
        CancellationToken cancellationToken)
    {
        foreach (var channel in channels)
        {
            await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO camera_channels (
                        id, device_id, channel_no, channel_name, stream_type, enabled)
                    VALUES (
                        $id, $deviceId, $channelNo, $channelName, $streamType, $enabled);
                    """,
                    cancellationToken,
                    ("$id", channel.Id.ToString("N")),
                    ("$deviceId", channel.DeviceId.ToString("N")),
                    ("$channelNo", channel.ChannelNo),
                    ("$channelName", channel.ChannelName),
                    ("$streamType", channel.StreamType.ToString()),
                    ("$enabled", ToDatabaseBoolean(channel.Enabled)))
                .ConfigureAwait(false);
        }
    }

    private static async Task<bool> ReadHasPasswordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is not null
            and not DBNull
            && !string.IsNullOrEmpty(Convert.ToString(value));
    }

    private async Task<List<DeviceGroupDto>> ReadGroupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, name, parent_id, sort, enabled, group_kind, revision
            FROM device_groups
            ORDER BY sort, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var groups = new List<DeviceGroupDto>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new DeviceGroupDto(
                ReadGuid(reader, 0, "device_groups.id"),
                reader.GetString(1),
                reader.IsDBNull(2)
                    ? null
                    : ReadGuid(reader, 2, "device_groups.parent_id"),
                reader.GetInt32(3),
                ReadBoolean(reader, 4, "device_groups.enabled"),
                ReadNullableEnum<MonitorGroupType>(
                    reader,
                    5,
                    "device_groups.group_kind"),
                reader.GetInt64(6)));
        }

        return groups;
    }

    private static async Task<DeviceGroupDto?> ReadGroupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, name, parent_id, sort, enabled, group_kind, revision
            FROM device_groups
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DeviceGroupDto(
            ReadGuid(reader, 0, "device_groups.id"),
            reader.GetString(1),
            reader.IsDBNull(2)
                ? null
                : ReadGuid(reader, 2, "device_groups.parent_id"),
            reader.GetInt32(3),
            ReadBoolean(reader, 4, "device_groups.enabled"),
            ReadNullableEnum<MonitorGroupType>(
                reader,
                5,
                "device_groups.group_kind"),
            reader.GetInt64(6));
    }

    private async Task<List<DeviceReadModel>> ReadDevicesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, group_id, name, ip_address, sdk_port, rtsp_port,
                   username, password_ciphertext, manufacturer, model,
                   transport_mode, enabled, remark, revision
            FROM camera_devices
            ORDER BY id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = new List<DeviceReadModel>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            devices.Add(ReadDevice(reader));
        }

        return devices;
    }

    private async Task<DeviceReadModel?> ReadDeviceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, group_id, name, ip_address, sdk_port, rtsp_port,
                   username, password_ciphertext, manufacturer, model,
                   transport_mode, enabled, remark, revision
            FROM camera_devices
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadDevice(reader)
            : null;
    }

    private static async Task ReadChannelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<DeviceReadModel> devices,
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
                throw new InvalidDataException("数据库通道所属设备不存在。");
            }

            device.Channels.Add(new CameraChannelDto(
                ReadGuid(reader, 0, "camera_channels.id"),
                deviceId,
                reader.GetInt32(2),
                reader.GetString(3),
                ReadEnum<StreamType>(
                    reader.GetString(4),
                    "camera_channels.stream_type"),
                ReadBoolean(reader, 5, "camera_channels.enabled")));
        }
    }

    private static async Task ReadChannelsForDeviceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceReadModel device,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, device_id, channel_no, channel_name, stream_type, enabled
            FROM camera_channels
            WHERE device_id = $deviceId
            ORDER BY channel_no, stream_type, id;
            """;
        command.Parameters.AddWithValue("$deviceId", device.Id.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var deviceId = ReadGuid(reader, 1, "camera_channels.device_id");
            if (deviceId != device.Id)
            {
                throw new InvalidDataException("数据库通道所属设备不存在。");
            }

            device.Channels.Add(new CameraChannelDto(
                ReadGuid(reader, 0, "camera_channels.id"),
                deviceId,
                reader.GetInt32(2),
                reader.GetString(3),
                ReadEnum<StreamType>(
                    reader.GetString(4),
                    "camera_channels.stream_type"),
                ReadBoolean(reader, 5, "camera_channels.enabled")));
        }
    }

    private static DeviceReadModel ReadDevice(DbDataReader reader)
    {
        var deviceId = ReadGuid(reader, 0, "camera_devices.id");
        return new DeviceReadModel
        {
            Id = deviceId,
            GroupId = ReadGuid(reader, 1, "camera_devices.group_id"),
            Name = reader.GetString(2),
            IpAddress = reader.GetString(3),
            SdkPort = reader.GetInt32(4),
            RtspPort = reader.GetInt32(5),
            Username = reader.GetString(6),
            HasPassword = !string.IsNullOrEmpty(reader.GetString(7)),
            Manufacturer = reader.GetString(8),
            Model = reader.GetString(9),
            TransportMode = ReadEnum<TransportMode>(
                reader.GetString(10),
                "camera_devices.transport_mode"),
            Enabled = ReadBoolean(reader, 11, "camera_devices.enabled"),
            Remark = reader.GetString(12),
            Revision = reader.GetInt64(13)
        };
    }

    private static DeviceGroupDto ToGroupDto(DeviceGroup group, long revision) =>
        new(
            group.Id,
            group.Name,
            group.ParentId,
            group.Sort,
            group.Enabled,
            group.Kind,
            revision);

    private static CameraDeviceDto ToDeviceDto(
        DeviceReadModel device) =>
        ToDeviceDto(device, device.Revision, device.HasPassword);

    private static CameraDeviceDto ToDeviceDto(
        CameraDevice device,
        long revision,
        bool hasPassword) =>
        new(
            device.Id,
            device.GroupId,
            device.Name,
            device.IpAddress,
            device.SdkPort,
            device.RtspPort,
            device.Username,
            hasPassword,
            device.Manufacturer,
            device.Model,
            device.TransportMode,
            device.Enabled,
            device.Remark,
            revision,
            device.Channels.Select(channel => new CameraChannelDto(
                channel.Id,
                channel.DeviceId,
                channel.ChannelNo,
                channel.ChannelName,
                channel.StreamType,
                channel.Enabled)).ToArray());

    private static CameraDeviceDto ToDeviceDto(DeviceReadModel device, long revision, bool hasPassword) =>
        new(
            device.Id,
            device.GroupId,
            device.Name,
            device.IpAddress,
            device.SdkPort,
            device.RtspPort,
            device.Username,
            hasPassword,
            device.Manufacturer,
            device.Model,
            device.TransportMode,
            device.Enabled,
            device.Remark,
            revision,
            device.Channels.ToArray());

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

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original repository failure.
        }
    }

    private static Guid ReadGuid(
        DbDataReader reader,
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
        DbDataReader reader,
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
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            || !Enum.IsDefined(typeof(TEnum), result)
            || !string.Equals(result.ToString(), value, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"数据库字段 {fieldName} 的枚举值无效。");
        }

        return result;
    }

    private static TEnum? ReadNullableEnum<TEnum>(
        DbDataReader reader,
        int ordinal,
        string fieldName)
        where TEnum : struct, Enum
    {
        return reader.IsDBNull(ordinal)
            ? null
            : ReadEnum<TEnum>(reader.GetString(ordinal), fieldName);
    }

    private static int ToDatabaseBoolean(bool value) => value ? 1 : 0;

    private sealed record GroupState(
        long Revision,
        bool HasChild,
        bool HasDevice);

    private sealed class DeviceReadModel
    {
        public Guid Id { get; init; }
        public Guid GroupId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public int SdkPort { get; init; }
        public int RtspPort { get; init; }
        public string Username { get; init; } = string.Empty;
        public bool HasPassword { get; init; }
        public string Manufacturer { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public TransportMode TransportMode { get; init; }
        public bool Enabled { get; init; }
        public string Remark { get; init; } = string.Empty;
        public long Revision { get; init; }
        public List<CameraChannelDto> Channels { get; } = [];
    }
}
