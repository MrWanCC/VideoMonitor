using System.Globalization;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Converters;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class DeviceManagementDraftTests
{
    [Fact]
    public void CentralCatalog_VisibleSurfaceUsesDtoData()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var child = Group("Child", root.Id, null);
        var device = ExistingDevice("DTO device", groupId: child.Id);
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device, [root, child]),
            new FakeCatalogCommandService());

        Assert.Same(root, Assert.Single(viewModel.ActiveGroupSections).CatalogGroup);
        Assert.Same(device, Assert.Single(viewModel.ActiveDevices));
        Assert.Same(child, Assert.Single(viewModel.ActiveEditableGroups));
    }

    [Fact]
    public void CentralAddChild_CancelPerformsZeroWrites()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root]),
            commands);

        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "Child";
        viewModel.CancelGroupEditCommand.Execute(null);

        Assert.Equal(0, commands.WriteCount);
        Assert.Empty(commands.CreatedGroups);
    }

    [Fact]
    public void CentralAddChild_MarksUnsavedUntilCancel()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root]),
            commands);

        viewModel.BeginAddGroupCommand.Execute(root);

        Assert.True(viewModel.HasUnsavedDraft);

        viewModel.CancelGroupEditCommand.Execute(null);

        Assert.False(viewModel.HasUnsavedDraft);
        Assert.Equal(0, commands.WriteCount);
    }

    [Fact]
    public async Task CentralAddChild_SavePerformsOneWrite()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root]),
            commands);

        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "Child";
        await viewModel.CommitGroupEditCommand.ExecuteAsync(null);

        var request = Assert.Single(commands.CreatedGroups);
        Assert.Equal(root.Id, request.ParentId);
        Assert.Null(request.Kind);
        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(1, commands.WriteCount);
    }

    [Fact]
    public async Task CentralRenameChild_MarksUnsavedUntilSaveOrCancel()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var child = Group("Child", root.Id, null);
        var commands = new FakeCatalogCommandService
        {
            UpdateGroupResult = child with { Name = "Renamed", Revision = 2 }
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root, child]),
            commands);

        viewModel.BeginRenameGroupCommand.Execute(child);
        Assert.True(viewModel.HasUnsavedDraft);

        viewModel.CancelGroupEditCommand.Execute(null);
        Assert.False(viewModel.HasUnsavedDraft);

        viewModel.BeginRenameGroupCommand.Execute(child);
        viewModel.EditingGroupName = "Renamed";
        await viewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasUnsavedDraft);
        Assert.Equal(1, commands.WriteCount);
    }

    [Fact]
    public void CancelGroupEdit_DoesNotClearUnsavedDeviceDraft()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var device = ExistingDevice(groupId: Guid.NewGuid());
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device, [root]),
            commands);

        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Name = "Unsubmitted device";
        viewModel.BeginAddGroupCommand.Execute(root);

        viewModel.CancelGroupEditCommand.Execute(null);

        Assert.True(viewModel.HasUnsavedDraft);

        viewModel.CancelEditCommand.Execute(null);

        Assert.False(viewModel.HasUnsavedDraft);
    }

    [Fact]
    public async Task CentralGroupConflict_RetainsDraft()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var child = Group("Child", root.Id, null);
        var commands = new FakeCatalogCommandService
        {
            NextGroupFailure = new CatalogApiException(
                "GROUP_REVISION_CONFLICT",
                7)
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root, child]),
            commands);

        viewModel.BeginRenameGroupCommand.Execute(child);
        viewModel.EditingGroupName = "Changed";
        await viewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUnsavedDraft);
        Assert.Equal("Changed", viewModel.EditingGroupName);
        Assert.Equal("GROUP_REVISION_CONFLICT", viewModel.GroupEditError);
        Assert.False(viewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task CentralGroupUncertain_RetainsDraftAndSetsSafeError()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var child = Group("Child", root.Id, null);
        var commands = new FakeCatalogCommandService
        {
            NextGroupFailure = new CatalogMutationUncertainException(
                "update-group",
                child.Id)
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root, child]),
            commands);

        viewModel.BeginRenameGroupCommand.Execute(child);
        viewModel.EditingGroupName = "Changed";
        await viewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUnsavedDraft);
        Assert.Equal("Changed", viewModel.EditingGroupName);
        Assert.Equal("CATALOG_MUTATION_UNCERTAIN", viewModel.GroupEditError);
        Assert.False(viewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task CentralDeleteGroupUncertain_SetsSafeErrorWithoutRemovingCachedDto()
    {
        var root = Group("Root", null, MonitorGroupType.Chute);
        var child = Group("Child", root.Id, null);
        var commands = new FakeCatalogCommandService
        {
            NextDeleteGroupFailure = new CatalogMutationUncertainException(
                "delete-group",
                child.Id)
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(null, [root, child]),
            commands);

        viewModel.DeleteGroupCommand.Execute(child);
        await viewModel.ConfirmDialogCommand.ExecuteAsync(null);

        Assert.Equal("CATALOG_MUTATION_UNCERTAIN", viewModel.GroupEditError);
        Assert.Contains(viewModel.CatalogGroups, group => group.Id == child.Id);
        Assert.False(viewModel.HasUnsavedDraft);
        Assert.False(viewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task CentralDeleteDeviceUncertain_SetsSafeErrorWithoutRemovingCachedDto()
    {
        var device = ExistingDevice();
        var commands = new FakeCatalogCommandService
        {
            NextDeleteDeviceFailure = new CatalogMutationUncertainException(
                "delete-device",
                device.Id)
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);

        viewModel.DeleteDeviceCommand.Execute(device);
        await viewModel.ConfirmDialogCommand.ExecuteAsync(null);

        Assert.Equal("CATALOG_MUTATION_UNCERTAIN", viewModel.OperationErrorCode);
        Assert.Contains(viewModel.CatalogDevices, item => item.Id == device.Id);
        Assert.False(viewModel.HasUnsavedDraft);
        Assert.False(viewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task CancelEdit_ClearsUnsavedDraftAndPerformsZeroWrites()
    {
        var device = ExistingDevice();
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);

        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Name = "Unsubmitted";
        Assert.True(viewModel.HasUnsavedDraft);

        viewModel.CancelEditCommand.Execute(null);
        await Task.CompletedTask;

        Assert.Equal(0, commands.WriteCount);
        Assert.False(viewModel.HasUnsavedDraft);
        Assert.False(viewModel.IsEditPanelOpen);
        Assert.Empty(viewModel.EditDraft.Name);
    }

    [Fact]
    public async Task WhitespacePassword_MapsToNoPasswordChange()
    {
        var device = ExistingDevice();
        var commands = new FakeCatalogCommandService
        {
            UpdateResult = device
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);

        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Password = "   ";
        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.Null(commands.LastUpdate!.NewPassword);
    }

    [Fact]
    public async Task ChannelIds_ArePreservedOnEdit()
    {
        var device = ExistingDeviceWithChannels();
        var commands = new FakeCatalogCommandService { UpdateResult = device };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);

        viewModel.EditDeviceCommand.Execute(device);
        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        var channels = commands.LastUpdate!.Channels;
        Assert.Equal(device.Channels.Select(channel => channel.Id), channels.Select(channel => channel.Id));
    }

    [Fact]
    public async Task UneditedChannels_ArePreserved()
    {
        var device = ExistingDeviceWithChannels();
        var commands = new FakeCatalogCommandService { UpdateResult = device };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);

        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Name = "Changed";
        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        var second = Assert.Single(commands.LastUpdate!.Channels.Skip(1));
        var expected = device.Channels[1];
        Assert.Equal(expected.Id, second.Id);
        Assert.Equal(expected.ChannelNo, second.ChannelNo);
        Assert.Equal(expected.ChannelName, second.ChannelName);
        Assert.Equal(expected.StreamType, second.StreamType);
        Assert.Equal(expected.Enabled, second.Enabled);
    }

    [Fact]
    public void FirstChannelConverters_AcceptCameraDeviceDto()
    {
        var device = ExistingDeviceWithChannels();
        var channelNo = new FirstChannelNoConverter().Convert(
            device,
            typeof(string),
            null!,
            CultureInfo.InvariantCulture);
        var stream = new FirstChannelStreamConverter().Convert(
            device,
            typeof(string),
            null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("1", channelNo);
        Assert.Equal("主码流", stream);
    }

    [Fact]
    public void CentralCatalogDeviceStatus_IsUnknownUntilProbed()
    {
        var converter = new DeviceCatalogStatusToTextConverter();
        var value = converter.Convert(
            ExistingDevice(),
            typeof(string),
            null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("未探测", value);
    }

    [Fact]
    public void DeviceCatalogStatusConverter_PreservesLegacyCameraDeviceStatus()
    {
        var converter = new DeviceCatalogStatusToTextConverter();

        var online = new CameraDevice { Status = CameraStatus.Online };
        var warning = new CameraDevice { Status = CameraStatus.Warning };

        Assert.Equal(
            "在线",
            converter.Convert(
                online,
                typeof(string),
                null!,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            "异常",
            converter.Convert(
                warning,
                typeof(string),
                null!,
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Offline_DisablesCentralWrites()
    {
        var commands = new FakeCatalogCommandService { CanWriteValue = false };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            commands);

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.False(viewModel.SaveDeviceCommand.CanExecute(null));
        Assert.Equal(0, commands.WriteCount);
    }

    [Fact]
    public void ExistingPassword_IsNeverLoadedIntoDraft()
    {
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            new FakeCatalogCommandService());

        Assert.Empty(viewModel.EditDraft.Password);
    }

    [Fact]
    public void ExistingDeviceEditor_StartsWithBlankPassword()
    {
        var device = ExistingDevice();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            new FakeCatalogCommandService());

        viewModel.EditDeviceCommand.Execute(device);

        Assert.Empty(viewModel.EditDraft.Password);
    }

    [Fact]
    public async Task BlankPassword_MapsToNoPasswordChange()
    {
        var device = ExistingDevice();
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);
        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Password = string.Empty;

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.Null(commands.LastUpdate!.NewPassword);
    }

    [Fact]
    public async Task Conflict_RetainsDraft()
    {
        var device = ExistingDevice();
        var commands = new FakeCatalogCommandService
        {
            NextFailure = new CatalogApiException("DEVICE_REVISION_CONFLICT", 9)
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);
        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Name = "Unsubmitted";

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUnsavedDraft);
        Assert.Equal("Unsubmitted", viewModel.EditDraft.Name);
        Assert.Equal("DEVICE_REVISION_CONFLICT", viewModel.OperationErrorCode);
        Assert.False(viewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task AmbiguousUpdate_SetsSafeErrorAndRetainsDraft()
    {
        var device = ExistingDevice();
        var commands = new FakeCatalogCommandService
        {
            NextFailure = new CatalogMutationUncertainException(
                "update-device",
                Guid.NewGuid())
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(device),
            commands);
        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.Password = "new-secret";

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUnsavedDraft);
        Assert.True(viewModel.HasOperationError);
        Assert.False(viewModel.LastOperationSucceeded);
        Assert.Equal("CATALOG_MUTATION_UNCERTAIN", viewModel.OperationErrorCode);
        Assert.DoesNotContain("new-secret", viewModel.OperationError ?? string.Empty);
    }

    private static DeviceGroupDto Group(
        string name,
        Guid? parentId,
        MonitorGroupType? kind) =>
        new(Guid.NewGuid(), name, parentId, 0, true, kind, 1);

    private static CameraDeviceDto ExistingDevice(
        string name = "Camera",
        Guid? id = null,
        Guid? groupId = null) =>
        new(
            id ?? Guid.NewGuid(),
            groupId ?? Guid.NewGuid(),
            name,
            "192.0.2.10",
            8000,
            554,
            "user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            8,
            []);

    private static CameraDeviceDto ExistingDeviceWithChannels()
    {
        var device = ExistingDevice();
        return device with
        {
            Channels =
            [
                new CameraChannelDto(
                    Guid.NewGuid(),
                    device.Id,
                    1,
                    "Main",
                    StreamType.Main,
                    true),
                new CameraChannelDto(
                    Guid.NewGuid(),
                    device.Id,
                    1,
                    "Sub",
                    StreamType.Sub,
                    false)
            ]
        };
    }

    private sealed class DeviceReadModelStub : IDeviceCatalogReadModel
    {
        private readonly CameraDeviceDto? device;
        private readonly IReadOnlyList<DeviceGroupDto> groups;

        public DeviceReadModelStub(CameraDeviceDto device)
            : this(device, [])
        {
        }

        public DeviceReadModelStub(
            CameraDeviceDto? device,
            IReadOnlyList<DeviceGroupDto> groups)
        {
            this.device = device;
            this.groups = groups;
        }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            device is null ? [] : [device];

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            device?.Id == deviceId ? device : null;
    }

    private sealed class FakeCatalogCommandService : IDeviceCatalogCommandService
    {
        public UpdateDeviceRequest? LastUpdate { get; private set; }

        public Exception? NextFailure { get; init; }

        public Exception? NextGroupFailure { get; init; }

        public Exception? NextDeleteGroupFailure { get; init; }

        public Exception? NextDeleteDeviceFailure { get; init; }

        public bool CanWriteValue { get; init; } = true;

        public CameraDeviceDto? UpdateResult { get; init; }

        public DeviceGroupDto? UpdateGroupResult { get; init; }

        public List<CreateGroupRequest> CreatedGroups { get; } = [];

        public int WriteCount { get; private set; }

        public bool CanWrite => CanWriteValue;

        public event EventHandler? AvailabilityChanged
        {
            add { }
            remove { }
        }

        public Task<DeviceGroupDto> CreateGroupAsync(
            CreateGroupRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            CreatedGroups.Add(request);
            return Task.FromResult(new DeviceGroupDto(
                request.Id,
                request.Name,
                request.ParentId,
                request.Sort,
                request.Enabled,
                request.Kind,
                1));
        }

        public Task<DeviceGroupDto> UpdateGroupAsync(
            Guid id,
            UpdateGroupRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return NextGroupFailure is not null
                ? Task.FromException<DeviceGroupDto>(NextGroupFailure)
                : Task.FromResult(UpdateGroupResult!);
        }

        public Task DeleteGroupAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return NextDeleteGroupFailure is not null
                ? Task.FromException(NextDeleteGroupFailure)
                : Task.CompletedTask;
        }

        public Task<CameraDeviceDto> CreateDeviceAsync(
            CreateDeviceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraDeviceDto>(null!);

        public Task<CameraDeviceDto> UpdateDeviceAsync(
            Guid id,
            UpdateDeviceRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            LastUpdate = request;
            return NextFailure is null
                ? Task.FromResult(UpdateResult!)
                : Task.FromException<CameraDeviceDto>(NextFailure);
        }

        public Task DeleteDeviceAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return NextDeleteDeviceFailure is not null
                ? Task.FromException(NextDeleteDeviceFailure)
                : Task.CompletedTask;
        }
    }
}
