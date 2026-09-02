using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Catalog;

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
    public async Task ReadFailure_LogsOperationAndExceptionTypeWithoutExceptionDetails()
    {
        var fake = new FakeCentralCatalogRepository
        {
            GetCatalogException = new InvalidOperationException(
                "TOP-SECRET-PASSWORD C:\\secret\\catalog.db")
        };
        var logger = new RecordingLogger();
        var service = CreateService(fake, logger);

        await InvokeAsync(service, "GetCatalogAsync", CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("Catalog operation failed safely", message);
        Assert.Contains("GetCatalogAsync", message);
        Assert.Contains(nameof(InvalidOperationException), message);
        Assert.DoesNotContain("TOP-SECRET-PASSWORD", message);
        Assert.DoesNotContain("catalog.db", message);
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
            Groups = { [parentId] = new DeviceGroupDto(parentId, "Parent", null, 0, true, MonitorGroupType.Chute, 1) }
        };
        var service = CreateService(fake);
        var request = new CreateGroupRequest(groupId, "Child", parentId, 2, true, null);

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
    public async Task CreateGroupAsync_RequiresKindForRootAndRejectsInvalidKind()
    {
        var fake = new FakeCentralCatalogRepository();
        var service = CreateService(fake);

        var validRoot = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(
                Guid.NewGuid(),
                "Chute Root",
                null,
                0,
                true,
                MonitorGroupType.Chute),
            CancellationToken.None);
        var missingKind = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Unclassified Root", null, 0, true, null),
            CancellationToken.None);
        var invalidKind = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(
                Guid.NewGuid(),
                "Invalid Root",
                null,
                0,
                true,
                (MonitorGroupType)999),
            CancellationToken.None);

        Assert.True(Get<bool>(validRoot, "IsSuccess"));
        Assert.Equal(MonitorGroupType.Chute, fake.CapturedGroup!.Kind);
        AssertError(missingKind, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(invalidKind, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(1, fake.CreateGroupCalls);
    }

    [Theory]
    [InlineData(MonitorGroupType.UnloadingStation)]
    [InlineData(MonitorGroupType.Chute)]
    [InlineData(MonitorGroupType.Tunnel)]
    public async Task CreateGroupAsync_AcceptsEachDefinedRootKind(MonitorGroupType kind)
    {
        var fake = new FakeCentralCatalogRepository();
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Root", null, 0, true, kind),
            CancellationToken.None);

        Assert.True(Get<bool>(result, "IsSuccess"));
        Assert.Equal(kind, fake.CapturedGroup!.Kind);
    }

    [Fact]
    public async Task CreateGroupAsync_RequiresDirectRootParentAndNullChildKind()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups =
            {
                [rootId] = new DeviceGroupDto(rootId, "Root", null, 0, true, MonitorGroupType.Chute, 1),
                [childId] = new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)
            }
        };
        var service = CreateService(fake);

        var validChild = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Child B", rootId, 0, true, null),
            CancellationToken.None);
        var childWithKind = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(
                Guid.NewGuid(),
                "Child With Kind",
                rootId,
                0,
                true,
                MonitorGroupType.Chute),
            CancellationToken.None);
        var nestedChild = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Nested Child", childId, 0, true, null),
            CancellationToken.None);
        var missingParent = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(Guid.NewGuid(), "Missing Parent", Guid.NewGuid(), 0, true, null),
            CancellationToken.None);
        var missingParentWithKind = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(
                Guid.NewGuid(),
                "Missing Parent With Kind",
                Guid.NewGuid(),
                0,
                true,
                MonitorGroupType.Chute),
            CancellationToken.None);

        Assert.True(Get<bool>(validChild, "IsSuccess"));
        Assert.Null(fake.CapturedGroup!.Kind);
        Assert.Equal(rootId, fake.CapturedGroup.ParentId);
        AssertError(childWithKind, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(nestedChild, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(missingParent, "GROUP_NOT_FOUND", 404);
        AssertError(missingParentWithKind, "GROUP_NOT_FOUND", 404);
        Assert.Equal(1, fake.CreateGroupCalls);
    }

    [Fact]
    public async Task CreateGroupAsync_UnclassifiedLegacyRootCannotAcceptNewChild()
    {
        var legacyRootId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups =
            {
                [legacyRootId] = new DeviceGroupDto(
                    legacyRootId,
                    "Legacy Root",
                    null,
                    0,
                    true,
                    null,
                    1)
            }
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(
                Guid.NewGuid(),
                "Child",
                legacyRootId,
                0,
                true,
                null),
            CancellationToken.None);

        AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.CreateGroupCalls);
    }

    [Fact]
    public async Task UpdateGroupAsync_RejectsRootChildConversionAndInvalidRootKindChanges()
    {
        var rootId = Guid.NewGuid();
        var anotherRootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(rootId, "Root", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(anotherRootId, "Another Root", null, 0, true, MonitorGroupType.Tunnel, 1),
                    new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)
                ],
                [])
        };
        var service = CreateService(fake);

        var rootToChild = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            rootId,
            new UpdateGroupRequest("Root", anotherRootId, 0, true, MonitorGroupType.Chute, 1),
            CancellationToken.None);
        var childToRoot = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            childId,
            new UpdateGroupRequest("Child", null, 0, true, MonitorGroupType.Chute, 1),
            CancellationToken.None);
        var kindMutation = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            rootId,
            new UpdateGroupRequest("Root", null, 0, true, MonitorGroupType.Tunnel, 1),
            CancellationToken.None);
        var childWithKind = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            childId,
            new UpdateGroupRequest("Child", rootId, 0, true, MonitorGroupType.Chute, 1),
            CancellationToken.None);
        var invalidKind = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            rootId,
            new UpdateGroupRequest("Root", null, 0, true, (MonitorGroupType)999, 1),
            CancellationToken.None);

        AssertError(rootToChild, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(childToRoot, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(kindMutation, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(childWithKind, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(invalidKind, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.UpdateGroupCalls);
    }

    [Fact]
    public async Task UpdateGroupAsync_AllowsChildMoveBetweenRoots()
    {
        var rootAId = Guid.NewGuid();
        var rootBId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(rootAId, "Root A", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(rootBId, "Root B", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(childId, "Child", rootAId, 0, true, null, 1)
                ],
                [])
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            childId,
            new UpdateGroupRequest("Moved Child", rootBId, 3, true, null, 1),
            CancellationToken.None);

        Assert.True(Get<bool>(result, "IsSuccess"));
        Assert.Equal(rootBId, fake.CapturedGroup!.ParentId);
        Assert.Null(fake.CapturedGroup.Kind);
    }

    [Fact]
    public async Task UpdateGroupAsync_ChildCannotMoveToUnclassifiedLegacyRoot()
    {
        var formalRootId = Guid.NewGuid();
        var legacyRootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(
                        formalRootId,
                        "Formal Root",
                        null,
                        0,
                        true,
                        MonitorGroupType.Chute,
                        1),
                    new DeviceGroupDto(
                        legacyRootId,
                        "Legacy Root",
                        null,
                        0,
                        true,
                        null,
                        1),
                    new DeviceGroupDto(
                        childId,
                        "Child",
                        formalRootId,
                        0,
                        true,
                        null,
                        1)
                ],
                [])
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            childId,
            new UpdateGroupRequest(
                "Child",
                legacyRootId,
                0,
                true,
                null,
                1),
            CancellationToken.None);

        AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.UpdateGroupCalls);
    }

    [Fact]
    public async Task UpdateGroupAsync_MissingChildParentReturnsNotFound()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [new DeviceGroupDto(rootId, "Root", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)],
                [])
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            childId,
            new UpdateGroupRequest(
                "Child",
                Guid.NewGuid(),
                0,
                true,
                MonitorGroupType.Chute,
                1),
            CancellationToken.None);

        AssertError(result, "GROUP_NOT_FOUND", 404);
        Assert.Equal(0, fake.UpdateGroupCalls);
    }

    [Fact]
    public async Task UpdateGroupAsync_AllowsSameKindAndOneTimeLegacyRootRepair()
    {
        var assignedRootId = Guid.NewGuid();
        var legacyRootId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(assignedRootId, "Assigned Root", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(legacyRootId, "Legacy Root", null, 0, true, null, 1)
                ],
                [])
        };
        var service = CreateService(fake);

        var sameKind = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            assignedRootId,
            new UpdateGroupRequest("Assigned Root", null, 0, true, MonitorGroupType.Chute, 1),
            CancellationToken.None);
        var repair = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            legacyRootId,
            new UpdateGroupRequest("Legacy Root", null, 0, true, MonitorGroupType.Tunnel, 1),
            CancellationToken.None);
        var stillUnclassified = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            legacyRootId,
            new UpdateGroupRequest("Legacy Root", null, 0, true, null, 1),
            CancellationToken.None);

        Assert.True(Get<bool>(sameKind, "IsSuccess"));
        Assert.True(Get<bool>(repair, "IsSuccess"));
        Assert.Equal(MonitorGroupType.Tunnel, fake.CapturedGroup!.Kind);
        AssertError(stillUnclassified, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(2, fake.UpdateGroupCalls);
    }

    [Fact]
    public async Task CreateAndUpdateDeviceAsync_RequireAValidBusinessChildTarget()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var nestedChildId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups =
            {
                [rootId] = new DeviceGroupDto(rootId, "Root", null, 0, true, MonitorGroupType.Chute, 1),
                [childId] = new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1),
                [nestedChildId] = new DeviceGroupDto(nestedChildId, "Nested Child", childId, 0, true, null, 1)
            }
        };
        var service = CreateService(fake);

        var againstRoot = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            ValidCreateDeviceRequest(rootId),
            CancellationToken.None);
        var againstNestedChild = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            ValidCreateDeviceRequest(nestedChildId),
            CancellationToken.None);
        var validCreate = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            ValidCreateDeviceRequest(childId),
            CancellationToken.None);
        var againstRootUpdate = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            Guid.NewGuid(),
            ValidUpdateDeviceRequest(rootId, 1),
            CancellationToken.None);
        var validUpdate = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            Guid.NewGuid(),
            ValidUpdateDeviceRequest(childId, 1),
            CancellationToken.None);

        AssertError(againstRoot, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(againstNestedChild, "CATALOG_VALIDATION_FAILED", 400);
        Assert.True(Get<bool>(validCreate, "IsSuccess"));
        AssertError(againstRootUpdate, "CATALOG_VALIDATION_FAILED", 400);
        Assert.True(Get<bool>(validUpdate, "IsSuccess"));
        Assert.Equal(1, fake.CreateDeviceCalls);
        Assert.Equal(1, fake.UpdateDeviceCalls);
    }

    [Fact]
    public async Task CreateAndUpdateDeviceAsync_UnclassifiedRootHierarchyIsNotWritable()
    {
        var legacyRootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups =
            {
                [legacyRootId] = new DeviceGroupDto(
                    legacyRootId,
                    "Legacy Root",
                    null,
                    0,
                    true,
                    null,
                    1),
                [childId] = new DeviceGroupDto(
                    childId,
                    "Child",
                    legacyRootId,
                    0,
                    true,
                    null,
                    1)
            }
        };
        var service = CreateService(fake);

        var create = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            ValidCreateDeviceRequest(childId),
            CancellationToken.None);
        var update = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            Guid.NewGuid(),
            ValidUpdateDeviceRequest(childId, 1),
            CancellationToken.None);

        AssertError(create, "CATALOG_VALIDATION_FAILED", 400);
        AssertError(update, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.CreateDeviceCalls);
        Assert.Equal(0, fake.UpdateDeviceCalls);
    }

    [Fact]
    public async Task UpdateGroupAsync_RejectsParentCycleAndMapsConflict()
    {
        var id = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var conflictRootId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Snapshot = new CatalogSnapshotDto(
                [
                    new DeviceGroupDto(id, "A", null, 0, true, MonitorGroupType.Chute, 1),
                    new DeviceGroupDto(parentId, "B", id, 0, true, null, 1),
                    new DeviceGroupDto(conflictRootId, "Conflict Root", null, 0, true, MonitorGroupType.Chute, 1)
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
            conflictRootId,
            new UpdateGroupRequest("B2", null, 0, true, MonitorGroupType.Chute, 1),
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
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
        fake.CreateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
            CatalogRepositoryStatus.Success,
            DeviceDto(Guid.NewGuid(), groupId, "Camera", hasPassword: false));
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
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
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
        AssertError(conflict, "CHANNEL_CONFLICT", 409);
        Assert.Equal(0, fake.CreateDeviceCalls);
    }

    [Fact]
    public async Task CreateDeviceAsync_NullPasswordIsValidationFailureWithoutWrite()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
        var service = CreateService(fake);
        var result = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            ValidCreateDeviceRequest(groupId) with { Password = null! },
            CancellationToken.None);

        AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
        Assert.Equal(0, fake.CreateDeviceCalls);
    }

    [Fact]
    public async Task CreateDeviceAsync_MissingGroupIsNotFoundWithoutWrite()
    {
        var fake = new FakeCentralCatalogRepository();
        var service = CreateService(fake);
        var result = await InvokeAsync(
            service,
            "CreateDeviceAsync",
            ValidCreateDeviceRequest(Guid.NewGuid()),
            CancellationToken.None);

        AssertError(result, "GROUP_NOT_FOUND", 404);
        Assert.Equal(0, fake.CreateDeviceCalls);
    }

    [Fact]
    public async Task CreateDeviceAsync_RejectsInvalidPortsAndTransportModeWithoutWrite()
    {
        var groupId = Guid.NewGuid();
        var requests = new[]
        {
            ValidCreateDeviceRequest(groupId) with { SdkPort = 0 },
            ValidCreateDeviceRequest(groupId) with { SdkPort = 65536 },
            ValidCreateDeviceRequest(groupId) with { RtspPort = 0 },
            ValidCreateDeviceRequest(groupId) with { RtspPort = 65536 },
            ValidCreateDeviceRequest(groupId) with { TransportMode = (TransportMode)999 }
        };

        foreach (var request in requests)
        {
            var fake = new FakeCentralCatalogRepository();
            var service = CreateService(fake);
            var result = await InvokeAsync(
                service,
                "CreateDeviceAsync",
                request,
                CancellationToken.None);

            AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
            Assert.Equal(0, fake.CreateDeviceCalls);
        }
    }

    [Fact]
    public async Task CreateDeviceAsync_RejectsInvalidChannelFieldsWithoutWrite()
    {
        var groupId = Guid.NewGuid();
        var valid = ValidCreateDeviceRequest(groupId);
        var invalidRequests = new[]
        {
            valid with
            {
                Channels = [valid.Channels[0] with { ChannelNo = 0 }]
            },
            valid with
            {
                Channels = [valid.Channels[0] with { StreamType = (StreamType)999 }]
            },
            valid with
            {
                Channels =
                [
                    valid.Channels[0],
                    valid.Channels[0] with { Id = valid.Channels[0].Id }
                ]
            }
        };

        foreach (var request in invalidRequests)
        {
            var fake = new FakeCentralCatalogRepository();
            var service = CreateService(fake);
            var result = await InvokeAsync(
                service,
                "CreateDeviceAsync",
                request,
                CancellationToken.None);

            AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
            Assert.Equal(0, fake.CreateDeviceCalls);
        }
    }

    [Fact]
    public async Task UpdateDeviceAsync_DuplicateChannelIdentity_ReturnsChannelConflictWithoutWrite()
    {
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var firstChannelId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            Groups = { [groupId] = new DeviceGroupDto(groupId, "Group", null, 0, true, 1) }
        };
        var service = CreateService(fake);
        var request = ValidUpdateDeviceRequest(groupId, expectedRevision: 1) with
        {
            Channels =
            [
                new CameraChannelInput(firstChannelId, 1, "Main", StreamType.Main, true),
                new CameraChannelInput(Guid.NewGuid(), 1, "Duplicate Main", StreamType.Main, true)
            ]
        };

        var result = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            deviceId,
            request,
            CancellationToken.None);

        AssertError(result, "CHANNEL_CONFLICT", 409);
        Assert.Equal(0, fake.UpdateDeviceCalls);
    }

    [Fact]
    public async Task UpdateDeviceAsync_ForwardsNullPasswordWithoutReadingIt()
    {
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
        fake.UpdateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
            CatalogRepositoryStatus.Success,
            DeviceDto(deviceId, groupId, "Updated", hasPassword: true));
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
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
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
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
        fake.UpdateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
            CatalogRepositoryStatus.RevisionConflict,
            CurrentRevision: 8);
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
    public async Task DeleteDeviceAsync_MapsRevisionConflict()
    {
        var fake = new FakeCentralCatalogRepository
        {
            DeleteDeviceResult = new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.RevisionConflict,
                9)
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "DeleteDeviceAsync",
            Guid.NewGuid(),
            8L,
            CancellationToken.None);

        AssertError(result, "DEVICE_REVISION_CONFLICT", 409, 9);
    }

    [Fact]
    public async Task DeleteGroupAsync_MapsRevisionConflict()
    {
        var fake = new FakeCentralCatalogRepository
        {
            DeleteGroupResult = new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.RevisionConflict,
                6)
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "DeleteGroupAsync",
            Guid.NewGuid(),
            5L,
            CancellationToken.None);

        AssertError(result, "GROUP_REVISION_CONFLICT", 409, 6);
    }

    [Fact]
    public async Task UpdateDeviceAsync_MapsNotFound()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository();
        AddBusinessChild(fake, groupId);
        fake.UpdateDeviceResult = new CatalogRepositoryResult<CameraDeviceDto>(
            CatalogRepositoryStatus.NotFound);
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "UpdateDeviceAsync",
            Guid.NewGuid(),
            ValidUpdateDeviceRequest(groupId, 1),
            CancellationToken.None);

        AssertError(result, "DEVICE_NOT_FOUND", 404);
    }

    [Fact]
    public async Task UpdateGroupAsync_MapsNotFound()
    {
        var fake = new FakeCentralCatalogRepository
        {
            UpdateGroupResult = new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.NotFound)
        };
        var service = CreateService(fake);

        var result = await InvokeAsync(
            service,
            "UpdateGroupAsync",
            Guid.NewGuid(),
            new UpdateGroupRequest("Updated", null, 0, true, 1),
            CancellationToken.None);

        AssertError(result, "GROUP_NOT_FOUND", 404);
    }

    [Fact]
    public async Task MutationException_IsSanitizedAndCancellationPropagates()
    {
        var groupId = Guid.NewGuid();
        var fake = new FakeCentralCatalogRepository
        {
            CreateGroupException = new InvalidOperationException(
                "TOP-SECRET-PASSWORD C:\\secret\\catalog.db")
        };
        var service = CreateService(fake);
        var failure = await InvokeAsync(
            service,
            "CreateGroupAsync",
            new CreateGroupRequest(
                Guid.NewGuid(),
                "Group",
                null,
                0,
                true,
                MonitorGroupType.Chute),
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

    private static object CreateService(
        FakeCentralCatalogRepository repository,
        ILogger<CatalogApplicationService>? logger = null)
    {
        var serviceType = typeof(Program).Assembly.GetType(
            "VideoMonitor.Server.Catalog.CatalogApplicationService");
        Assert.NotNull(serviceType);
        var service = Activator.CreateInstance(
            serviceType!,
            repository,
            logger ?? new RecordingLogger());
        Assert.NotNull(service);
        return service!;
    }

    private static void AddBusinessChild(
        FakeCentralCatalogRepository fake,
        Guid childId)
    {
        var rootId = Guid.NewGuid();
        fake.Groups[rootId] = new DeviceGroupDto(
            rootId,
            "Root",
            null,
            0,
            true,
            MonitorGroupType.Chute,
            1);
        fake.Groups[childId] = new DeviceGroupDto(
            childId,
            "Child",
            rootId,
            0,
            true,
            null,
            1);
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

    private static CreateDeviceRequest ValidCreateDeviceRequest(
        Guid groupId,
        string password = "password") =>
        new(
            Guid.NewGuid(),
            groupId,
            "Camera",
            "192.0.2.10",
            8000,
            554,
            "user",
            password,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
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

    private sealed class RecordingLogger : ILogger<CatalogApplicationService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
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
                new DeviceGroupDto(
                    group.Id,
                    group.Name,
                    group.ParentId,
                    group.Sort,
                    group.Enabled,
                    group.Kind,
                    1)));
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
                new DeviceGroupDto(
                    group.Id,
                    group.Name,
                    group.ParentId,
                    group.Sort,
                    group.Enabled,
                    group.Kind,
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
