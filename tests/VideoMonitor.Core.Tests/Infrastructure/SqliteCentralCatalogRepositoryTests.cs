using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteCentralCatalogRepositoryTests
{
    [Fact]
    public void Task4Contracts_ExposeRevisionProtectedUpdateDelete()
    {
        var assembly = typeof(SqliteConnectionFactory).Assembly;
        var repositoryContract = assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.ICentralCatalogRepository");
        var resultType = assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.CatalogRepositoryResult`1");
        var deleteResultType = assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.CatalogRepositoryDeleteResult");

        Assert.NotNull(repositoryContract);
        Assert.NotNull(resultType);
        Assert.NotNull(deleteResultType);

        var methodNames = repositoryContract!
            .GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CreateDeviceAsync",
                "CreateGroupAsync",
                "DeleteDeviceAsync",
                "DeleteGroupAsync",
                "GetCatalogAsync",
                "GetDeviceAsync",
                "GetGroupAsync",
                "UpdateDeviceAsync",
                "UpdateGroupAsync"
            },
            methodNames);
    }

    [Fact]
    public async Task GetCatalogAsync_EmptyDatabaseReturnsEmptySnapshot()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());

        var snapshot = await GetCatalogAsync(repository);

        Assert.Empty(snapshot.Groups);
        Assert.Empty(snapshot.Devices);
    }

    [Fact]
    public async Task CreateGroupAsync_StoresRevisionOneAndGetGroupReadsIt()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = new DeviceGroup
        {
            Id = Guid.Parse("91000000-0000-0000-0000-000000000001"),
            Name = "Test Group",
            Sort = 4,
            Enabled = true,
            Revision = 77
        };

        var result = await InvokeAsync(
            repository,
            "CreateGroupAsync",
            group,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var created = Assert.IsType<DeviceGroupDto>(GetProperty(result, "Value"));
        Assert.Equal(group.Id, created.Id);
        Assert.Equal(group.Name, created.Name);
        Assert.Equal(1L, created.Revision);

        var loaded = await GetGroupAsync(repository, group.Id);
        Assert.NotNull(loaded);
        Assert.Equal(1L, loaded!.Revision);
        Assert.Equal(group.Name, loaded.Name);

        await using var connection = context.Factory.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(
            "1",
            await ReadScalarAsync(
                connection,
                "SELECT revision FROM device_groups WHERE id = $id;",
                ("$id", group.Id.ToString("N"))));
    }

    [Fact]
    public async Task GroupKind_RoundTripsForRootAndChild()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var root = await CreateGroupAsync(
            repository,
            id: Guid.Parse("91000000-0000-0000-0000-000000000011"),
            name: "Chute Root",
            kind: MonitorGroupType.Chute);
        var child = await CreateGroupAsync(
            repository,
            id: Guid.Parse("91000000-0000-0000-0000-000000000012"),
            parentId: root.Id,
            name: "Chute Child");

        var loadedRoot = await GetGroupAsync(repository, root.Id);
        var loadedChild = await GetGroupAsync(repository, child.Id);

        Assert.Equal(MonitorGroupType.Chute, loadedRoot!.Kind);
        Assert.Null(loadedChild!.Kind);
    }

    [Fact]
    public async Task UpdateGroupAsync_PersistsKind_AndGetCatalogReadsIt()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var root = await CreateGroupAsync(
            repository,
            id: Guid.Parse("91000000-0000-0000-0000-000000000021"),
            name: "Legacy Root");
        var child = await CreateGroupAsync(
            repository,
            id: Guid.Parse("91000000-0000-0000-0000-000000000022"),
            parentId: root.Id,
            name: "Legacy Child");

        var rootDto = (await GetGroupAsync(repository, root.Id))!;
        var model = ToWriteModel(rootDto);
        model.Kind = MonitorGroupType.Chute;

        var result = await UpdateGroupAsync(repository, model, rootDto.Revision);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var updated = Assert.IsType<DeviceGroupDto>(GetProperty(result, "Value"));
        Assert.Equal(MonitorGroupType.Chute, updated.Kind);
        Assert.Equal(2L, updated.Revision);

        var loadedRoot = (await GetGroupAsync(repository, root.Id))!;
        Assert.Equal(MonitorGroupType.Chute, loadedRoot.Kind);
        Assert.Equal(2L, loadedRoot.Revision);

        var snapshot = await GetCatalogAsync(repository);
        var catalogRoot = Assert.Single(snapshot.Groups, group => group.Id == root.Id);
        var catalogChild = Assert.Single(snapshot.Groups, group => group.Id == child.Id);
        Assert.Equal(MonitorGroupType.Chute, catalogRoot.Kind);
        Assert.Equal(2L, catalogRoot.Revision);
        Assert.Null(catalogChild.Kind);
        Assert.Equal(root.Id, catalogChild.ParentId);
    }

    [Fact]
    public async Task GetGroupAsync_InvalidPersistedKind_ThrowsInvalidDataException()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        await context.SetGroupKindAsync(group.Id, "NotAGroupKind");

        await Assert.ThrowsAsync<InvalidDataException>(() => GetGroupAsync(repository, group.Id));
    }

    [Fact]
    public async Task CreateDeviceAsync_ReturnsSafeDtoAndReadsMainAndSubChannels()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Password-Only-In-Memory");

        var result = await InvokeAsync(
            repository,
            "CreateDeviceAsync",
            device,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var created = Assert.IsType<CameraDeviceDto>(GetProperty(result, "Value"));
        Assert.Equal(device.Id, created.Id);
        Assert.Equal(device.Name, created.Name);
        Assert.Equal(1L, created.Revision);
        Assert.True(created.HasPassword);
        Assert.Equal(2, created.Channels.Count);
        Assert.Equal(StreamType.Main, created.Channels[0].StreamType);
        Assert.Equal(StreamType.Sub, created.Channels[1].StreamType);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);

        var rawCiphertext = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        Assert.StartsWith("test-protected:", rawCiphertext, StringComparison.Ordinal);
        Assert.DoesNotContain("Password-Only-In-Memory", rawCiphertext, StringComparison.Ordinal);

        var loaded = await GetDeviceAsync(repository, device.Id);
        Assert.NotNull(loaded);
        Assert.Equal(device.Name, loaded!.Name);
        Assert.True(loaded.HasPassword);
        Assert.Equal(2, loaded.Channels.Count);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task GetCatalogAsync_ReturnsConsistentSafeSnapshotWithoutDecrypting()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Catalog-Password");
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);

        protector.ResetUnprotectCalls();
        var snapshot = await GetCatalogAsync(repository);

        var loadedGroup = Assert.Single(snapshot.Groups);
        var loadedDevice = Assert.Single(snapshot.Devices);
        Assert.Equal(group.Id, loadedGroup.Id);
        Assert.Equal(device.Id, loadedDevice.Id);
        Assert.Equal(group.Id, loadedDevice.GroupId);
        Assert.Equal(2, loadedDevice.Channels.Count);
        Assert.True(loadedDevice.HasPassword);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task GetDeviceAsync_ReadsOnlyRequestedDeviceChannels_WhenOtherDevicesExist()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        var deviceB = CreateDevice(
            group.Id,
            string.Empty,
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000002"),
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000003"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Other Camera");

        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceB, CancellationToken.None);
        protector.ResetUnprotectCalls();

        var loaded = await GetDeviceAsync(repository, deviceA.Id);

        Assert.NotNull(loaded);
        Assert.Equal(deviceA.Id, loaded!.Id);
        Assert.Equal(2, loaded.Channels.Count);
        Assert.All(loaded.Channels, channel => Assert.Equal(deviceA.Id, channel.DeviceId));
        Assert.Equal(
            deviceA.Channels.Select(channel => channel.Id),
            loaded.Channels.Select(channel => channel.Id));
        Assert.DoesNotContain(
            loaded.Channels,
            channel => deviceB.Channels.Any(other => other.Id == channel.Id));
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task CreateDeviceAsync_DuplicateDeviceId_IsNotChannelConflict()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        var duplicate = CreateDevice(
            group.Id,
            string.Empty,
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000003"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Duplicate Device");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InvokeAsync(
            repository,
            "CreateDeviceAsync",
            duplicate,
            CancellationToken.None));

        Assert.NotEqual(2067, exception.SqliteExtendedErrorCode);
        Assert.Equal("Central Camera", (await GetDeviceAsync(repository, deviceA.Id))!.Name);
    }

    [Fact]
    public async Task CreateDeviceAsync_DuplicateChannelPrimaryKey_RollsBackAndIsNotChannelConflict()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        var deviceB = CreateDevice(
            group.Id,
            string.Empty,
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000002"),
            mainChannelId: deviceA.Channels[0].Id,
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Second Device");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InvokeAsync(
            repository,
            "CreateDeviceAsync",
            deviceB,
            CancellationToken.None));

        Assert.NotEqual(2067, exception.SqliteExtendedErrorCode);
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_devices WHERE id = $id;",
                ("$id", deviceB.Id.ToString("N"))));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_channels WHERE device_id = $id;",
                ("$id", deviceB.Id.ToString("N"))));
        Assert.Equal("Central Camera", (await GetDeviceAsync(repository, deviceA.Id))!.Name);
        Assert.Equal(2, (await GetDeviceAsync(repository, deviceA.Id))!.Channels.Count);
    }

    [Fact]
    public async Task CreateDeviceAsync_EmptyPasswordStoresEmptyWithoutProtecting()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);

        var result = await InvokeAsync(
            repository,
            "CreateDeviceAsync",
            device,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var created = Assert.IsType<CameraDeviceDto>(GetProperty(result, "Value"));
        Assert.False(created.HasPassword);
        Assert.Equal(0, protector.ProtectCalls);
        Assert.Equal(
            string.Empty,
            await ReadScalarAsync(
                context,
                "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task CreateDeviceAsync_DuplicateChannelIdentityRollsBackAggregate()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);
        device.Channels.Add(new CameraChannel
        {
            Id = Guid.Parse("93000000-0000-0000-0000-000000000003"),
            DeviceId = device.Id,
            ChannelNo = 1,
            ChannelName = "Duplicate Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-duplicate",
            Enabled = true
        });

        var result = await InvokeAsync(
            repository,
            "CreateDeviceAsync",
            device,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.ChannelConflict, GetStatus(result));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_channels WHERE device_id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task UpdateDeviceAsync_DuplicateChannelIdentity_ReturnsChannelConflictWithoutChanges()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);

        var dto = (await GetDeviceAsync(repository, device.Id))!;
        var model = ToWriteModel(dto, "MUST-NOT-BE-USED");
        model.Name = "Must Not Persist";
        var existingMain = model.Channels.Single(channel =>
            channel.StreamType == StreamType.Main);
        model.Channels.Add(new CameraChannel
        {
            Id = Guid.Parse("97000000-0000-0000-0000-000000000005"),
            DeviceId = model.Id,
            ChannelNo = existingMain.ChannelNo,
            ChannelName = "Duplicate Main",
            StreamType = existingMain.StreamType,
            Enabled = true
        });

        var result = await UpdateDeviceAsync(
            repository,
            model,
            newPassword: null,
            dto.Revision);

        Assert.Equal(CatalogRepositoryStatus.ChannelConflict, GetStatus(result));

        var loaded = (await GetDeviceAsync(repository, device.Id))!;
        Assert.Equal(1L, loaded.Revision);
        Assert.Equal(dto.Name, loaded.Name);
        Assert.Equal(
            dto.Channels.Select(channel => channel.Id),
            loaded.Channels.Select(channel => channel.Id));
        Assert.Equal(dto.Channels.Count, loaded.Channels.Count);
        Assert.DoesNotContain(
            loaded.Channels,
            channel => channel.Id == Guid.Parse("97000000-0000-0000-0000-000000000005"));
    }

    [Fact]
    public async Task GetMissingGroupAndDevice_ReturnNull()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());

        Assert.Null(await GetGroupAsync(repository, Guid.Parse("94000000-0000-0000-0000-000000000001")));
        Assert.Null(await GetDeviceAsync(repository, Guid.Parse("95000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public async Task UpdateDeviceAsync_FirstWriterWins_AndStaleWriterCannotOverwrite()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);

        var dtoA = (await GetDeviceAsync(repository, device.Id))!;
        var dtoB = (await GetDeviceAsync(repository, device.Id))!;
        var modelA = ToWriteModel(dtoA, "MUST-NOT-BE-USED");
        var modelB = ToWriteModel(dtoB, "MUST-NOT-BE-USED");
        modelA.Name = "First Writer";
        modelB.Name = "Stale Writer";
        modelB.Channels[0].ChannelName = "Stale Channel";

        var firstResult = await UpdateDeviceAsync(
            repository,
            modelA,
            newPassword: null,
            dtoA.Revision);
        var staleResult = await UpdateDeviceAsync(
            repository,
            modelB,
            newPassword: null,
            dtoB.Revision);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(firstResult));
        Assert.Equal(2L, GetDtoRevision(GetProperty(firstResult, "Value")));
        Assert.Equal(CatalogRepositoryStatus.RevisionConflict, GetStatus(staleResult));
        Assert.Equal(2L, GetCurrentRevision(staleResult));

        var loaded = (await GetDeviceAsync(repository, device.Id))!;
        Assert.Equal("First Writer", loaded.Name);
        Assert.Equal(2L, loaded.Revision);
        Assert.Equal("CH1 Main", loaded.Channels[0].ChannelName);
    }

    [Fact]
    public async Task UpdateGroupAsync_FirstWriterWins_AndStaleWriterCannotOverwrite()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var dtoA = (await GetGroupAsync(repository, group.Id))!;
        var dtoB = (await GetGroupAsync(repository, group.Id))!;
        var modelA = ToWriteModel(dtoA);
        var modelB = ToWriteModel(dtoB);
        modelA.Name = "First Group Writer";
        modelB.Name = "Stale Group Writer";

        var firstResult = await UpdateGroupAsync(repository, modelA, dtoA.Revision);
        var staleResult = await UpdateGroupAsync(repository, modelB, dtoB.Revision);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(firstResult));
        Assert.Equal(2L, GetDtoRevision(GetProperty(firstResult, "Value")));
        Assert.Equal(CatalogRepositoryStatus.RevisionConflict, GetStatus(staleResult));
        Assert.Equal(2L, GetCurrentRevision(staleResult));
        var loaded = (await GetGroupAsync(repository, group.Id))!;
        Assert.Equal("First Group Writer", loaded.Name);
        Assert.Equal(2L, loaded.Revision);
    }

    [Fact]
    public async Task UpdateDeviceAsync_NullPasswordPreservesCiphertextWithoutProtection()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Original-Secret");
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);
        var ciphertextBefore = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        var dto = (await GetDeviceAsync(repository, device.Id))!;
        var model = ToWriteModel(dto, "MUST-NOT-BE-USED");
        model.Name = "Updated Without Password";
        protector.ResetCounts();

        var result = await UpdateDeviceAsync(repository, model, null, dto.Revision);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        Assert.Equal(2L, GetDtoRevision(GetProperty(result, "Value")));
        Assert.True(GetDeviceHasPassword(GetProperty(result, "Value")));
        Assert.Equal(0, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
        var ciphertextAfter = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        Assert.Equal(ciphertextBefore, ciphertextAfter);
    }

    [Fact]
    public async Task UpdateDeviceAsync_NonEmptyPasswordReplacesCiphertextWithoutDecrypting()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Original-Secret");
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);
        var ciphertextBefore = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        var dto = (await GetDeviceAsync(repository, device.Id))!;
        var model = ToWriteModel(dto, "MUST-NOT-BE-USED");
        protector.ResetCounts();

        var result = await UpdateDeviceAsync(
            repository,
            model,
            "New-Secret",
            dto.Revision);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        Assert.Equal(2L, GetDtoRevision(GetProperty(result, "Value")));
        Assert.True(GetDeviceHasPassword(GetProperty(result, "Value")));
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
        Assert.Equal(
            $"camera-password:{device.Id:N}",
            protector.LastPurpose);
        var ciphertextAfter = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        Assert.NotEqual(ciphertextBefore, ciphertextAfter);
        Assert.DoesNotContain("New-Secret", ciphertextAfter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateDeviceAsync_EmptyPasswordIsRejectedWithoutDatabaseChanges()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Original-Secret");
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);
        var dto = (await GetDeviceAsync(repository, device.Id))!;
        var model = ToWriteModel(dto, "MUST-NOT-BE-USED");
        var ciphertextBefore = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        protector.ResetCounts();

        await Assert.ThrowsAsync<ArgumentException>(() => UpdateDeviceAsync(
            repository,
            model,
            string.Empty,
            dto.Revision));

        Assert.Equal(0, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
        var loaded = (await GetDeviceAsync(repository, device.Id))!;
        Assert.Equal(dto.Revision, loaded.Revision);
        Assert.Equal(
            ciphertextBefore,
            await ReadScalarAsync(
                context,
                "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task UpdateDeviceAsync_PasswordProtectionFailurePreservesExistingAggregate()
    {
        await using var context = await TestContext.CreateAsync();
        var normalProtector = new CountingSecretProtector();
        var repository = CreateRepository(context, normalProtector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Original-Secret");
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);
        var dto = (await GetDeviceAsync(repository, device.Id))!;
        var model = ToWriteModel(dto, "MUST-NOT-BE-USED");
        model.Name = "Must Not Persist";
        var ciphertextBefore = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        var failingProtector = new FailingReplacementProtector();
        var failingRepository = CreateRepository(context, failingProtector);

        await Assert.ThrowsAsync<CryptographicException>(() => UpdateDeviceAsync(
            failingRepository,
            model,
            "Replacement-Secret",
            dto.Revision));

        Assert.Equal(1, failingProtector.ProtectCalls);
        Assert.Equal(0, failingProtector.UnprotectCalls);
        var loaded = (await GetDeviceAsync(repository, device.Id))!;
        Assert.Equal(dto.Revision, loaded.Revision);
        Assert.Equal("Central Camera", loaded.Name);
        Assert.Equal(2, loaded.Channels.Count);
        Assert.Equal(
            ciphertextBefore,
            await ReadScalarAsync(
                context,
                "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task UpdateDeviceAsync_ChannelDatabaseFailureRollsBackAggregate()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        var deviceB = CreateDevice(
            group.Id,
            "Original-B-Secret",
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000002"),
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000003"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Second Device");
        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceB, CancellationToken.None);
        var dtoB = (await GetDeviceAsync(repository, deviceB.Id))!;
        var modelB = ToWriteModel(dtoB, "MUST-NOT-BE-USED");
        modelB.Name = "Must Roll Back";
        modelB.Channels[0].Id = deviceA.Channels[0].Id;
        var ciphertextBefore = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", deviceB.Id.ToString("N")));
        protector.ResetCounts();

        await Assert.ThrowsAsync<SqliteException>(() => UpdateDeviceAsync(
            repository,
            modelB,
            "Replacement-Secret",
            dtoB.Revision));

        Assert.Equal(1, protector.ProtectCalls);
        var loadedB = (await GetDeviceAsync(repository, deviceB.Id))!;
        Assert.Equal(dtoB.Revision, loadedB.Revision);
        Assert.Equal("Second Device", loadedB.Name);
        Assert.Equal(
            dtoB.Channels.Select(channel => channel.Id),
            loadedB.Channels.Select(channel => channel.Id));
        Assert.Equal(
            ciphertextBefore,
            await ReadScalarAsync(
                context,
                "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
                ("$id", deviceB.Id.ToString("N"))));
        var loadedA = (await GetDeviceAsync(repository, deviceA.Id))!;
        Assert.Equal("Central Camera", loadedA.Name);
        Assert.Equal(2, loadedA.Channels.Count);
    }

    [Fact]
    public async Task UpdateDeviceAsync_AndDeleteDeviceAsync_DistinguishStaleAndSuccess()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);
        var dto = (await GetDeviceAsync(repository, device.Id))!;
        var model = ToWriteModel(dto, "MUST-NOT-BE-USED");
        model.Name = "Updated Device";
        await UpdateDeviceAsync(repository, model, null, dto.Revision);

        var staleDelete = await DeleteDeviceAsync(repository, device.Id, dto.Revision);
        Assert.Equal(CatalogRepositoryStatus.RevisionConflict, GetStatus(staleDelete));
        Assert.Equal(2L, GetCurrentRevision(staleDelete));
        Assert.Equal(2, (await GetDeviceAsync(repository, device.Id))!.Channels.Count);

        var successDelete = await DeleteDeviceAsync(repository, device.Id, 2L);
        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(successDelete));
        Assert.Null(await GetDeviceAsync(repository, device.Id));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_channels WHERE device_id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task MissingDeviceUpdateAndDelete_ReturnNotFound()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var missing = CreateDevice(
            group.Id,
            string.Empty,
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000009"),
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000009"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000010"));

        var update = await UpdateDeviceAsync(repository, missing, null, 1L);
        var delete = await DeleteDeviceAsync(repository, missing.Id, 1L);

        Assert.Equal(CatalogRepositoryStatus.NotFound, GetStatus(update));
        Assert.Equal(CatalogRepositoryStatus.NotFound, GetStatus(delete));
        Assert.Null(GetCurrentRevision(update));
        Assert.Null(GetCurrentRevision(delete));
    }

    [Fact]
    public async Task DeleteGroupAsync_EmptyTopLevelGroupSucceeds()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(
            repository,
            Guid.Parse("98000000-0000-0000-0000-000000000001"),
            name: "Empty Top Level");

        var result = await DeleteGroupAsync(repository, group.Id, group.Revision);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        Assert.Null(await GetGroupAsync(repository, group.Id));
    }

    [Fact]
    public async Task DeleteGroupAsync_WithChildOrDeviceReturnsGroupNotEmpty()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var root = await CreateGroupAsync(
            repository,
            Guid.Parse("98000000-0000-0000-0000-000000000002"),
            name: "Root");
        await CreateGroupAsync(
            repository,
            Guid.Parse("98000000-0000-0000-0000-000000000003"),
            root.Id,
            "Child");

        var childResult = await DeleteGroupAsync(repository, root.Id, root.Revision);
        Assert.Equal(CatalogRepositoryStatus.GroupNotEmpty, GetStatus(childResult));
        Assert.NotNull(await GetGroupAsync(repository, root.Id));

        var deviceGroup = await CreateGroupAsync(
            repository,
            Guid.Parse("98000000-0000-0000-0000-000000000004"),
            name: "Device Group");
        var device = CreateDevice(
            deviceGroup.Id,
            string.Empty,
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000004"),
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000011"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000012"));
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);

        var deviceResult = await DeleteGroupAsync(
            repository,
            deviceGroup.Id,
            deviceGroup.Revision);
        Assert.Equal(CatalogRepositoryStatus.GroupNotEmpty, GetStatus(deviceResult));
        Assert.NotNull(await GetGroupAsync(repository, deviceGroup.Id));
    }

    [Fact]
    public async Task DeleteGroupAsync_StaleAndMissingReturnExpectedStatus()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(
            repository,
            Guid.Parse("98000000-0000-0000-0000-000000000005"),
            name: "Revision Group");
        var model = ToWriteModel(group);
        model.Name = "Updated Group";
        var update = await UpdateGroupAsync(repository, model, group.Revision);
        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(update));

        var stale = await DeleteGroupAsync(repository, group.Id, group.Revision);
        var missing = await DeleteGroupAsync(
            repository,
            Guid.Parse("98000000-0000-0000-0000-000000000009"),
            1L);

        Assert.Equal(CatalogRepositoryStatus.RevisionConflict, GetStatus(stale));
        Assert.Equal(2L, GetCurrentRevision(stale));
        Assert.Equal(CatalogRepositoryStatus.NotFound, GetStatus(missing));
        Assert.Null(GetCurrentRevision(missing));
        Assert.NotNull(await GetGroupAsync(repository, group.Id));
    }

    [Fact]
    public async Task MissingGroupUpdate_ReturnsNotFound()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var model = new DeviceGroup
        {
            Id = Guid.Parse("98000000-0000-0000-0000-000000000010"),
            Name = "Missing",
            Enabled = true
        };

        var result = await UpdateGroupAsync(repository, model, 1L);

        Assert.Equal(CatalogRepositoryStatus.NotFound, GetStatus(result));
        Assert.Null(GetCurrentRevision(result));
    }

    private static object CreateRepository(TestContext context, ISecretProtector protector)
    {
        var repositoryType = typeof(SqliteConnectionFactory).Assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.SqliteCentralCatalogRepository");
        Assert.NotNull(repositoryType);

        var repository = Activator.CreateInstance(
            repositoryType!,
            context.Factory,
            protector);
        Assert.NotNull(repository);
        return repository!;
    }

    private static async Task<CatalogSnapshotDto> GetCatalogAsync(object repository) =>
        Assert.IsType<CatalogSnapshotDto>(await InvokeAsync(
            repository,
            "GetCatalogAsync",
            CancellationToken.None));

    private static async Task<DeviceGroupDto?> GetGroupAsync(object repository, Guid id) =>
        (DeviceGroupDto?)await InvokeAsync(
            repository,
            "GetGroupAsync",
            id,
            CancellationToken.None);

    private static async Task<CameraDeviceDto?> GetDeviceAsync(object repository, Guid id) =>
        (CameraDeviceDto?)await InvokeAsync(
            repository,
            "GetDeviceAsync",
            id,
            CancellationToken.None);

    private static Task<object?> UpdateDeviceAsync(
        object repository,
        CameraDevice device,
        string? newPassword,
        long expectedRevision) =>
        InvokeAsync(
            repository,
            "UpdateDeviceAsync",
            device,
            newPassword,
            expectedRevision,
            CancellationToken.None);

    private static Task<object?> DeleteDeviceAsync(
        object repository,
        Guid id,
        long expectedRevision) =>
        InvokeAsync(
            repository,
            "DeleteDeviceAsync",
            id,
            expectedRevision,
            CancellationToken.None);

    private static Task<object?> UpdateGroupAsync(
        object repository,
        DeviceGroup group,
        long expectedRevision) =>
        InvokeAsync(
            repository,
            "UpdateGroupAsync",
            group,
            expectedRevision,
            CancellationToken.None);

    private static Task<object?> DeleteGroupAsync(
        object repository,
        Guid id,
        long expectedRevision) =>
        InvokeAsync(
            repository,
            "DeleteGroupAsync",
            id,
            expectedRevision,
            CancellationToken.None);

    private static async Task<DeviceGroupDto> CreateGroupAsync(
        object repository,
        Guid? id = null,
        Guid? parentId = null,
        string name = "Device Group",
        MonitorGroupType? kind = null)
    {
        var group = new DeviceGroup
        {
            Id = id ?? Guid.Parse("92000000-0000-0000-0000-000000000001"),
            Name = name,
            ParentId = parentId,
            Sort = 1,
            Enabled = true,
            Kind = kind,
            Revision = 15
        };
        var result = await InvokeAsync(
            repository,
            "CreateGroupAsync",
            group,
            CancellationToken.None);
        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        return Assert.IsType<DeviceGroupDto>(GetProperty(result, "Value"));
    }

    private static CameraDevice CreateDevice(
        Guid groupId,
        string password,
        Guid? deviceId = null,
        Guid? mainChannelId = null,
        Guid? subChannelId = null,
        string name = "Central Camera")
    {
        var actualDeviceId = deviceId ??
            Guid.Parse("96000000-0000-0000-0000-000000000001");
        var actualMainChannelId = mainChannelId ??
            Guid.Parse("97000000-0000-0000-0000-000000000001");
        var actualSubChannelId = subChannelId ??
            Guid.Parse("97000000-0000-0000-0000-000000000002");
        var device = new CameraDevice
        {
            Id = actualDeviceId,
            Revision = 99,
            Name = name,
            GroupId = groupId,
            IpAddress = "192.0.2.10",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "camera-user",
            Password = password,
            Manufacturer = "Hikvision",
            Model = "DS-2CD",
            TransportMode = TransportMode.Tcp,
            Status = CameraStatus.Online,
            Enabled = true,
            Remark = "Task 3 test"
        };
        device.Channels.Add(new CameraChannel
        {
            Id = actualMainChannelId,
            DeviceId = actualDeviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-main",
            Enabled = true
        });
        device.Channels.Add(new CameraChannel
        {
            Id = actualSubChannelId,
            DeviceId = actualDeviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Sub",
            StreamType = StreamType.Sub,
            StreamId = "runtime-sub",
            Enabled = false
        });
        return device;
    }

    private static DeviceGroup ToWriteModel(DeviceGroupDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            ParentId = dto.ParentId,
            Sort = dto.Sort,
            Enabled = dto.Enabled,
            Kind = dto.Kind,
            Revision = dto.Revision
        };

    private static CameraDevice ToWriteModel(CameraDeviceDto dto, string password)
    {
        var device = new CameraDevice
        {
            Id = dto.Id,
            GroupId = dto.GroupId,
            Name = dto.Name,
            IpAddress = dto.IpAddress,
            SdkPort = dto.SdkPort,
            RtspPort = dto.RtspPort,
            Username = dto.Username,
            Password = password,
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            TransportMode = dto.TransportMode,
            Enabled = dto.Enabled,
            Remark = dto.Remark,
            Revision = dto.Revision,
            Status = CameraStatus.Online
        };
        foreach (var channel in dto.Channels)
        {
            device.Channels.Add(new CameraChannel
            {
                Id = channel.Id,
                DeviceId = channel.DeviceId,
                ChannelNo = channel.ChannelNo,
                ChannelName = channel.ChannelName,
                StreamType = channel.StreamType,
                Enabled = channel.Enabled
            });
        }

        return device;
    }

    private static long GetDtoRevision(object? dto) =>
        Convert.ToInt64(GetProperty(dto, "Revision"));

    private static bool GetDeviceHasPassword(object? dto) =>
        (bool)GetProperty(dto, "HasPassword")!;

    private static long? GetCurrentRevision(object? result)
    {
        var value = GetProperty(result, "CurrentRevision");
        return value is null ? null : Convert.ToInt64(value);
    }

    private static CatalogRepositoryStatus GetStatus(object? result) =>
        Assert.IsType<CatalogRepositoryStatus>(GetProperty(result, "Status"));

    private static object? GetProperty(object? source, string name)
    {
        Assert.NotNull(source);
        var property = source!.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property!.GetValue(source);
    }

    private static async Task<object?> InvokeAsync(
        object repository,
        string methodName,
        params object?[] arguments)
    {
        var method = repository.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        object? invocation;
        try
        {
            invocation = method!.Invoke(repository, arguments);
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

    private static async Task<string> ReadScalarAsync(
        TestContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = context.Factory.CreateConnection();
        await connection.OpenAsync();
        return await ReadScalarAsync(connection, sql, parameters);
    }

    private static async Task<string> ReadScalarAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result) ?? string.Empty;
    }

    private static async Task<List<string>> ReadColumnAsync(
        DbConnection connection,
        string sql,
        int columnIndex)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(columnIndex));
        }

        return values;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(string root)
        {
            Provider = new DefaultAppPathProvider(new ServerStorageOptions { RootPath = root });
            new ServerStorageLayout(Provider).EnsureCreated();
            Factory = new SqliteConnectionFactory(Provider);
            Initializer = new SqliteDatabaseInitializer(Factory);
        }

        public DefaultAppPathProvider Provider { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteDatabaseInitializer Initializer { get; }

        public static async Task<TestContext> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VideoMonitorCentralCatalogRepositoryTests",
                Guid.NewGuid().ToString("N"));
            var context = new TestContext(root);
            await context.Initializer.InitializeAsync();
            return context;
        }

        public async Task SetGroupKindAsync(Guid id, string value)
        {
            await using var connection = Factory.CreateConnection();
            await connection.OpenAsync();
            var columns = await ReadColumnAsync(connection, "PRAGMA table_info(device_groups);", columnIndex: 1);
            if (!columns.Contains("group_kind", StringComparer.Ordinal))
            {
                await ExecuteAsync(connection, "ALTER TABLE device_groups ADD COLUMN group_kind TEXT NULL;");
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE device_groups SET group_kind = $kind WHERE id = $id;";
            var kindParameter = command.CreateParameter();
            kindParameter.ParameterName = "$kind";
            kindParameter.Value = value;
            command.Parameters.Add(kindParameter);
            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "$id";
            idParameter.Value = id.ToString("N");
            command.Parameters.Add(idParameter);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            await Task.Yield();
            try
            {
                if (Directory.Exists(Provider.RootDirectory))
                {
                    Directory.Delete(Provider.RootDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Microsoft.Data.Sqlite may retain a pooled native handle briefly.
            }
        }
    }

    private sealed class CountingSecretProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }
        public string? LastPurpose { get; private set; }

        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            ProtectCalls++;
            LastPurpose = purpose;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
            return Task.FromResult($"test-protected:{encoded}");
        }

        public Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            UnprotectCalls++;
            throw new InvalidOperationException("UnprotectAsync must not be called by catalog reads.");
        }

        public void ResetCounts()
        {
            ProtectCalls = 0;
            UnprotectCalls = 0;
            LastPurpose = null;
        }

        public void ResetUnprotectCalls() => UnprotectCalls = 0;
    }

    private sealed class FailingReplacementProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }

        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            ProtectCalls++;
            throw new CryptographicException("replacement protection failed");
        }

        public Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            UnprotectCalls++;
            throw new InvalidOperationException("UnprotectAsync must not be called by updates.");
        }
    }
}
