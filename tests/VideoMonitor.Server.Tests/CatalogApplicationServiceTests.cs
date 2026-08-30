using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Tests;

public sealed class CatalogApplicationServiceTests
{
    [Fact]
    public void ApplicationServiceContract_Exists()
    {
        var assembly = typeof(Program).Assembly;

        Assert.NotNull(assembly.GetType("VideoMonitor.Server.Catalog.CatalogOperationResult`1"));
        Assert.NotNull(assembly.GetType("VideoMonitor.Server.Catalog.CatalogApplicationService"));
    }

    [Fact]
    public async Task GetCatalogAsync_MapsSuccessAndReadFailureWithoutLeakingException()
    {
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = SnapshotWithOneGroup()
        };
        var service = CreateService(fake);

        var success = await InvokeAsync(service, "GetCatalogAsync", CancellationToken.None);

        Assert.True(Get<bool>(success, "IsSuccess"));
        Assert.Equal(200, Get<int>(success, "StatusCode"));
        Assert.Null(GetProperty(success, "Error"));
        Assert.IsType<CatalogSnapshotDto>(GetProperty(success, "Value"));
        Assert.Equal(1, fake.GetCatalogCalls);

        fake.GetCatalogException = new InvalidOperationException(
            "TOP-SECRET-PASSWORD C:\\secret\\catalog.db");
        var failure = await InvokeAsync(service, "GetCatalogAsync", CancellationToken.None);

        Assert.False(Get<bool>(failure, "IsSuccess"));
        Assert.Equal(500, Get<int>(failure, "StatusCode"));
        AssertError(failure, "CATALOG_READ_FAILED", 500);
        Assert.DoesNotContain("TOP-SECRET-PASSWORD", GetErrorMessage(failure));
        Assert.DoesNotContain("catalog.db", GetErrorMessage(failure));
    }

    [Fact]
    public async Task GetGroupsAndDevices_ReadCatalogOnceAndFilterDevices()
    {
        var groupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        var first = DeviceDto(Guid.NewGuid(), groupId, "First");
        var second = DeviceDto(Guid.NewGuid(), otherGroupId, "Second");
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [new DeviceGroupDto(groupId, "Group", null, 0, true, 1)],
                [first, second])
        };
        var service = CreateService(fake);

        var groups = await InvokeAsync(service, "GetGroupsAsync", CancellationToken.None);
        var devices = await InvokeAsync(service, "GetDevicesAsync", groupId, CancellationToken.None);

        Assert.Equal(200, Get<int>(groups, "StatusCode"));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<DeviceGroupDto>>(
            GetProperty(groups, "Value")));
        Assert.Equal(200, Get<int>(devices, "StatusCode"));
        var filtered = Assert.IsAssignableFrom<IReadOnlyList<CameraDeviceDto>>(
            GetProperty(devices, "Value"));
        Assert.Single(filtered);
        Assert.Equal(first.Id, filtered[0].Id);
        Assert.Equal(2, fake.GetCatalogCalls);
    }

    [Fact]
    public async Task GetDevicesAsync_EmptyGroupIdIsValidationFailureWithoutRepositoryCall()
    {
        var fake = new FakeCentralCatalogRepository();
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "GetDevicesAsync",
            Guid.Empty,
            CancellationToken.None);

        AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.GetCatalogCalls);
    }

    [Fact]
    public async Task GetDeviceAsync_EmptyAndMissingIdsAreHandledSafely()
    {
        var fake = new FakeCentralCatalogRepository();
        var service = CreateService(fake);

        var invalid = await InvokeAsync(
            service,
            "GetDeviceAsync",
            Guid.Empty,
            CancellationToken.None);
        var missing = await InvokeAsync(
            service,
            "GetDeviceAsync",
            Guid.NewGuid(),
            CancellationToken.None);

        AssertError(invalid, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(missing, "DEVICE_NOT_FOUND", 404);
        Assert.Equal(1, fake.GetDeviceCalls);
    }

    [Fact]
    public async Task CreateGroupAsync_MapsParentLookupAndSuccess()
    {
        var parentId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [parentId] = new DeviceGroupDto(parentId, "Parent", null, 0, true, 1) }
        };
        var service = CreateService(fake);
        var request = new CreateGroupRequest(groupId, "Child", parentId, 2, true);

        var result = await InvokeAsync(
            service,
            "CreateGroupAsync",
            request,
            CancellationToken.None);

        Assert.True(Get<bool>(result, "IsSuccess"));
        Assert.Equal(201, Get<int>(result, "StatusCode"));
        Assert.Null(GetProperty(result, "Error"));
        Assert.Equal(groupId, fake.CapturedGroup!.Id);
        Assert.Equal(parentId, fake.CapturedGroup.ParentId);
        Assert.Equal("Child", fake.CapturedGroup.Name);
        Assert.Equal(1, fake.CreateGroupCalls);
    }

    [Fact]
    public async Task CreateGroupAsync_ValidationAndMissingParentDoNotWrite()
    {
        var fake = new FakeCentralCatalogRepository();
        var service = CreateService(fake);
        var invalid = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.Empty, " ", null, 0, true),
            CancellationToken.None);
        var missingParent = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Group", Guid.NewGuid(), 0, true),
            CancellationToken.None);

        AssertError(invalid, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(missingParent, "GROUP_NOT_FOUND", 404);
        Assert.Equal(0, fake.CreateGroupCalls);
    }

    [Fact]
    public async Task UpdateGroupAsync_RejectsParentCycleAndMapsConflict()
    {
        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(id, "A", null, 0, true, 1),
                    new DeviceGroupDto(parentId, "B", id, 0, true, 1)
                ],
                []),
            UpdateGroupResult = new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.RevisionConflict,
                CurrentRevision: 4)
        };
        var service = CreateService(fake);
        var cycle = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            id,
            new UpdateGroupRequest("A", parentId, 0, true, 1),
            CancellationToken.None);
        AssertError(cycle, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.UpdateGroupCalls);

        var conflict = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            parentId,
            new UpdateGroupRequest("B2", null, 0, true, 1),
            CancellationToken.None);

        AssertError(conflict, "GROUP_REVISION_CONFLICT", 409, 4);
        Assert.Equal(1, fake.UpdateGroupCalls);
    }

    [Fact]
    public async Task DeleteGroupAsync_MapsRepositoryStatuses()
    {
        var fake = new FakeCentralCatalogRepository
        {
            DeleteGroupResult = new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.GroupNotEmpty)
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "DeleteGroupAsync",
            Guid.NewGuid(),
            1L,
            CancellationToken.None);

        AssertError(result, "GROUP_NOT_EMPTY", 409);
        Assert.Equal(1, fake.DeleteGroupCalls);
    }

    [Fact]
    public async Task CreateDeviceAsync_AllowsEmptyPasswordAndMapsTrustedModel()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) },
            CreateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.Success,
                DeviceDto(Guid.NewGuid(), groupId, "Camera", hasPassword: false))
        };
        var service = CreateService(fake);
        var deviceId = Guid.NewGuid();
        var request = new CreateDeviceRequest(
            deviceId,
            groupId,
            "Camera",
            "192.0.2.10",
            8000,
            554,
            "user",
            string.Empty,
            "Hikvision",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            [new CameraChannelInput(Guid.NewGuid(), 1, "Main", StreamType.Main, true)]);

        var result = await InvokeAsync(service, "CreateDeviceAsync", request, CancellationToken.None);

        Assert.Equal(201, Get<int>(result, "StatusCode"));
        Assert.Equal(1, fake.CreateDeviceCalls);
        Assert.Equal(deviceId, fake.CapturedDevice!.Id);
        Assert.Equal(string.Empty, fake.CapturedDevice.Password);
        Assert.Equal(string.Empty, fake.CapturedDevice.Channels[0].StreamId);
    }

    [Fact]
    public async Task CreateDeviceAsync_RejectsInvalidInputAndDuplicateChannelIdentity()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) }
        };
        var service = CreateService(fake);
        var channelId = Guid.NewGuid();
        var request = new CreateDeviceRequest(
            Guid.NewGuid(), groupId, "Camera", "not-an-ip", 8000, 554,
            "user", "password", "Maker", "Model", TransportMode.Tcp, true, "",
            [
                new CameraChannelInput(channelId, 1, "Main", StreamType.Main, true),
                new CameraChannelInput(Guid.NewGuid(), 1, "Duplicate", StreamType.Main, true)
            ]);

        var invalid = await InvokeAsync(service, "CreateDeviceAsync", request, CancellationToken.None);
        AssertError(invalid, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.CreateDeviceCalls);

        var validWithConflict = request with
        {
            IpAddress = "192.0.2.11",
            Channels = [
                new CameraChannelInput(channelId, 1, "Main", StreamType.Main, true),
                new CameraChannelInput(Guid.NewGuid(), 1, "Duplicate", StreamType.Main, true)]
        };
        var conflict = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            validWithConflict,
            CancellationToken.None);
        AssertError(conflict, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.CreateDeviceCalls);
    }

    [Fact]
    public async Task UpdateDeviceAsync_ForwardsNullPasswordWithoutReadingIt()
    {
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) },
            UpdateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.Success,
                DeviceDto(deviceId, groupId, "Updated", hasPassword: true))
        };
        var service = CreateService(fake);
        var request = ValidUpdateDeviceRequest(groupId, expectedRevision: 3);

        var result = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            deviceId,
            request,
            CancellationToken.None);

        Assert.Equal(200, Get<int>(result, "StatusCode"));
        Assert.Null(fake.CapturedNewPassword);
        Assert.Equal(3, fake.CapturedExpectedRevision);
        Assert.Equal(1, fake.UpdateDeviceCalls);
        Assert.Equal(string.Empty, fake.CapturedDevice!.Password);
    }

    [Fact]
    public async Task UpdateDeviceAsync_RejectsEmptyPasswordBeforeRepositoryWrite()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) }
        };
        var service = CreateService(fake);
        var request = ValidUpdateDeviceRequest(groupId, expectedRevision: 1) with
        {
            NewPassword = string.Empty
        };

        var result = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            Guid.NewGuid(),
            request,
            CancellationToken.None);

        AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.UpdateDeviceCalls);
    }

    [Fact]
    public async Task UpdateDeviceAsync_ForwardsNonEmptyPasswordAndMapsConflict()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) },
            UpdateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.RevisionConflict,
                CurrentRevision: 8)
        };
        var service = CreateService(fake);
        var result = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            Guid.NewGuid(),
            ValidUpdateDeviceRequest(groupId, 7) with { NewPassword = "new-secret" },
            CancellationToken.None);

        AssertError(result, "DEVICE_REVISION_CONFLICT", 409, 8);
        Assert.Equal("new-secret", fake.CapturedNewPassword);
        Assert.Equal(1, fake.UpdateDeviceCalls);
    }

    [Fact]
    public async Task DeleteDeviceAsync_MapsNotFoundAndSuccess()
    {
        var fake = new FakeCentralCatalogRepository
        {
            DeleteDeviceResult = new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.NotFound)
        };
        var service = CreateService(fake);
        var missing = await InvokeAsync(
            service,
            "DeleteDeviceAsync",
            Guid.NewGuid(),
            1L,
            CancellationToken.None);

        AssertError(missing, "DEVICE_NOT_FOUND", 404);

        fake.DeleteDeviceResult = new CatalogRepositoryDeleteResult(
            CatalogRepositoryStatus.Success);
        var success = await InvokeAsync(
            service,
            "DeleteDeviceAsync",
            Guid.NewGuid(),
            1L,
            CancellationToken.None);

        Assert.True(Get<bool>(success, "IsSuccess"));
        Assert.Equal(204, Get<int>(success, "StatusCode"));
        Assert.Null(GetProperty(success, "Value"));
        Assert.Null(GetProperty(success, "Error"));
    }

    [Fact]
    public async Task MutationException_IsSanitizedAndCancellationPropagates()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) },
            CreateGroupException = new InvalidOperationException(
                "TOP-SECRET-PASSWORD C:\\secret\\catalog.db")
        };
        var service = CreateService(fake);
        var failure = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Group", null, 0, true),
            CancellationToken.None);

        AssertError(failure, "CATALOG_WRITE_FAILED", 500);
        Assert.DoesNotContain("TOP-SECRET-PASSWORD", GetErrorMessage(failure));
        Assert.DoesNotContain("catalog.db", GetErrorMessage(failure));

        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fake.GetCatalogException = new OperationCanceledException(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeAsync(
            service,
            "GetCatalogAsync",
            cancellation.Token));
    }

    private static object CreateService(FakeCentralCatalogRepository repository)
    {
        var serviceType = typeof(Program).Assembly.GetType(
            "VideoMonitor.Server.Catalog.CatalogApplicationService");
        Assert.NotNull(serviceType);
        var service = Activator.CreateInstance(serviceType!, repository);
        Assert.NotNull(service);
        return service!;
    }

    private static async Task<object?> InvokeAsync(
        object service,
        string methodName,
        params object?[] arguments)
    {
        var method = service.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        object? invocation;
        try
        {
            invocation = method!.Invoke(service, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static UpdateDeviceRequest ValidUpdateDeviceRequest(Guid groupId, long expectedRevision) =>
        new(
            groupId,
            "Updated Camera",
            "192.0.2.20",
            8000,
            554,
            "user",
            null,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            expectedRevision,
            [new CameraChannelInput(Guid.NewGuid(), 1, "Main", StreamType.Main, true)]);

    private static CatalogSnapshotDto SnapshotWithOneGroup()
    {
        var groupId = Guid.NewGuid();
        return new CatalogSnapshotDto(
            [new DeviceGroupDto(groupId, "Group", null, 0, true, 1)],
            []);
    }

    private static CameraDeviceDto DeviceDto(
        Guid id,
        Guid groupId,
        string name,
        bool hasPassword = false) =>
        new(
            id,
            groupId,
            name,
            "192.0.2.10",
            8000,
            554,
            "user",
            hasPassword,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            1,
            [new CameraChannelDto(Guid.NewGuid(), id, 1, "Main", StreamType.Main, true)]);

    private static void AssertError(
        object? result,
        string code,
        int statusCode,
        long? currentRevision = null)
    {
        Assert.NotNull(result);
        Assert.False(Get<bool>(result, "IsSuccess"));
        Assert.Equal(statusCode, Get<int>(result, "StatusCode"));
        var error = Assert.IsType<CatalogErrorDto>(GetProperty(result, "Error"));
        Assert.Equal(code, error.Code);
        Assert.Equal(currentRevision, error.CurrentRevision);
        Assert.Null(GetProperty(result, "Value"));
    }

    private static string GetErrorMessage(object? result) =>
        Assert.IsType<CatalogErrorDto>(GetProperty(result, "Error")).Message;

    private static T Get<T>(object? source, string propertyName)
    {
        var value = GetProperty(source, propertyName);
        Assert.NotNull(value);
        return (T)value!;
    }

    private static object? GetProperty(object? source, string propertyName)
    {
        Assert.NotNull(source);
        var property = source!.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return property!.GetValue(source);
    }

    private sealed class FakeCentralCatalogRepository : ICentralCatalogRepository
    {
        public CatalogSnapshotDto Snapshot { get; set; } = new([], []);
        public Dictionary<Guid, DeviceGroupDto> Groups { get; } = [];
        public Dictionary<Guid, CameraDeviceDto> Devices { get; } = [];
        public Exception? GetCatalogException { get; set; }
        public Exception? GetGroupException { get; set; }
        public Exception? GetDeviceException { get; set; }
        public Exception? CreateGroupException { get; set; }
        public Exception? CreateDeviceException { get; set; }
        public CatalogRepositoryResult<DeviceGroupDto>? CreateGroupResult { get; set; }
        public CatalogRepositoryResult<CameraDeviceDto>? CreateDeviceResult { get; set; }
        public CatalogRepositoryResult<DeviceGroupDto>? UpdateGroupResult { get; set; }
        public CatalogRepositoryDeleteResult? DeleteGroupResult { get; set; }
        public CatalogRepositoryResult<CameraDeviceDto>? UpdateDeviceResult { get; set; }
        public CatalogRepositoryDeleteResult? DeleteDeviceResult { get; set; }
        public int GetCatalogCalls { get; private set; }
        public int GetGroupCalls { get; private set; }
        public int GetDeviceCalls { get; private set; }
        public int CreateGroupCalls { get; private set; }
        public int CreateDeviceCalls { get; private set; }
        public int UpdateGroupCalls { get; private set; }
        public int DeleteGroupCalls { get; private set; }
        public int UpdateDeviceCalls { get; private set; }
        public int DeleteDeviceCalls { get; private set; }
        public DeviceGroup? CapturedGroup { get; private set; }
        public CameraDevice? CapturedDevice { get; private set; }
        public string? CapturedNewPassword { get; private set; }
        public long CapturedExpectedRevision { get; private set; }

        public Task<CatalogSnapshotDto> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            GetCatalogCalls++;
            ThrowIfConfigured(GetCatalogException, cancellationToken);
            return Task.FromResult(Snapshot);
        }

        public Task<DeviceGroupDto?> GetGroupAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetGroupCalls++;
            ThrowIfConfigured(GetGroupException, cancellationToken);
            Groups.TryGetValue(id, out var group);
            return Task.FromResult(group);
        }

        public Task<CameraDeviceDto?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetDeviceCalls++;
            ThrowIfConfigured(GetDeviceException, cancellationToken);
            Devices.TryGetValue(id, out var device);
            return Task.FromResult(device);
        }

        public Task<CatalogRepositoryResult<DeviceGroupDto>> CreateGroupAsync(
            DeviceGroup group,
            CancellationToken cancellationToken = default)
        {
            CreateGroupCalls++;
            CapturedGroup = group;
            ThrowIfConfigured(CreateGroupException, cancellationToken);
            return Task.FromResult(CreateGroupResult ?? new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.Success,
                new DeviceGroupDto(group.Id, group.Name, group.ParentId, group.Sort, group.Enabled, 1)));
        }

        public Task<CatalogRepositoryResult<CameraDeviceDto>> CreateDeviceAsync(
            CameraDevice device,
            CancellationToken cancellationToken = default)
        {
            CreateDeviceCalls++;
            CapturedDevice = device;
            ThrowIfConfigured(CreateDeviceException, cancellationToken);
            return Task.FromResult(CreateDeviceResult ?? new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.Success,
                DeviceDto(device.Id, device.GroupId, device.Name, !string.IsNullOrEmpty(device.Password))));
        }

        public Task<CatalogRepositoryResult<DeviceGroupDto>> UpdateGroupAsync(
            DeviceGroup group,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            UpdateGroupCalls++;
            CapturedGroup = group;
            CapturedExpectedRevision = expectedRevision;
            return Task.FromResult(UpdateGroupResult ?? new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.Success,
                new DeviceGroupDto(group.Id, group.Name, group.ParentId, group.Sort, group.Enabled,
                    expectedRevision + 1)));
        }

        public Task<CatalogRepositoryDeleteResult> DeleteGroupAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            DeleteGroupCalls++;
            CapturedExpectedRevision = expectedRevision;
            return Task.FromResult(DeleteGroupResult ?? new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.Success));
        }

        public Task<CatalogRepositoryResult<CameraDeviceDto>> UpdateDeviceAsync(
            CameraDevice device,
            string? newPassword,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            UpdateDeviceCalls++;
            CapturedDevice = device;
            CapturedNewPassword = newPassword;
            CapturedExpectedRevision = expectedRevision;
            return Task.FromResult(UpdateDeviceResult ?? new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.Success,
                DeviceDto(device.Id, device.GroupId, device.Name,
                    newPassword is not null && newPassword.Length > 0)));
        }

        public Task<CatalogRepositoryDeleteResult> DeleteDeviceAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            DeleteDeviceCalls++;
            CapturedExpectedRevision = expectedRevision;
            return Task.FromResult(DeleteDeviceResult ?? new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.Success));
        }

        private static void ThrowIfConfigured(Exception? exception, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
