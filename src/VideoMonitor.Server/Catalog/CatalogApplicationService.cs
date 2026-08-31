using System.Net;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Catalog;

public sealed class CatalogApplicationService
{
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string DeviceNotFoundCode = "DEVICE_NOT_FOUND";
    private const string GroupNotFoundCode = "GROUP_NOT_FOUND";
    private const string DeviceRevisionConflictCode = "DEVICE_REVISION_CONFLICT";
    private const string GroupRevisionConflictCode = "GROUP_REVISION_CONFLICT";
    private const string GroupNotEmptyCode = "GROUP_NOT_EMPTY";
    private const string ChannelConflictCode = "CHANNEL_CONFLICT";
    private const string ReadFailedCode = "CATALOG_READ_FAILED";
    private const string WriteFailedCode = "CATALOG_WRITE_FAILED";

    private readonly ICentralCatalogRepository repository;

    public CatalogApplicationService(ICentralCatalogRepository repository)
    {
        this.repository = repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CatalogOperationResult<CatalogSnapshotDto>> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Success(
                await repository.GetCatalogAsync(cancellationToken).ConfigureAwait(false),
                StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReadFailure<CatalogSnapshotDto>();
        }
    }

    public async Task<CatalogOperationResult<IReadOnlyList<DeviceGroupDto>>> GetGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await repository.GetCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            return Success(snapshot.Groups, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReadFailure<IReadOnlyList<DeviceGroupDto>>();
        }
    }

    public async Task<CatalogOperationResult<IReadOnlyList<CameraDeviceDto>>> GetDevicesAsync(
        Guid? groupId = null,
        CancellationToken cancellationToken = default)
    {
        if (groupId == Guid.Empty)
        {
            return ValidationFailure<IReadOnlyList<CameraDeviceDto>>();
        }

        try
        {
            var snapshot = await repository.GetCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            var devices = groupId is null
                ? snapshot.Devices
                : snapshot.Devices
                    .Where(device => device.GroupId == groupId.Value)
                    .ToArray();
            return Success(devices, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReadFailure<IReadOnlyList<CameraDeviceDto>>();
        }
    }

    public async Task<CatalogOperationResult<CameraDeviceDto>> GetDeviceAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return ValidationFailure<CameraDeviceDto>();
        }

        try
        {
            var device = await repository.GetDeviceAsync(id, cancellationToken)
                .ConfigureAwait(false);
            return device is null
                ? Failure<CameraDeviceDto>(
                    StatusCodes.Status404NotFound,
                    DeviceNotFoundCode,
                    "Device was not found.")
                : Success(device, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReadFailure<CameraDeviceDto>();
        }
    }

    public async Task<CatalogOperationResult<DeviceGroupDto>> CreateGroupAsync(
        CreateGroupRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateCreateGroup(request, out var validationError))
        {
            return validationError!;
        }

        try
        {
            var validRequest = request!;
            var parentValidation = await ValidateCreateGroupParentAsync(
                    validRequest,
                    cancellationToken)
                .ConfigureAwait(false);
            if (parentValidation is not null)
            {
                return parentValidation;
            }

            var group = new DeviceGroup
            {
                Id = validRequest.Id,
                Name = validRequest.Name,
                ParentId = validRequest.ParentId,
                Sort = validRequest.Sort,
                Enabled = validRequest.Enabled,
                Kind = validRequest.Kind
            };
            var result = await repository.CreateGroupAsync(group, cancellationToken)
                .ConfigureAwait(false);
            return MapGroupResult(result, StatusCodes.Status201Created);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteFailure<DeviceGroupDto>();
        }
    }

    public async Task<CatalogOperationResult<DeviceGroupDto>> UpdateGroupAsync(
        Guid id,
        UpdateGroupRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateUpdateGroup(id, request, out var validationError))
        {
            return validationError!;
        }

        try
        {
            var validRequest = request!;
            var snapshot = await repository.GetCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            var groupsById = snapshot.Groups.ToDictionary(group => group.Id);
            if (!groupsById.TryGetValue(id, out var current))
            {
                return Failure<DeviceGroupDto>(
                    StatusCodes.Status404NotFound,
                    GroupNotFoundCode,
                    "Group was not found.");
            }

            if (!ValidateGroupUpdate(current, validRequest, groupsById, out var groupError))
            {
                return groupError!;
            }

            var group = new DeviceGroup
            {
                Id = id,
                Name = validRequest.Name,
                ParentId = validRequest.ParentId,
                Sort = validRequest.Sort,
                Enabled = validRequest.Enabled,
                Kind = validRequest.Kind,
                Revision = validRequest.ExpectedRevision
            };
            var result = await repository.UpdateGroupAsync(
                    group,
                    validRequest.ExpectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
            return MapGroupResult(result, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteFailure<DeviceGroupDto>();
        }
    }

    public async Task<CatalogOperationResult<object?>> DeleteGroupAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || expectedRevision <= 0)
        {
            return ValidationFailure<object?>();
        }

        try
        {
            var result = await repository.DeleteGroupAsync(
                    id,
                    expectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
            return MapDeleteGroupResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteFailure<object?>();
        }
    }

    public async Task<CatalogOperationResult<CameraDeviceDto>> CreateDeviceAsync(
        CreateDeviceRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateCreateDevice(request, out var validationError))
        {
            return validationError!;
        }

        try
        {
            var targetValidation = await ValidateDeviceTargetAsync<CameraDeviceDto>(
                    request!.GroupId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (targetValidation is not null)
            {
                return targetValidation;
            }

            var device = ToDevice(request);
            var result = await repository.CreateDeviceAsync(device, cancellationToken)
                .ConfigureAwait(false);
            return MapDeviceResult(result, StatusCodes.Status201Created);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteFailure<CameraDeviceDto>();
        }
    }

    public async Task<CatalogOperationResult<CameraDeviceDto>> UpdateDeviceAsync(
        Guid id,
        UpdateDeviceRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateUpdateDevice(id, request, out var validationError))
        {
            return validationError!;
        }

        try
        {
            var targetValidation = await ValidateDeviceTargetAsync<CameraDeviceDto>(
                    request!.GroupId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (targetValidation is not null)
            {
                return targetValidation;
            }

            var device = ToDevice(request, id);
            var result = await repository.UpdateDeviceAsync(
                    device,
                    request.NewPassword,
                    request.ExpectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
            return MapDeviceResult(result, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteFailure<CameraDeviceDto>();
        }
    }

    public async Task<CatalogOperationResult<object?>> DeleteDeviceAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || expectedRevision <= 0)
        {
            return ValidationFailure<object?>();
        }

        try
        {
            var result = await repository.DeleteDeviceAsync(
                    id,
                    expectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
            return MapDeleteDeviceResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WriteFailure<object?>();
        }
    }

    private static bool TryValidateCreateGroup(
        CreateGroupRequest? request,
        out CatalogOperationResult<DeviceGroupDto>? error)
    {
        if (request is null
            || request.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Name)
            || request.ParentId == Guid.Empty
            || request.ParentId == request.Id)
        {
            error = ValidationFailure<DeviceGroupDto>();
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateUpdateGroup(
        Guid id,
        UpdateGroupRequest? request,
        out CatalogOperationResult<DeviceGroupDto>? error)
    {
        if (request is null
            || id == Guid.Empty
            || request.ExpectedRevision <= 0
            || string.IsNullOrWhiteSpace(request.Name)
            || request.ParentId == Guid.Empty
            || request.ParentId == id)
        {
            error = ValidationFailure<DeviceGroupDto>();
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateCreateDevice(
        CreateDeviceRequest? request,
        out CatalogOperationResult<CameraDeviceDto>? error)
    {
        if (request is null
            || request.Id == Guid.Empty
            || request.GroupId == Guid.Empty
            || request.Password is null
            || !ValidateDeviceFields(
                request.Name,
                request.IpAddress,
                request.SdkPort,
                request.RtspPort,
                request.Username,
                request.Manufacturer,
                request.Model,
                request.Remark,
                request.TransportMode))
        {
            error = ValidationFailure<CameraDeviceDto>();
            return false;
        }

        if (!ValidateChannels(request.Channels, out var channelError))
        {
            error = channelError ?? ValidationFailure<CameraDeviceDto>();
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateUpdateDevice(
        Guid id,
        UpdateDeviceRequest? request,
        out CatalogOperationResult<CameraDeviceDto>? error)
    {
        if (request is null
            || id == Guid.Empty
            || request.GroupId == Guid.Empty
            || request.ExpectedRevision <= 0
            || request.NewPassword is not null && request.NewPassword.Length == 0
            || !ValidateDeviceFields(
                request.Name,
                request.IpAddress,
                request.SdkPort,
                request.RtspPort,
                request.Username,
                request.Manufacturer,
                request.Model,
                request.Remark,
                request.TransportMode))
        {
            error = ValidationFailure<CameraDeviceDto>();
            return false;
        }

        if (!ValidateChannels(request.Channels, out var channelError))
        {
            error = channelError ?? ValidationFailure<CameraDeviceDto>();
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidateDeviceFields(
        string? name,
        string? ipAddress,
        int sdkPort,
        int rtspPort,
        string? username,
        string? manufacturer,
        string? model,
        string? remark,
        TransportMode transportMode)
    {
        return !string.IsNullOrWhiteSpace(name)
            && !string.IsNullOrWhiteSpace(ipAddress)
            && IPAddress.TryParse(ipAddress, out _)
            && sdkPort is >= 1 and <= 65535
            && rtspPort is >= 1 and <= 65535
            && username is not null
            && manufacturer is not null
            && model is not null
            && remark is not null
            && Enum.IsDefined(transportMode);
    }

    private async Task<CatalogOperationResult<DeviceGroupDto>?> ValidateCreateGroupParentAsync(
        CreateGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ParentId is null)
        {
            return IsValidGroupKind(request.Kind)
                ? null
                : ValidationFailure<DeviceGroupDto>();
        }

        var parent = await repository.GetGroupAsync(
                request.ParentId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (parent is null)
        {
            return Failure<DeviceGroupDto>(
                StatusCodes.Status404NotFound,
                GroupNotFoundCode,
                "Group was not found.");
        }

        return IsFormalRoot(parent) && request.Kind is null
            ? null
            : ValidationFailure<DeviceGroupDto>();
    }

    private static bool ValidateGroupUpdate(
        DeviceGroupDto current,
        UpdateGroupRequest request,
        IReadOnlyDictionary<Guid, DeviceGroupDto> groupsById,
        out CatalogOperationResult<DeviceGroupDto>? error)
    {
        if (current.ParentId is null)
        {
            if (request.ParentId is not null
                || !IsValidGroupKind(request.Kind)
                || current.Kind is not null && current.Kind != request.Kind)
            {
                error = ValidationFailure<DeviceGroupDto>();
                return false;
            }
        }
        else
        {
            if (request.ParentId is not Guid parentId)
            {
                error = ValidationFailure<DeviceGroupDto>();
                return false;
            }

            if (!groupsById.TryGetValue(parentId, out var parent))
            {
                error = Failure<DeviceGroupDto>(
                    StatusCodes.Status404NotFound,
                    GroupNotFoundCode,
                    "Group was not found.");
                return false;
            }

            if (!IsFormalRoot(parent)
                || request.Kind is not null
                || CreatesParentCycle(groupsById, current.Id, parentId))
            {
                error = ValidationFailure<DeviceGroupDto>();
                return false;
            }
        }

        error = null;
        return true;
    }

    private async Task<CatalogOperationResult<T>?> ValidateDeviceTargetAsync<T>(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var group = await repository.GetGroupAsync(groupId, cancellationToken)
            .ConfigureAwait(false);
        if (group is null)
        {
            return Failure<T>(
                StatusCodes.Status404NotFound,
                GroupNotFoundCode,
                "Group was not found.");
        }

        if (group.ParentId is not Guid parentId || group.Kind is not null)
        {
            return ValidationFailure<T>();
        }

        var parent = await repository.GetGroupAsync(parentId, cancellationToken)
            .ConfigureAwait(false);
        return parent is not null && IsFormalRoot(parent)
            ? null
            : ValidationFailure<T>();
    }

    private static bool IsFormalRoot(DeviceGroupDto group) =>
        group.ParentId is null && IsValidGroupKind(group.Kind);

    private static bool IsValidGroupKind(MonitorGroupType? kind) =>
        kind is MonitorGroupType value
        && Enum.IsDefined(typeof(MonitorGroupType), value);

    private static bool ValidateChannels(
        IReadOnlyList<CameraChannelInput>? channels,
        out CatalogOperationResult<CameraDeviceDto>? error)
    {
        if (channels is null)
        {
            error = ValidationFailure<CameraDeviceDto>();
            return false;
        }

        var channelIds = new HashSet<Guid>();
        var identities = new HashSet<(int ChannelNo, StreamType StreamType)>();
        foreach (var channel in channels)
        {
            if (channel is null
                || channel.Id == Guid.Empty
                || channel.ChannelNo <= 0
                || channel.ChannelName is null
                || !Enum.IsDefined(channel.StreamType))
            {
                error = ValidationFailure<CameraDeviceDto>();
                return false;
            }

            if (!channelIds.Add(channel.Id))
            {
                error = ValidationFailure<CameraDeviceDto>();
                return false;
            }

            if (!identities.Add((channel.ChannelNo, channel.StreamType)))
            {
                error = Failure<CameraDeviceDto>(
                    StatusCodes.Status409Conflict,
                    ChannelConflictCode,
                    "Channel configuration conflicts.");
                return false;
            }
        }

        error = null;
        return true;
    }

    private static CameraDevice ToDevice(CreateDeviceRequest request)
    {
        var device = new CameraDevice
        {
            Id = request.Id,
            GroupId = request.GroupId,
            Name = request.Name,
            IpAddress = request.IpAddress,
            SdkPort = request.SdkPort,
            RtspPort = request.RtspPort,
            Username = request.Username,
            Password = request.Password,
            Manufacturer = request.Manufacturer,
            Model = request.Model,
            TransportMode = request.TransportMode,
            Enabled = request.Enabled,
            Remark = request.Remark
        };
        AddChannels(device, request.Channels);
        return device;
    }

    private static CameraDevice ToDevice(UpdateDeviceRequest request, Guid id)
    {
        var device = new CameraDevice
        {
            Id = id,
            GroupId = request.GroupId,
            Name = request.Name,
            IpAddress = request.IpAddress,
            SdkPort = request.SdkPort,
            RtspPort = request.RtspPort,
            Username = request.Username,
            Password = string.Empty,
            Manufacturer = request.Manufacturer,
            Model = request.Model,
            TransportMode = request.TransportMode,
            Enabled = request.Enabled,
            Remark = request.Remark,
            Revision = request.ExpectedRevision
        };
        AddChannels(device, request.Channels);
        return device;
    }

    private static void AddChannels(
        CameraDevice device,
        IReadOnlyList<CameraChannelInput> channels)
    {
        foreach (var channel in channels)
        {
            device.Channels.Add(new CameraChannel
            {
                Id = channel.Id,
                DeviceId = device.Id,
                ChannelNo = channel.ChannelNo,
                ChannelName = channel.ChannelName,
                StreamType = channel.StreamType,
                Enabled = channel.Enabled
            });
        }
    }

    private static bool CreatesParentCycle(
        IReadOnlyDictionary<Guid, DeviceGroupDto> groupsById,
        Guid groupId,
        Guid proposedParentId)
    {
        var visited = new HashSet<Guid>();
        var current = proposedParentId;
        while (true)
        {
            if (current == groupId || !visited.Add(current))
            {
                return true;
            }

            if (!groupsById.TryGetValue(current, out var group)
                || group.ParentId is not Guid parentId)
            {
                return false;
            }

            current = parentId;
        }
    }

    private static CatalogOperationResult<DeviceGroupDto> MapGroupResult(
        CatalogRepositoryResult<DeviceGroupDto> result,
        int successStatusCode) =>
        result.Status switch
        {
            CatalogRepositoryStatus.Success => Success(result.Value!, successStatusCode),
            CatalogRepositoryStatus.NotFound => Failure<DeviceGroupDto>(
                StatusCodes.Status404NotFound,
                GroupNotFoundCode,
                "Group was not found."),
            CatalogRepositoryStatus.RevisionConflict => Failure<DeviceGroupDto>(
                StatusCodes.Status409Conflict,
                GroupRevisionConflictCode,
                "Group revision conflict.",
                result.CurrentRevision),
            CatalogRepositoryStatus.GroupNotEmpty => Failure<DeviceGroupDto>(
                StatusCodes.Status409Conflict,
                GroupNotEmptyCode,
                "Group is not empty."),
            _ => WriteFailure<DeviceGroupDto>()
        };

    private static CatalogOperationResult<CameraDeviceDto> MapDeviceResult(
        CatalogRepositoryResult<CameraDeviceDto> result,
        int successStatusCode) =>
        result.Status switch
        {
            CatalogRepositoryStatus.Success => Success(result.Value!, successStatusCode),
            CatalogRepositoryStatus.NotFound => Failure<CameraDeviceDto>(
                StatusCodes.Status404NotFound,
                DeviceNotFoundCode,
                "Device was not found."),
            CatalogRepositoryStatus.RevisionConflict => Failure<CameraDeviceDto>(
                StatusCodes.Status409Conflict,
                DeviceRevisionConflictCode,
                "Device revision conflict.",
                result.CurrentRevision),
            CatalogRepositoryStatus.ChannelConflict => Failure<CameraDeviceDto>(
                StatusCodes.Status409Conflict,
                ChannelConflictCode,
                "Channel configuration conflicts."),
            _ => WriteFailure<CameraDeviceDto>()
        };

    private static CatalogOperationResult<object?> MapDeleteGroupResult(
        CatalogRepositoryDeleteResult result) =>
        result.Status switch
        {
            CatalogRepositoryStatus.Success => Success<object?>(null, StatusCodes.Status204NoContent),
            CatalogRepositoryStatus.NotFound => Failure<object?>(
                StatusCodes.Status404NotFound,
                GroupNotFoundCode,
                "Group was not found."),
            CatalogRepositoryStatus.RevisionConflict => Failure<object?>(
                StatusCodes.Status409Conflict,
                GroupRevisionConflictCode,
                "Group revision conflict.",
                result.CurrentRevision),
            CatalogRepositoryStatus.GroupNotEmpty => Failure<object?>(
                StatusCodes.Status409Conflict,
                GroupNotEmptyCode,
                "Group is not empty."),
            _ => WriteFailure<object?>()
        };

    private static CatalogOperationResult<object?> MapDeleteDeviceResult(
        CatalogRepositoryDeleteResult result) =>
        result.Status switch
        {
            CatalogRepositoryStatus.Success => Success<object?>(null, StatusCodes.Status204NoContent),
            CatalogRepositoryStatus.NotFound => Failure<object?>(
                StatusCodes.Status404NotFound,
                DeviceNotFoundCode,
                "Device was not found."),
            CatalogRepositoryStatus.RevisionConflict => Failure<object?>(
                StatusCodes.Status409Conflict,
                DeviceRevisionConflictCode,
                "Device revision conflict.",
                result.CurrentRevision),
            _ => WriteFailure<object?>()
        };

    private static CatalogOperationResult<T> Success<T>(T value, int statusCode) =>
        new(true, value, statusCode, null);

    private static CatalogOperationResult<T> ValidationFailure<T>() =>
        Failure<T>(
            StatusCodes.Status400BadRequest,
            ValidationCode,
            "Catalog request validation failed.");

    private static CatalogOperationResult<T> ReadFailure<T>() =>
        Failure<T>(
            StatusCodes.Status500InternalServerError,
            ReadFailedCode,
            "Catalog read failed.");

    private static CatalogOperationResult<T> WriteFailure<T>() =>
        Failure<T>(
            StatusCodes.Status500InternalServerError,
            WriteFailedCode,
            "Catalog write failed.");

    private static CatalogOperationResult<T> Failure<T>(
        int statusCode,
        string code,
        string message,
        long? currentRevision = null) =>
        new(
            false,
            default,
            statusCode,
            new CatalogErrorDto(code, message, currentRevision));
}
