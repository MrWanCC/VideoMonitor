using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class DeviceManagementGroupTests
{
    [Fact]
    public void SelectGroup_FiltersDevicesToSelectedGroup()
    {
        var viewModel = CreateViewModel();
        var group = viewModel.Groups.Single(item => item.Name == "西401溜井");

        viewModel.SelectGroupCommand.Execute(group);

        Assert.Equal(3, viewModel.Devices.Count);
        Assert.All(viewModel.Devices, device => Assert.Equal(group.Id, device.GroupId));
    }

    [Theory]
    [InlineData("西401", 3)]
    [InlineData("192.168.17", 3)]
    [InlineData("192.168.17.6", 1)]
    public void SearchKeyword_FiltersCurrentGroupByNameOrIp(string keyword, int expected)
    {
        var viewModel = CreateViewModel("西401溜井");

        viewModel.SearchKeyword = keyword;

        Assert.Equal(expected, viewModel.Devices.Count);
    }

    [Fact]
    public void DeleteNonEmptyGroup_IsBlockedWithoutConfirmation()
    {
        var viewModel = CreateViewModel("西401溜井");
        var group = viewModel.SelectedGroup!;

        viewModel.DeleteGroupCommand.Execute(group);

        Assert.True(viewModel.IsDialogOpen);
        Assert.Equal(DeviceDialogMode.Information, viewModel.DialogMode);
        Assert.Equal("该分组下仍有设备，请先移动或删除设备。", viewModel.DialogMessage);
        Assert.Contains(group, viewModel.Groups);
    }

    [Fact]
    public void CancelRename_DoesNotChangeOriginalName()
    {
        var viewModel = CreateViewModel("西402溜井");
        var group = viewModel.SelectedGroup!;

        viewModel.BeginRenameGroupCommand.Execute(group);
        viewModel.EditingGroupName = "已修改但未保存";
        viewModel.CancelGroupEditCommand.Execute(null);

        Assert.Equal("西402溜井", group.Name);
    }

    [Fact]
    public void DeleteEmptyGroup_RequiresConfirmation()
    {
        var viewModel = CreateViewModel();
        var root = viewModel.Groups.Single(item => item.Name == "溜井监控");
        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "临时空分组";
        viewModel.CommitGroupEditCommand.Execute(null);
        var group = viewModel.Groups.Single(item => item.Name == "临时空分组");

        viewModel.DeleteGroupCommand.Execute(group);

        Assert.Equal(DeviceDialogMode.Confirmation, viewModel.DialogMode);
        Assert.Contains(group, viewModel.Groups);
        viewModel.ConfirmDialogCommand.Execute(null);
        Assert.DoesNotContain(group, viewModel.Groups);
    }

    [Fact]
    public void AddGroup_CommitCreatesNamedChildUnderRoot()
    {
        var viewModel = CreateViewModel();
        var root = viewModel.Groups.Single(group => group.Name == "溜井监控");

        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "东401溜井";
        viewModel.CommitGroupEditCommand.Execute(null);

        var added = viewModel.Groups.Single(group => group.Name == "东401溜井");
        Assert.Equal(root.Id, added.ParentId);
    }

    [Fact]
    public void EmptyCatalog_CanBeginFirstRootDraft()
    {
        var fixture = RootFixture.Empty();

        fixture.ViewModel.BeginAddRootCommand.Execute(null);

        Assert.True(fixture.ViewModel.IsRootEditorOpen);
        Assert.NotNull(fixture.ViewModel.EditingRootId);
        Assert.NotEqual(Guid.Empty, fixture.ViewModel.EditingRootId);
        Assert.Empty(fixture.ViewModel.RootEditName);
        Assert.Null(fixture.ViewModel.RootEditKind);
        Assert.True(fixture.ViewModel.CanEditRootKind);
        Assert.True(fixture.ViewModel.HasUnsavedDraft);
        Assert.Equal(0, fixture.Commands.WriteCount);
    }

    [Fact]
    public void CancelRootDraft_PerformsZeroWrites()
    {
        var fixture = RootFixture.Empty();

        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditName = "未提交分类";
        fixture.ViewModel.CancelRootEditCommand.Execute(null);

        Assert.Equal(0, fixture.Commands.WriteCount);
        Assert.False(fixture.ViewModel.IsRootEditorOpen);
        Assert.False(fixture.ViewModel.HasUnsavedDraft);
        Assert.Empty(fixture.ReadModel.GetGroups());
    }

    [Fact]
    public async Task NewRoot_RequiresName()
    {
        var fixture = RootFixture.Empty();

        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Commands.WriteCount);
        Assert.True(fixture.ViewModel.IsRootEditorOpen);
        Assert.True(fixture.ViewModel.HasUnsavedDraft);
    }

    [Fact]
    public async Task NewRoot_RequiresKind()
    {
        var fixture = RootFixture.Empty();

        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditName = "新分类";
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Commands.WriteCount);
        Assert.True(fixture.ViewModel.IsRootEditorOpen);
        Assert.True(fixture.ViewModel.HasUnsavedDraft);
    }

    [Fact]
    public async Task NewRoot_UsesStableGuidGeneratedBeforeSave()
    {
        var fixture = RootFixture.Empty();
        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        var id = fixture.ViewModel.EditingRootId;
        fixture.ViewModel.RootEditName = "新分类";
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;

        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(id, fixture.Commands.LastCreateGroup!.Id);
    }

    [Fact]
    public async Task NewRoot_UsesParentIdNull()
    {
        var fixture = RootFixture.Empty();
        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditName = "新分类";
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;

        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Null(fixture.Commands.LastCreateGroup!.ParentId);
    }

    [Fact]
    public async Task NewRoot_UsesNextRootSort()
    {
        var fixture = RootFixture.WithGroups(
            Root("A", sort: 2),
            Root("B", sort: 8),
            Group("Child", Guid.NewGuid(), null, sort: 99));
        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditName = "新分类";
        fixture.ViewModel.RootEditKind = MonitorGroupType.Tunnel;

        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(9, fixture.Commands.LastCreateGroup!.Sort);
    }

    [Fact]
    public async Task NewRoot_SavePerformsOneWrite()
    {
        var fixture = RootFixture.Empty();
        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditName = "新分类";
        fixture.ViewModel.RootEditKind = MonitorGroupType.UnloadingStation;

        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Commands.WriteCount);
        Assert.False(fixture.ViewModel.IsRootEditorOpen);
        Assert.False(fixture.ViewModel.HasUnsavedDraft);
    }

    [Fact]
    public async Task MappedRootKind_IsImmutable()
    {
        var root = Root("Mapped", kind: MonitorGroupType.Chute);
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditName = "Renamed";
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.CanEditRootKind);
        Assert.Equal(MonitorGroupType.Chute, fixture.Commands.LastUpdateGroup!.Kind);
    }

    [Fact]
    public async Task MappedRootKind_CannotBeChangedEvenIfDraftValueIsForced()
    {
        var root = Root("Mapped", kind: MonitorGroupType.Tunnel);
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(MonitorGroupType.Tunnel, fixture.Commands.LastUpdateGroup!.Kind);
    }

    [Fact]
    public async Task LegacyRootKind_MayBeAssignedOnce()
    {
        var root = Root("Legacy", kind: null);
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Commands.WriteCount);
        Assert.Equal(MonitorGroupType.Chute, fixture.Commands.LastUpdateGroup!.Kind);
    }

    [Fact]
    public async Task LegacyRootKind_IsLockedAfterAuthoritativeRefresh()
    {
        var root = Root("Legacy", kind: null);
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);
        fixture.ReadModel.ReplaceGroups(root with { Kind = MonitorGroupType.Chute });
        fixture.ReadModel.RaiseChanged();

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);

        Assert.False(fixture.ViewModel.CanEditRootKind);
        Assert.Equal(MonitorGroupType.Chute, fixture.ViewModel.RootEditKind);
    }

    [Fact]
    public async Task ExistingRootUpdate_PreservesSortAndEnabled()
    {
        var root = Root("Mapped", sort: 11, enabled: false, kind: MonitorGroupType.Chute);
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditName = "Renamed";
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        var request = fixture.Commands.LastUpdateGroup!;
        Assert.Equal(root.Id, fixture.Commands.LastUpdateId);
        Assert.Equal("Renamed", request.Name);
        Assert.Null(request.ParentId);
        Assert.Equal(root.Sort, request.Sort);
        Assert.Equal(root.Enabled, request.Enabled);
        Assert.Equal(root.Revision, request.ExpectedRevision);
        Assert.Equal(root.Kind, request.Kind);
    }

    [Fact]
    public async Task RootConflict_RetainsDraft()
    {
        var root = Root("Mapped", kind: MonitorGroupType.Chute);
        var fixture = RootFixture.WithGroups(root);
        fixture.Commands.NextFailure = new CatalogApiException(
            "GROUP_REVISION_CONFLICT",
            root.Revision + 1);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditName = "Unsubmitted";
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.IsRootEditorOpen);
        Assert.True(fixture.ViewModel.HasUnsavedDraft);
        Assert.Equal("Unsubmitted", fixture.ViewModel.RootEditName);
        Assert.Equal("配置已被更新，请刷新后重试。", fixture.ViewModel.RootEditError);
        Assert.False(fixture.ViewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task RootUncertain_RetainsDraftAndShowsSafeError()
    {
        var root = Root("Mapped", kind: MonitorGroupType.Chute);
        var fixture = RootFixture.WithGroups(root);
        fixture.Commands.NextFailure = new CatalogMutationUncertainException(
            "update-group",
            root.Id);

        fixture.ViewModel.BeginEditRootCommand.Execute(root.Id);
        fixture.ViewModel.RootEditName = "Unsubmitted";
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.IsRootEditorOpen);
        Assert.True(fixture.ViewModel.HasUnsavedDraft);
        Assert.Equal("Unsubmitted", fixture.ViewModel.RootEditName);
        Assert.Equal("操作结果暂无法确认，请刷新后检查。", fixture.ViewModel.RootEditError);
        Assert.False(fixture.ViewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task DeleteRoot_RoutesGuidAndExpectedRevision()
    {
        var root = Root("Root", revision: 4);
        var fixture = RootFixture.WithGroups(root);

        await fixture.ViewModel.DeleteRootCommand.ExecuteAsync(root.Id);

        Assert.Equal(root.Id, fixture.Commands.LastDeleteGroupId);
        Assert.Equal(root.Revision, fixture.Commands.LastDeleteGroupRevision);
        Assert.Equal(1, fixture.Commands.WriteCount);
    }

    [Fact]
    public async Task DeleteRootFailure_DoesNotRemoveAuthoritativeDto()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);
        fixture.Commands.NextFailure = new CatalogApiException("GROUP_NOT_EMPTY");

        await fixture.ViewModel.DeleteRootCommand.ExecuteAsync(root.Id);

        Assert.Contains(fixture.ViewModel.CatalogGroups, group => group.Id == root.Id);
        Assert.Equal("分组仍有设备或子项，无法删除。", fixture.ViewModel.RootEditError);
        Assert.False(fixture.ViewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task DeleteRootUncertain_DoesNotRemoveAuthoritativeDto()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);
        fixture.Commands.NextFailure = new CatalogMutationUncertainException(
            "delete-group",
            root.Id);

        await fixture.ViewModel.DeleteRootCommand.ExecuteAsync(root.Id);

        Assert.Contains(fixture.ViewModel.CatalogGroups, group => group.Id == root.Id);
        Assert.Equal("操作结果暂无法确认，请刷新后检查。", fixture.ViewModel.RootEditError);
        Assert.False(fixture.ViewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task DeleteRootFailure_WhenEditorClosed_RetainsVisibleRootErrorState()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);
        fixture.Commands.NextFailure = new CatalogApiException("GROUP_NOT_EMPTY");

        Assert.False(fixture.ViewModel.IsRootEditorOpen);

        await fixture.ViewModel.DeleteRootCommand.ExecuteAsync(root.Id);

        Assert.False(fixture.ViewModel.IsRootEditorOpen);
        Assert.Equal("分组仍有设备或子项，无法删除。", fixture.ViewModel.RootEditError);
        Assert.False(fixture.ViewModel.LastOperationSucceeded);
        Assert.Contains(fixture.ViewModel.CatalogGroups, group => group.Id == root.Id);
    }

    [Fact]
    public void LegacyMode_DisablesFormalRootManagement()
    {
        var data = MockDeviceData.Create();
        var viewModel = new DeviceManagementViewModel(
            new InMemoryDeviceCatalog(data.Groups, data.Devices));
        var root = viewModel.Groups.First(group => group.ParentId is null);

        Assert.False(viewModel.IsRootManagementAvailable);
        Assert.False(viewModel.BeginAddRootCommand.CanExecute(null));
        Assert.True(viewModel.BeginAddGroupCommand.CanExecute(root));
    }

    [Fact]
    public void CentralMode_EnablesRootManagement()
    {
        var root = Root("Root");
        var child = Group("Child", root.Id, null);
        var fixture = RootFixture.WithGroups(root, child);

        Assert.True(fixture.ViewModel.IsRootManagementAvailable);
        Assert.True(fixture.ViewModel.BeginAddRootCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.BeginEditRootCommand.CanExecute(root.Id));
        Assert.False(fixture.ViewModel.BeginEditRootCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.BeginEditRootCommand.CanExecute(Guid.NewGuid()));
        Assert.False(fixture.ViewModel.BeginEditRootCommand.CanExecute(child.Id));
    }

    [Fact]
    public void CentralNewChildDraft_MaterializesEditableTreeItemWithoutAuthoritativeInsert()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginAddGroupCommand.Execute(root);

        Assert.Empty(fixture.ViewModel.CatalogGroups.Where(group => group.ParentId == root.Id));
        Assert.Equal(0, fixture.Commands.WriteCount);
        Assert.True(fixture.ViewModel.HasUnsavedDraft);
        var rootSection = fixture.ViewModel.GroupSections.Single(section => section.CatalogGroup?.Id == root.Id);
        var draft = Assert.Single(rootSection.Children);
        Assert.True(draft.IsDraft);
        Assert.True(draft.IsEditing);
        Assert.Null(draft.ActiveGroup);
        Assert.Empty(draft.Name);
        Assert.Empty(draft.Children);
    }

    [Fact]
    public void CancelCentralNewChildDraft_RemovesProjectedEditorWithoutWrite()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginAddGroupCommand.Execute(root);
        Assert.Single(fixture.ViewModel.GroupSections.Single().Children);

        fixture.ViewModel.CancelGroupEditCommand.Execute(null);

        Assert.Equal(0, fixture.Commands.WriteCount);
        Assert.False(fixture.ViewModel.HasUnsavedDraft);
        Assert.Empty(fixture.ViewModel.CatalogGroups.Where(group => group.ParentId == root.Id));
        Assert.Empty(fixture.ViewModel.GroupSections.Single().Children);
    }

    [Fact]
    public async Task SaveCentralNewChildDraft_UsesStableGuidAndRootParent()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginAddGroupCommand.Execute(root);
        var draftId = fixture.ViewModel.EditingGroupId;
        fixture.ViewModel.EditingGroupName = "401";

        await fixture.ViewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Commands.WriteCount);
        Assert.Equal(draftId, fixture.Commands.LastCreateGroup!.Id);
        Assert.Equal(root.Id, fixture.Commands.LastCreateGroup.ParentId);
        Assert.Null(fixture.Commands.LastCreateGroup.Kind);
        Assert.False(fixture.ViewModel.HasUnsavedDraft);
    }

    [Fact]
    public async Task DuplicateGroupCommitForSameEditingGroupId_PerformsOneMutation()
    {
        var root = Root("Root");
        var commands = new RecordingCatalogCommandService(true)
        {
            CreateGroupRelease = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var fixture = RootFixture.WithGroups(root, commands);

        fixture.ViewModel.BeginAddGroupCommand.Execute(root);
        fixture.ViewModel.EditingGroupName = "401";

        var firstCommit = fixture.ViewModel.CommitGroupEditCommand.ExecuteAsync(null);
        await commands.CreateGroupEntered.Task;

        var duplicateCommit = fixture.ViewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.Equal(1, commands.WriteCount);
        commands.CreateGroupRelease.SetResult(null);
        await Task.WhenAll(firstCommit, duplicateCommit);

        Assert.Equal(1, commands.WriteCount);
    }

    [Fact]
    public async Task DuplicateRenameCommitForSameEditingGroupId_PerformsOneMutation()
    {
        var root = Root("Root");
        var child = Group("Child", root.Id, null);
        var commands = new RecordingCatalogCommandService(true)
        {
            UpdateGroupRelease = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var fixture = new RootFixture(
            new DeviceManagementReadModelStub([root, child], []),
            commands);

        fixture.ViewModel.BeginRenameGroupCommand.Execute(child);
        fixture.ViewModel.EditingGroupName = "Renamed";

        var firstCommit = fixture.ViewModel.CommitGroupEditCommand.ExecuteAsync(null);
        await commands.UpdateGroupEntered.Task;

        var duplicateCommit = fixture.ViewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.Equal(1, commands.WriteCount);
        commands.UpdateGroupRelease.SetResult(null);
        await Task.WhenAll(firstCommit, duplicateCommit);

        Assert.Equal(1, commands.WriteCount);
    }

    [Fact]
    public async Task GroupWriteFailure_UsesSafeChineseMessage()
    {
        var root = Root("Root");
        var commands = new RecordingCatalogCommandService(true)
        {
            NextFailure = new CatalogApiException("CATALOG_WRITE_FAILED")
        };
        var fixture = RootFixture.WithGroups(root, commands);

        fixture.ViewModel.BeginAddGroupCommand.Execute(root);
        fixture.ViewModel.EditingGroupName = "401";
        await fixture.ViewModel.CommitGroupEditCommand.ExecuteAsync(null);

        Assert.Equal("设备配置保存失败，请重试。", fixture.ViewModel.GroupEditError);
        Assert.DoesNotContain("CATALOG_WRITE_FAILED", fixture.ViewModel.GroupEditError);
        Assert.DoesNotContain("Catalog write failed", fixture.ViewModel.GroupEditError);
    }

    [Fact]
    public void LegacyInitialSelection_DoesNotPreferReservedDisplayName()
    {
        var root = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Sort = 0
        };
        var ordinary = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "普通分组",
            ParentId = root.Id,
            Sort = 1
        };
        var reserved = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "备用1",
            ParentId = root.Id,
            Sort = 99
        };

        var viewModel = new DeviceManagementViewModel(
            new InMemoryDeviceCatalog([root, ordinary, reserved], []));

        Assert.Equal(ordinary.Id, viewModel.SelectedGroup?.Id);
    }

    [Fact]
    public async Task LegacyDeleteSelectedGroup_FallbackDoesNotPreferReservedDisplayName()
    {
        var root = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Sort = 0
        };
        var target = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "待删除",
            ParentId = root.Id,
            Sort = 0
        };
        var ordinary = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "普通分组",
            ParentId = root.Id,
            Sort = 1
        };
        var reserved = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "备用1",
            ParentId = root.Id,
            Sort = 99
        };
        var viewModel = new DeviceManagementViewModel(
            new InMemoryDeviceCatalog([root, target, ordinary, reserved], []));

        viewModel.SelectGroupCommand.Execute(target);
        viewModel.DeleteGroupCommand.Execute(target);
        await viewModel.ConfirmDialogCommand.ExecuteAsync(null);

        Assert.Equal(ordinary.Id, viewModel.SelectedGroup?.Id);
    }

    [Fact]
    public void RootDraft_DoesNotClearUnsavedDeviceDraft()
    {
        var root = Root("Root");
        var device = Device(root.Id);
        var fixture = RootFixture.WithGroupsAndDevices([root], [device]);

        fixture.ViewModel.EditDeviceCommand.Execute(device);
        fixture.ViewModel.EditDraft.Name = "Unsubmitted device";
        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.CancelRootEditCommand.Execute(null);

        Assert.True(fixture.ViewModel.HasUnsavedDraft);
        fixture.ViewModel.CancelEditCommand.Execute(null);
        Assert.False(fixture.ViewModel.HasUnsavedDraft);
    }

    [Fact]
    public void RootDraft_DoesNotClearUnsavedChildDraft()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root);

        fixture.ViewModel.BeginAddGroupCommand.Execute(root);
        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.CancelRootEditCommand.Execute(null);

        Assert.True(fixture.ViewModel.HasUnsavedDraft);
        fixture.ViewModel.CancelGroupEditCommand.Execute(null);
        Assert.False(fixture.ViewModel.HasUnsavedDraft);
    }

    [Fact]
    public void RootIdentity_DoesNotDependOnDuplicateName()
    {
        var first = Root("Duplicate", sort: 1);
        var second = Root("Duplicate", sort: 2);
        var fixture = RootFixture.WithGroups(first, second);

        fixture.ViewModel.BeginEditRootCommand.Execute(second.Id);

        Assert.Equal(second.Id, fixture.ViewModel.EditingRootId);
        Assert.Equal(second.Name, fixture.ViewModel.RootEditName);
    }

    [Fact]
    public async Task Offline_DisablesRootWrites()
    {
        var root = Root("Root");
        var fixture = RootFixture.WithGroups(root, canWrite: false);

        fixture.ViewModel.BeginAddRootCommand.Execute(null);
        fixture.ViewModel.RootEditName = "Offline";
        fixture.ViewModel.RootEditKind = MonitorGroupType.Chute;

        Assert.False(fixture.ViewModel.SaveRootCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.DeleteRootCommand.CanExecute(root.Id));
        await fixture.ViewModel.SaveRootCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Commands.WriteCount);
    }

    private static DeviceManagementViewModel CreateViewModel(string? selectedGroup = null)
    {
        var data = MockDeviceData.Create();
        var viewModel = new DeviceManagementViewModel(
            new InMemoryDeviceCatalog(data.Groups, data.Devices));
        if (selectedGroup is not null)
        {
            viewModel.SelectGroupCommand.Execute(
                viewModel.Groups.Single(group => group.Name == selectedGroup));
        }

        return viewModel;
    }

    private static DeviceGroupDto Root(
        string name,
        int sort = 0,
        bool enabled = true,
        MonitorGroupType? kind = MonitorGroupType.Chute,
        long revision = 1,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, null, sort, enabled, kind, revision);

    private static DeviceGroupDto Group(
        string name,
        Guid parentId,
        MonitorGroupType? kind,
        int sort = 0) =>
        new(Guid.NewGuid(), name, parentId, sort, true, kind, 1);

    private static CameraDeviceDto Device(Guid groupId) =>
        new(
            Guid.NewGuid(),
            groupId,
            "Camera",
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
            1,
            []);

    private sealed class RootFixture
    {
        public RootFixture(
            DeviceManagementReadModelStub readModel,
            RecordingCatalogCommandService commands)
        {
            ReadModel = readModel;
            Commands = commands;
            ViewModel = new DeviceManagementViewModel(readModel, commands);
        }

        public DeviceManagementViewModel ViewModel { get; }
        public DeviceManagementReadModelStub ReadModel { get; }
        public RecordingCatalogCommandService Commands { get; }

        public static RootFixture Empty(bool canWrite = true) =>
            Create([], [], canWrite);

        public static RootFixture WithGroups(
            params DeviceGroupDto[] groups) =>
            Create(groups, [], true);

        public static RootFixture WithGroups(
            DeviceGroupDto root,
            RecordingCatalogCommandService commands) =>
            new(
                new DeviceManagementReadModelStub([root], []),
                commands);

        public static RootFixture WithGroups(
            DeviceGroupDto root,
            bool canWrite) =>
            Create([root], [], canWrite);

        public static RootFixture WithGroupsAndDevices(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto> devices,
            bool canWrite = true) =>
            Create(groups, devices, canWrite);

        private static RootFixture Create(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto> devices,
            bool canWrite) =>
            new(
                new DeviceManagementReadModelStub(groups, devices),
                new RecordingCatalogCommandService(canWrite));
    }

    private sealed class DeviceManagementReadModelStub : IDeviceCatalogReadModel
    {
        private IReadOnlyList<DeviceGroupDto> groups;
        private readonly IReadOnlyList<CameraDeviceDto> devices;

        public DeviceManagementReadModelStub(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto> devices)
        {
            this.groups = groups;
            this.devices = devices;
        }

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            devices.Where(device => device.GroupId == groupId).ToArray();

        public CameraDeviceDto? GetDevice(Guid id) =>
            devices.FirstOrDefault(device => device.Id == id);

        public void ReplaceGroups(params DeviceGroupDto[] nextGroups) => groups = nextGroups;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RecordingCatalogCommandService : IDeviceCatalogCommandService
    {
        public RecordingCatalogCommandService(bool canWrite) => CanWrite = canWrite;

        public bool CanWrite { get; }
        public int WriteCount { get; private set; }
        public CreateGroupRequest? LastCreateGroup { get; private set; }
        public UpdateGroupRequest? LastUpdateGroup { get; private set; }
        public Guid? LastUpdateId { get; private set; }
        public Guid? LastDeleteGroupId { get; private set; }
        public long? LastDeleteGroupRevision { get; private set; }
        public Exception? NextFailure { get; set; }
        public TaskCompletionSource<object?> CreateGroupEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?>? CreateGroupRelease { get; set; }
        public TaskCompletionSource<object?> UpdateGroupEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?>? UpdateGroupRelease { get; set; }

        public event EventHandler? AvailabilityChanged;

        public void RaiseAvailabilityChanged() =>
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);

        public async Task<DeviceGroupDto> CreateGroupAsync(
            CreateGroupRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            LastCreateGroup = request;
            CreateGroupEntered.TrySetResult(null);
            if (CreateGroupRelease is not null)
            {
                await CreateGroupRelease.Task;
            }

            if (NextFailure is not null)
            {
                throw NextFailure;
            }

            return new DeviceGroupDto(
                request.Id,
                request.Name,
                request.ParentId,
                request.Sort,
                request.Enabled,
                request.Kind,
                1);
        }

        public async Task<DeviceGroupDto> UpdateGroupAsync(
            Guid id,
            UpdateGroupRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            LastUpdateId = id;
            LastUpdateGroup = request;
            UpdateGroupEntered.TrySetResult(null);
            if (UpdateGroupRelease is not null)
            {
                await UpdateGroupRelease.Task;
            }

            if (NextFailure is not null)
            {
                throw NextFailure;
            }

            return new DeviceGroupDto(
                id,
                request.Name,
                request.ParentId,
                request.Sort,
                request.Enabled,
                request.Kind,
                request.ExpectedRevision + 1);
        }

        public Task DeleteGroupAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            LastDeleteGroupId = id;
            LastDeleteGroupRevision = expectedRevision;
            return NextFailure is not null
                ? Task.FromException(NextFailure)
                : Task.CompletedTask;
        }

        public Task<CameraDeviceDto> CreateDeviceAsync(
            CreateDeviceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraDeviceDto>(null!);

        public Task<CameraDeviceDto> UpdateDeviceAsync(
            Guid id,
            UpdateDeviceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraDeviceDto>(null!);

        public Task DeleteDeviceAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
