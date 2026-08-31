using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class DeviceManagementViewModel : ObservableObject
{
    private readonly IDeviceCatalog catalog;
    private Guid? pendingNewGroupId;
    private Func<Task>? pendingDialogAction;
    private DeviceGroup? selectedGroup;
    private string searchKeyword = string.Empty;
    private Guid? editingGroupId;
    private string editingGroupName = string.Empty;
    private string groupEditError = string.Empty;
    private bool isDialogOpen;
    private DeviceDialogMode dialogMode;
    private string dialogMessage = string.Empty;
    private CameraDevice? selectedDevice;
    private bool isEditPanelOpen;
    private bool isEditing;
    private string validationMessage = string.Empty;
    private readonly IDeviceCatalogReadModel? readModel;
    private readonly IDeviceCatalogCommandService? commandService;
    private readonly bool centralMode;
    private DeviceGroupDto? selectedCatalogGroup;
    private Guid? editingCatalogParentId;
    private bool editingCatalogGroupIsNew;
    private long editingCatalogGroupRevision;
    private CameraDeviceDto? editingCatalogDevice;
    private bool suppressDraftTracking;
    private bool isSaving;
    private bool isServerAvailable;
    private bool hasUnsavedDraft;
    private string? operationErrorCode;
    private string operationError = string.Empty;
    private bool lastOperationSucceeded;

    public DeviceManagementViewModel(IDeviceCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        readModel = new LegacyDeviceCatalogReadModel(this.catalog);
        commandService = new LegacyDeviceCatalogCommandService(this.catalog);
        Groups = [];
        Devices = [];
        GroupSections = [];
        CatalogGroups = [];
        CatalogDevices = [];
        EditDraft = new DeviceEditDraftViewModel();

        SelectGroupCommand = new RelayCommand<object?>(SelectGroup);
        BeginAddGroupCommand = new RelayCommand<object?>(BeginAddGroup);
        BeginRenameGroupCommand = new RelayCommand<object?>(BeginRenameGroup);
        CommitGroupEditCommand = new AsyncRelayCommand(
            CommitGroupEditAsync,
            CanCommitGroupEdit);
        CancelGroupEditCommand = new RelayCommand(CancelGroupEdit);
        DeleteGroupCommand = new RelayCommand<object?>(
            DeleteGroup,
            _ => !centralMode || IsServerAvailable);
        ConfirmDialogCommand = new AsyncRelayCommand(ConfirmDialogAsync);
        CancelDialogCommand = new RelayCommand(ClearDialog);
        AddDeviceCommand = new RelayCommand(AddDevice);
        EditDeviceCommand = new RelayCommand<object?>(EditDevice);
        DeleteDeviceCommand = new RelayCommand<object?>(
            DeleteDevice,
            _ => !centralMode || IsServerAvailable);
        SaveDeviceCommand = new AsyncRelayCommand(SaveDeviceAsync, CanSaveDevice);
        CancelEditCommand = new RelayCommand(CancelEdit);

        catalog.Changed += OnCatalogChanged;
        EditDraft.PropertyChanged += OnEditDraftPropertyChanged;
        RefreshCatalogView();
    }

    public DeviceManagementViewModel(
        IDeviceCatalogReadModel catalog,
        IDeviceCatalogCommandService commands)
    {
        readModel = catalog ?? throw new ArgumentNullException(nameof(catalog));
        commandService = commands ?? throw new ArgumentNullException(nameof(commands));
        this.catalog = null!;
        centralMode = true;
        Groups = [];
        Devices = [];
        GroupSections = [];
        CatalogGroups = [];
        CatalogDevices = [];
        EditDraft = new DeviceEditDraftViewModel();

        SelectGroupCommand = new RelayCommand<object?>(SelectGroup);
        BeginAddGroupCommand = new RelayCommand<object?>(BeginAddGroup);
        BeginRenameGroupCommand = new RelayCommand<object?>(BeginRenameGroup);
        CommitGroupEditCommand = new AsyncRelayCommand(
            CommitGroupEditAsync,
            CanCommitGroupEdit);
        CancelGroupEditCommand = new RelayCommand(CancelGroupEdit);
        DeleteGroupCommand = new RelayCommand<object?>(
            DeleteGroup,
            _ => !centralMode || IsServerAvailable);
        ConfirmDialogCommand = new AsyncRelayCommand(ConfirmDialogAsync);
        CancelDialogCommand = new RelayCommand(ClearDialog);
        AddDeviceCommand = new RelayCommand(AddDevice);
        EditDeviceCommand = new RelayCommand<object?>(EditDevice);
        DeleteDeviceCommand = new RelayCommand<object?>(
            DeleteDevice,
            _ => !centralMode || IsServerAvailable);
        SaveDeviceCommand = new AsyncRelayCommand(SaveDeviceAsync, CanSaveDevice);
        CancelEditCommand = new RelayCommand(CancelEdit);

        readModel.Changed += OnReadModelChanged;
        commandService.AvailabilityChanged += OnCommandAvailabilityChanged;
        EditDraft.PropertyChanged += OnEditDraftPropertyChanged;
        RefreshCentralCatalogView();
    }

    public ObservableCollection<DeviceGroup> Groups { get; }

    public ObservableCollection<DeviceGroupTreeItemViewModel> GroupSections { get; }

    public ObservableCollection<DeviceGroupTreeItemViewModel> CatalogGroupSections => GroupSections;

    public ObservableCollection<CameraDevice> Devices { get; }

    public ObservableCollection<DeviceGroupDto> CatalogGroups { get; }

    public ObservableCollection<CameraDeviceDto> CatalogDevices { get; }

    public DeviceEditDraftViewModel EditDraft { get; }

    public IEnumerable<DeviceGroup> EditableGroups => Groups
        .Where(group => group.ParentId is not null && group.Enabled)
        .OrderBy(group => Groups.First(root => root.Id == group.ParentId).Sort)
        .ThenBy(group => group.Sort);

    public IEnumerable<DeviceGroupDto> EditableCatalogGroups => CatalogGroups
        .Where(group => group.ParentId is not null && group.Enabled)
        .OrderBy(group => group.Sort)
        .ThenBy(group => group.Id);

    public ObservableCollection<DeviceGroupTreeItemViewModel> ActiveGroupSections => GroupSections;

    public IEnumerable<object> ActiveDevices => centralMode
        ? CatalogDevices
            .Where(device => selectedCatalogGroup?.Id == device.GroupId)
            .Where(MatchesSearch)
            .Cast<object>()
        : Devices.Cast<object>();

    public IEnumerable<object> ActiveEditableGroups => centralMode
        ? EditableCatalogGroups.Cast<object>()
        : EditableGroups.Cast<object>();

    public string ActiveSelectedGroupName => centralMode
        ? selectedCatalogGroup?.Name ?? "未选择"
        : selectedGroup?.Name ?? "未选择";

    public IReadOnlyList<StreamType> StreamTypes { get; } = Enum.GetValues<StreamType>();

    public IReadOnlyList<TransportMode> TransportModes { get; } = Enum.GetValues<TransportMode>();

    public IRelayCommand<object?> SelectGroupCommand { get; }

    public IRelayCommand<object?> BeginAddGroupCommand { get; }

    public IRelayCommand<object?> BeginRenameGroupCommand { get; }

    public IRelayCommand<object?> AddGroupCommand => BeginAddGroupCommand;

    public IRelayCommand<object?> RenameGroupCommand => BeginRenameGroupCommand;

    public IAsyncRelayCommand CommitGroupEditCommand { get; }

    public IRelayCommand CancelGroupEditCommand { get; }

    public IRelayCommand<object?> DeleteGroupCommand { get; }

    public IAsyncRelayCommand ConfirmDialogCommand { get; }

    public IRelayCommand CancelDialogCommand { get; }

    public IRelayCommand AddDeviceCommand { get; }

    public IRelayCommand<object?> EditDeviceCommand { get; }

    public IRelayCommand<object?> DeleteDeviceCommand { get; }

    public IAsyncRelayCommand SaveDeviceCommand { get; }

    public IRelayCommand CancelEditCommand { get; }

    public bool IsSaving
    {
        get => isSaving;
        private set
        {
            if (SetProperty(ref isSaving, value))
            {
                ((AsyncRelayCommand)SaveDeviceCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsServerAvailable
    {
        get => isServerAvailable;
        private set
        {
            if (SetProperty(ref isServerAvailable, value))
            {
                ((AsyncRelayCommand)SaveDeviceCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CommitGroupEditCommand).NotifyCanExecuteChanged();
                ((RelayCommand<object?>)DeleteGroupCommand).NotifyCanExecuteChanged();
                ((RelayCommand<object?>)DeleteDeviceCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasUnsavedDraft
    {
        get => hasUnsavedDraft;
        private set => SetProperty(ref hasUnsavedDraft, value);
    }

    public string? OperationErrorCode
    {
        get => operationErrorCode;
        private set
        {
            if (SetProperty(ref operationErrorCode, value))
            {
                OnPropertyChanged(nameof(HasOperationError));
            }
        }
    }

    public string OperationError
    {
        get => operationError;
        private set
        {
            if (SetProperty(ref operationError, value))
            {
                OnPropertyChanged(nameof(HasOperationError));
            }
        }
    }

    public bool HasOperationError => !string.IsNullOrEmpty(OperationErrorCode);

    public bool LastOperationSucceeded
    {
        get => lastOperationSucceeded;
        private set => SetProperty(ref lastOperationSucceeded, value);
    }

    public DeviceGroup? SelectedGroup
    {
        get => selectedGroup;
        private set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                RebuildGroupSections();
                RefreshDevices();
            }
        }
    }

    public string SearchKeyword
    {
        get => searchKeyword;
        set
        {
            if (SetProperty(ref searchKeyword, value ?? string.Empty))
            {
                if (centralMode)
                {
                    OnPropertyChanged(nameof(ActiveDevices));
                    return;
                }

                RefreshDevices();
            }
        }
    }

    public Guid? EditingGroupId
    {
        get => editingGroupId;
        private set
        {
            if (SetProperty(ref editingGroupId, value))
            {
                ((AsyncRelayCommand)CommitGroupEditCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string EditingGroupName
    {
        get => editingGroupName;
        set => SetProperty(ref editingGroupName, value ?? string.Empty);
    }

    public string GroupEditError
    {
        get => groupEditError;
        private set => SetProperty(ref groupEditError, value);
    }

    public bool IsDialogOpen
    {
        get => isDialogOpen;
        private set => SetProperty(ref isDialogOpen, value);
    }

    public DeviceDialogMode DialogMode
    {
        get => dialogMode;
        private set => SetProperty(ref dialogMode, value);
    }

    public string DialogMessage
    {
        get => dialogMessage;
        private set => SetProperty(ref dialogMessage, value);
    }

    public CameraDevice? SelectedDevice
    {
        get => selectedDevice;
        private set => SetProperty(ref selectedDevice, value);
    }

    public bool IsEditPanelOpen
    {
        get => isEditPanelOpen;
        private set => SetProperty(ref isEditPanelOpen, value);
    }

    public bool IsEditing
    {
        get => isEditing;
        private set
        {
            if (SetProperty(ref isEditing, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
            }
        }
    }

    public string EditorTitle => IsEditing
        && (centralMode ? editingCatalogDevice?.Name : SelectedDevice?.Name) is { } name
        ? $"编辑设备：{name}"
        : "新增设备";

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    private void SelectGroup(object? value)
    {
        if (centralMode)
        {
            if (value is DeviceGroupDto group && group.ParentId is not null)
            {
                selectedCatalogGroup = group;
                OnPropertyChanged(nameof(ActiveSelectedGroupName));
                OnPropertyChanged(nameof(ActiveDevices));
                RefreshCentralGroupSections();
            }

            return;
        }

            if (value is DeviceGroup legacyGroup && legacyGroup.ParentId is not null)
            {
                SelectedGroup = legacyGroup;
        }
    }

    private bool MatchesSearch(CameraDeviceDto device)
    {
        var keyword = SearchKeyword.Trim();
        return keyword.Length == 0
            || device.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || device.IpAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        if (SelectedGroup is null)
        {
            return;
        }

        var keyword = SearchKeyword.Trim();
        foreach (var device in catalog.GetDevices(SelectedGroup.Id).Where(device =>
                     device.GroupId == SelectedGroup.Id
                     && (keyword.Length == 0
                         || device.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || device.IpAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
        {
            Devices.Add(device);
        }
    }

    private void BeginAddGroup(object? value)
    {
        if (centralMode)
        {
            BeginAddCatalogChild(value as DeviceGroupDto);
            return;
        }

        BeginAddLegacyGroup(value as DeviceGroup);
    }

    private void BeginAddLegacyGroup(DeviceGroup? root)
    {
        if (root is null || root.ParentId is not null)
        {
            return;
        }

        CancelGroupEdit();
        var group = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            ParentId = root.Id,
            Sort = Groups.Where(item => item.ParentId == root.Id).Select(item => item.Sort).DefaultIfEmpty().Max() + 1,
            Enabled = true
        };
        catalog.AddGroup(group);
        pendingNewGroupId = group.Id;
        EditingGroupId = group.Id;
        EditingGroupName = string.Empty;
        GroupEditError = string.Empty;
        RebuildGroupSections();
    }

    private void BeginAddCatalogChild(DeviceGroupDto? root)
    {
        if (root is null || root.ParentId is not null || !root.Kind.HasValue)
        {
            return;
        }

        CancelGroupEdit();
        EditingGroupId = Guid.NewGuid();
        editingCatalogParentId = root.Id;
        editingCatalogGroupIsNew = true;
        editingCatalogGroupRevision = 0;
        EditingGroupName = string.Empty;
        GroupEditError = string.Empty;
        RefreshCentralGroupSections();
    }

    private void BeginRenameGroup(object? value)
    {
        if (centralMode)
        {
            BeginRenameCatalogChild(value as DeviceGroupDto);
            return;
        }

        BeginRenameLegacyGroup(value as DeviceGroup);
    }

    private void BeginRenameLegacyGroup(DeviceGroup? group)
    {
        if (group?.ParentId is null)
        {
            return;
        }

        CancelGroupEdit();
        EditingGroupId = group.Id;
        EditingGroupName = group.Name;
        GroupEditError = string.Empty;
        RebuildGroupSections();
    }

    private void BeginRenameCatalogChild(DeviceGroupDto? group)
    {
        if (group is null || group.ParentId is null)
        {
            return;
        }

        CancelGroupEdit();
        EditingGroupId = group.Id;
        editingCatalogParentId = group.ParentId;
        editingCatalogGroupIsNew = false;
        editingCatalogGroupRevision = group.Revision;
        EditingGroupName = group.Name;
        GroupEditError = string.Empty;
        RefreshCentralGroupSections();
    }

    private void CommitGroupEditLegacy()
    {
        var group = EditingGroupId is { } id
            ? Groups.FirstOrDefault(item => item.Id == id)
            : null;
        if (group is null)
        {
            ClearGroupEditState();
            return;
        }

        var name = EditingGroupName.Trim();
        if (name.Length == 0)
        {
            if (pendingNewGroupId == group.Id)
            {
                CancelGroupEdit();
            }
            else
            {
                GroupEditError = "分组名称不能为空。";
            }

            return;
        }

        var duplicate = Groups.Any(item =>
            item.Id != group.Id
            && item.ParentId == group.ParentId
            && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            GroupEditError = "同一分类下已存在同名分组。";
            return;
        }

        group.Name = name;
        catalog.UpdateGroup(group);
        SelectedGroup = Groups.First(item => item.Id == group.Id);
        ClearGroupEditState();
        RebuildGroupSections();
    }

    private async Task CommitGroupEditAsync()
    {
        if (!centralMode)
        {
            CommitGroupEditLegacy();
            return;
        }

        if (EditingGroupId is not { } groupId
            || editingCatalogParentId is not { } parentId
            || commandService is null)
        {
            ClearGroupEditState();
            return;
        }

        var name = EditingGroupName.Trim();
        if (name.Length == 0)
        {
            GroupEditError = "分组名称不能为空。";
            return;
        }

        if (!IsServerAvailable)
        {
            GroupEditError = "Catalog API is unavailable.";
            return;
        }

        if (CatalogGroups.Any(group =>
                group.Id != groupId
                && group.ParentId == parentId
                && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            GroupEditError = "同一分类下已存在同名分组。";
            return;
        }

        try
        {
            if (editingCatalogGroupIsNew)
            {
                var sort = CatalogGroups
                    .Where(group => group.ParentId == parentId)
                    .Select(group => group.Sort)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;
                await commandService.CreateGroupAsync(
                        new CreateGroupRequest(
                            groupId,
                            name,
                            parentId,
                            sort,
                            true,
                            null))
                    .ConfigureAwait(true);
            }
            else if (CatalogGroups.FirstOrDefault(group => group.Id == groupId) is { } group)
            {
                await commandService.UpdateGroupAsync(
                        groupId,
                        new UpdateGroupRequest(
                            name,
                            parentId,
                            group.Sort,
                            group.Enabled,
                            group.Kind,
                            editingCatalogGroupRevision))
                    .ConfigureAwait(true);
            }
            else
            {
                GroupEditError = "分组不存在。";
                return;
            }

            ClearGroupEditState();
            RefreshCentralCatalogView();
        }
        catch (CatalogApiException exception)
        {
            GroupEditError = exception.Code;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            GroupEditError = "Catalog write failed.";
        }
    }

    private void CancelGroupEdit()
    {
        if (centralMode)
        {
            ClearGroupEditState();
            RefreshCentralGroupSections();
            return;
        }

        CancelGroupEditLegacy();
    }

    private void CancelGroupEditLegacy()
    {
        if (pendingNewGroupId is { } pendingId)
        {
            catalog.DeleteGroup(pendingId);
        }

        ClearGroupEditState();
        RebuildGroupSections();
    }

    private void DeleteGroup(object? value)
    {
        if (centralMode)
        {
            DeleteCatalogChild(value as DeviceGroupDto);
            return;
        }

        DeleteLegacyGroup(value as DeviceGroup);
    }

    private void DeleteLegacyGroup(DeviceGroup? group)
    {
        if (group?.ParentId is null)
        {
            return;
        }

        if (catalog.GetDevices(group.Id).Count > 0)
        {
            ShowDialog(
                DeviceDialogMode.Information,
                "该分组下仍有设备，请先移动或删除设备。",
                null);
            return;
        }

        ShowDialog(
            DeviceDialogMode.Confirmation,
            $"确定删除分组“{group.Name}”吗？",
            () =>
            {
                catalog.DeleteGroup(group.Id);
                if (SelectedGroup?.Id == group.Id)
                {
                    selectedGroup = Groups.FirstOrDefault(item => item.Name == "备用1" && item.ParentId is not null)
                        ?? Groups.Where(item => item.ParentId is not null).OrderBy(item => item.Sort).FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedGroup));
                }

                RebuildGroupSections();
                RefreshDevices();
                return Task.CompletedTask;
            });
    }

    private void DeleteCatalogChild(DeviceGroupDto? group)
    {
        if (group is null || group.ParentId is null)
        {
            return;
        }

        ShowDialog(
            DeviceDialogMode.Confirmation,
            $"确定删除分组“{group.Name}”吗？",
            async () =>
            {
                if (commandService is null || !IsServerAvailable)
                {
                    GroupEditError = "Catalog API is unavailable.";
                    return;
                }

                try
                {
                    await commandService.DeleteGroupAsync(group.Id, group.Revision)
                        .ConfigureAwait(true);
                    if (selectedCatalogGroup?.Id == group.Id)
                    {
                        selectedCatalogGroup = null;
                    }

                    RefreshCentralCatalogView();
                }
                catch (CatalogApiException exception)
                {
                    GroupEditError = exception.Code;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    GroupEditError = "Catalog write failed.";
                }
            });
    }

    private void ShowDialog(DeviceDialogMode mode, string message, Func<Task>? action)
    {
        pendingDialogAction = action;
        DialogMode = mode;
        DialogMessage = message;
        IsDialogOpen = true;
    }

    private async Task ConfirmDialogAsync()
    {
        var action = DialogMode == DeviceDialogMode.Confirmation ? pendingDialogAction : null;
        ClearDialog();
        if (action is not null)
        {
            await action().ConfigureAwait(true);
        }
    }

    private void ClearDialog()
    {
        pendingDialogAction = null;
        DialogMode = DeviceDialogMode.None;
        DialogMessage = string.Empty;
        IsDialogOpen = false;
    }

    private void ClearGroupEditState()
    {
        pendingNewGroupId = null;
        EditingGroupId = null;
        editingCatalogParentId = null;
        editingCatalogGroupIsNew = false;
        editingCatalogGroupRevision = 0;
        EditingGroupName = string.Empty;
        GroupEditError = string.Empty;
    }

    private void RebuildGroupSections()
    {
        GroupSections.Clear();
        foreach (var root in Groups.Where(group => group.ParentId is null).OrderBy(group => group.Sort))
        {
            var children = Groups
                .Where(group => group.ParentId == root.Id)
                .OrderBy(group => group.Sort)
                .Select(group => new DeviceGroupTreeItemViewModel(group)
                {
                    IsSelected = group.Id == SelectedGroup?.Id,
                    IsEditing = group.Id == EditingGroupId
                });
            GroupSections.Add(new DeviceGroupTreeItemViewModel(root, children));
        }

        OnPropertyChanged(nameof(EditableGroups));
    }

    private void AddDevice()
    {
        if (centralMode)
        {
            if (selectedCatalogGroup?.ParentId is null)
            {
                return;
            }

            editingCatalogDevice = null;
            SelectedDevice = null;
            IsEditing = false;
            suppressDraftTracking = true;
            try
            {
                EditDraft.ResetForAdd(selectedCatalogGroup.Id);
                HasUnsavedDraft = false;
            }
            finally
            {
                suppressDraftTracking = false;
            }

            ValidationMessage = string.Empty;
            OperationErrorCode = null;
            OperationError = string.Empty;
            IsEditPanelOpen = true;
            OnPropertyChanged(nameof(EditorTitle));
            return;
        }

        if (SelectedGroup?.ParentId is null)
        {
            return;
        }

        SelectedDevice = null;
        IsEditing = false;
        EditDraft.ResetForAdd(SelectedGroup.Id);
        ValidationMessage = string.Empty;
        IsEditPanelOpen = true;
    }

    private void EditDevice(object? value)
    {
        if (centralMode)
        {
            if (value is not CameraDeviceDto device)
            {
                return;
            }

            editingCatalogDevice = device;
            SelectedDevice = null;
            IsEditing = true;
            suppressDraftTracking = true;
            try
            {
                EditDraft.Load(device);
                HasUnsavedDraft = false;
            }
            finally
            {
                suppressDraftTracking = false;
            }

            ValidationMessage = string.Empty;
            OperationErrorCode = null;
            OperationError = string.Empty;
            IsEditPanelOpen = true;
            OnPropertyChanged(nameof(EditorTitle));
            return;
        }

        if (value is not CameraDevice legacyDevice)
        {
            return;
        }

        SelectedDevice = legacyDevice;
        IsEditing = true;
        EditDraft.Load(legacyDevice);
        ValidationMessage = string.Empty;
        IsEditPanelOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    private bool CanSaveDevice() =>
        !IsSaving && (!centralMode || IsServerAvailable);

    private bool CanCommitGroupEdit() =>
        !centralMode || IsServerAvailable && EditingGroupId is not null;

    private async Task SaveDeviceAsync()
    {
        if (!centralMode)
        {
            SaveDevice();
            LastOperationSucceeded = true;
            return;
        }

        OperationErrorCode = null;
        OperationError = string.Empty;
        LastOperationSucceeded = false;

        if (!TryValidateCatalogDraft(out var groupId, out var sdkPort, out var rtspPort, out var channelNo))
        {
            return;
        }

        if (!IsServerAvailable || commandService is null)
        {
            SetOperationError("CATALOG_UNAVAILABLE", "Catalog API is unavailable.");
            return;
        }

        IsSaving = true;
        try
        {
            if (editingCatalogDevice is { } existing)
            {
                var request = BuildUpdateRequest(
                    existing,
                    groupId,
                    sdkPort,
                    rtspPort,
                    channelNo);
                var updated = await commandService.UpdateDeviceAsync(
                        existing.Id,
                        request)
                    .ConfigureAwait(true);
                editingCatalogDevice = updated;
                RefreshCentralCatalogView();
            }
            else
            {
                var request = BuildCreateRequest(
                    groupId,
                    sdkPort,
                    rtspPort,
                    channelNo);
                var created = await commandService.CreateDeviceAsync(request)
                    .ConfigureAwait(true);
                editingCatalogDevice = created;
                RefreshCentralCatalogView();
            }

            HasUnsavedDraft = false;
            LastOperationSucceeded = true;
            CloseCentralEditor();
        }
        catch (CatalogMutationUncertainException exception)
        {
            SetOperationError("CATALOG_MUTATION_UNCERTAIN", exception.Message);
        }
        catch (CatalogApiException exception)
        {
            SetOperationError(exception.Code, exception.Code);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetOperationError("CATALOG_WRITE_FAILED", "Catalog write failed.");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool TryValidateCatalogDraft(
        out Guid groupId,
        out int sdkPort,
        out int rtspPort,
        out int channelNo)
    {
        groupId = Guid.Empty;
        sdkPort = 0;
        rtspPort = 0;
        channelNo = 0;

        if (string.IsNullOrWhiteSpace(EditDraft.Name))
        {
            ValidationMessage = "请输入设备名称。";
            return false;
        }

        if (EditDraft.GroupId is not { } selectedGroupId || selectedGroupId == Guid.Empty)
        {
            ValidationMessage = "请选择所属分组。";
            return false;
        }

        if (CatalogGroups.Count > 0
            && CatalogGroups.All(group => group.Id != selectedGroupId))
        {
            ValidationMessage = "请选择所属分组。";
            return false;
        }

        if (!IPAddress.TryParse(EditDraft.IpAddress.Trim(), out _))
        {
            ValidationMessage = "请输入有效的设备IP地址。";
            return false;
        }

        if (!int.TryParse(EditDraft.SdkPort, out sdkPort) || sdkPort is < 1 or > 65535)
        {
            ValidationMessage = "SDK端口必须在1到65535之间。";
            return false;
        }

        if (!int.TryParse(EditDraft.RtspPort, out rtspPort) || rtspPort is < 1 or > 65535)
        {
            ValidationMessage = "RTSP端口必须在1到65535之间。";
            return false;
        }

        if (!int.TryParse(EditDraft.ChannelNo, out channelNo) || channelNo <= 0)
        {
            ValidationMessage = "通道号必须大于0。";
            return false;
        }

        groupId = selectedGroupId;
        ValidationMessage = string.Empty;
        return true;
    }

    private UpdateDeviceRequest BuildUpdateRequest(
        CameraDeviceDto source,
        Guid groupId,
        int sdkPort,
        int rtspPort,
        int channelNo)
    {
        var channels = source.Channels.Count == 0
            ? [new CameraChannelInput(
                Guid.NewGuid(),
                channelNo,
                GetChannelName(channelNo),
                EditDraft.StreamType,
                true)]
            : source.Channels.Select((channel, index) => index == 0
                ? new CameraChannelInput(
                    channel.Id,
                    channelNo,
                    GetChannelName(channelNo),
                    EditDraft.StreamType,
                    channel.Enabled)
                : new CameraChannelInput(
                    channel.Id,
                    channel.ChannelNo,
                    channel.ChannelName,
                    channel.StreamType,
                    channel.Enabled)).ToArray();

        return new UpdateDeviceRequest(
            groupId,
            EditDraft.Name.Trim(),
            EditDraft.IpAddress.Trim(),
            sdkPort,
            rtspPort,
            EditDraft.Username.Trim(),
            string.IsNullOrWhiteSpace(EditDraft.Password)
                ? null
                : EditDraft.Password,
            EditDraft.Manufacturer.Trim(),
            EditDraft.Model.Trim(),
            EditDraft.TransportMode,
            true,
            EditDraft.Remark.Trim(),
            source.Revision,
            channels);
    }

    private CreateDeviceRequest BuildCreateRequest(
        Guid groupId,
        int sdkPort,
        int rtspPort,
        int channelNo)
    {
        var deviceId = Guid.NewGuid();
        return new CreateDeviceRequest(
            deviceId,
            groupId,
            EditDraft.Name.Trim(),
            EditDraft.IpAddress.Trim(),
            sdkPort,
            rtspPort,
            EditDraft.Username.Trim(),
            EditDraft.Password,
            EditDraft.Manufacturer.Trim(),
            EditDraft.Model.Trim(),
            EditDraft.TransportMode,
            true,
            EditDraft.Remark.Trim(),
            [new CameraChannelInput(
                Guid.NewGuid(),
                channelNo,
                GetChannelName(channelNo),
                EditDraft.StreamType,
                true)]);
    }

    private string GetChannelName(int channelNo) =>
        string.IsNullOrWhiteSpace(EditDraft.ChannelName)
            ? $"通道{channelNo}"
            : EditDraft.ChannelName.Trim();

    private void SetOperationError(string code, string message)
    {
        OperationErrorCode = code;
        OperationError = message;
        HasUnsavedDraft = true;
        LastOperationSucceeded = false;
    }

    private void OnEditDraftPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (centralMode && !suppressDraftTracking)
        {
            HasUnsavedDraft = true;
        }
    }

    private void OnReadModelChanged(object? sender, EventArgs e) => RefreshCentralCatalogView();

    private void OnCommandAvailabilityChanged(object? sender, EventArgs e)
    {
        IsServerAvailable = commandService?.CanWrite == true;
    }

    private void RefreshCentralCatalogView()
    {
        if (!centralMode || readModel is null)
        {
            return;
        }

        CatalogGroups.Clear();
        foreach (var group in readModel.GetGroups())
        {
            CatalogGroups.Add(group);
        }

        CatalogDevices.Clear();
        var groupIds = CatalogGroups.Select(group => group.Id).ToList();
        if (groupIds.Count == 0)
        {
            groupIds.Add(Guid.Empty);
        }

        foreach (var groupId in groupIds)
        {
            foreach (var device in readModel.GetDevices(groupId))
            {
                if (CatalogDevices.All(existing => existing.Id != device.Id))
                {
                    CatalogDevices.Add(device);
                }
            }
        }

        selectedCatalogGroup = CatalogGroups.FirstOrDefault(group =>
            group.Id == selectedCatalogGroup?.Id
            && group.ParentId is not null);
        selectedCatalogGroup ??= CatalogGroups
            .Where(group => group.ParentId is not null)
            .OrderBy(group => CatalogGroups
                .FirstOrDefault(root => root.Id == group.ParentId)?.Sort ?? int.MaxValue)
            .ThenBy(group => group.Sort)
            .ThenBy(group => group.Id)
            .FirstOrDefault();
        IsServerAvailable = commandService?.CanWrite == true;
        OnPropertyChanged(nameof(EditableCatalogGroups));
        OnPropertyChanged(nameof(ActiveSelectedGroupName));
        OnPropertyChanged(nameof(ActiveDevices));
        RefreshCentralGroupSections();
    }

    private void RefreshCentralGroupSections()
    {
        if (!centralMode)
        {
            return;
        }

        GroupSections.Clear();
        foreach (var root in CatalogGroups
                     .Where(group => group.ParentId is null)
                     .OrderBy(group => group.Sort)
                     .ThenBy(group => group.Id))
        {
            var children = CatalogGroups
                .Where(group => group.ParentId == root.Id)
                .OrderBy(group => group.Sort)
                .ThenBy(group => group.Id)
                .Select(group => new DeviceGroupTreeItemViewModel(group)
                {
                    IsSelected = group.Id == selectedCatalogGroup?.Id,
                    IsEditing = group.Id == EditingGroupId
                });
            GroupSections.Add(new DeviceGroupTreeItemViewModel(root, children));
        }

        OnPropertyChanged(nameof(ActiveGroupSections));
        OnPropertyChanged(nameof(ActiveEditableGroups));
    }

    private void CloseCentralEditor()
    {
        IsEditPanelOpen = false;
        IsEditing = false;
        editingCatalogDevice = null;
        suppressDraftTracking = true;
        try
        {
            EditDraft.ResetForAdd(selectedCatalogGroup?.Id ?? Guid.Empty);
            HasUnsavedDraft = false;
        }
        finally
        {
            suppressDraftTracking = false;
        }

        OnPropertyChanged(nameof(EditorTitle));
    }

    private void SaveDevice()
    {
        if (!TryValidateDraft(out var groupId, out var sdkPort, out var rtspPort, out var channelNo))
        {
            return;
        }

        if (IsEditing && SelectedDevice is not null)
        {
            var updated = Clone(SelectedDevice);
            ApplyDraft(updated, groupId, sdkPort, rtspPort, channelNo);
            catalog.UpdateDevice(updated);
        }
        else
        {
            var device = new CameraDevice { Id = Guid.NewGuid() };
            ApplyDraft(device, groupId, sdkPort, rtspPort, channelNo);
            catalog.AddDevice(device);
        }

        CloseEditor();
        RefreshDevices();
    }

    private bool TryValidateDraft(
        out Guid groupId,
        out int sdkPort,
        out int rtspPort,
        out int channelNo)
    {
        groupId = Guid.Empty;
        sdkPort = 0;
        rtspPort = 0;
        channelNo = 0;

        if (string.IsNullOrWhiteSpace(EditDraft.Name))
        {
            ValidationMessage = "请输入设备名称。";
            return false;
        }

        var group = EditDraft.GroupId is { } selectedGroupId
            ? Groups.FirstOrDefault(item => item.Id == selectedGroupId && item.ParentId is not null && item.Enabled)
            : null;
        if (group is null)
        {
            ValidationMessage = "请选择所属分组。";
            return false;
        }

        if (!IPAddress.TryParse(EditDraft.IpAddress.Trim(), out _))
        {
            ValidationMessage = "请输入有效的设备IP地址。";
            return false;
        }

        if (!int.TryParse(EditDraft.SdkPort, out sdkPort) || sdkPort is < 1 or > 65535)
        {
            ValidationMessage = "SDK端口必须在1到65535之间。";
            return false;
        }

        if (!int.TryParse(EditDraft.RtspPort, out rtspPort) || rtspPort is < 1 or > 65535)
        {
            ValidationMessage = "RTSP端口必须在1到65535之间。";
            return false;
        }

        if (!int.TryParse(EditDraft.ChannelNo, out channelNo) || channelNo <= 0)
        {
            ValidationMessage = "通道号必须大于0。";
            return false;
        }

        groupId = group.Id;
        ValidationMessage = string.Empty;
        return true;
    }

    private void ApplyDraft(
        CameraDevice device,
        Guid groupId,
        int sdkPort,
        int rtspPort,
        int channelNo)
    {
        device.Name = EditDraft.Name.Trim();
        device.GroupId = groupId;
        device.IpAddress = EditDraft.IpAddress.Trim();
        device.SdkPort = sdkPort;
        device.RtspPort = rtspPort;
        device.Username = EditDraft.Username.Trim();
        if (!IsEditing || EditDraft.Password.Length > 0)
        {
            device.Password = EditDraft.Password;
        }
        device.Manufacturer = EditDraft.Manufacturer.Trim();
        device.Model = EditDraft.Model.Trim();
        device.Remark = EditDraft.Remark.Trim();
        device.TransportMode = EditDraft.TransportMode;
        device.Enabled = true;

        var channel = device.Channels.FirstOrDefault();
        if (channel is null)
        {
            channel = new CameraChannel
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                Enabled = true
            };
            device.Channels.Add(channel);
        }

        channel.ChannelNo = channelNo;
        channel.ChannelName = string.IsNullOrWhiteSpace(EditDraft.ChannelName)
            ? $"通道{channelNo}"
            : EditDraft.ChannelName.Trim();
        channel.StreamType = EditDraft.StreamType;
        channel.StreamId = string.IsNullOrWhiteSpace(channel.StreamId)
            ? $"camera-{device.Id:N}-channel-{channelNo}"
            : channel.StreamId;
    }

    private void CancelEdit()
    {
        if (centralMode)
        {
            CloseCentralEditor();
            return;
        }

        CloseEditor();
    }

    private void CloseEditor()
    {
        IsEditPanelOpen = false;
        IsEditing = false;
        SelectedDevice = null;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(EditorTitle));
    }

    private void DeleteDevice(object? value)
    {
        if (centralMode)
        {
            DeleteCatalogDevice(value as CameraDeviceDto);
            return;
        }

        DeleteLegacyDevice(value as CameraDevice);
    }

    private void DeleteLegacyDevice(CameraDevice? device)
    {
        if (device is null)
        {
            return;
        }

        ShowDialog(
            DeviceDialogMode.Confirmation,
            $"确定删除设备“{device.Name}”吗？",
            () =>
            {
                catalog.DeleteDevice(device.Id);
                if (ReferenceEquals(SelectedDevice, device))
                {
                    CloseEditor();
                }

                RefreshDevices();
                return Task.CompletedTask;
            });
    }

    private void DeleteCatalogDevice(CameraDeviceDto? device)
    {
        if (device is null)
        {
            return;
        }

        ShowDialog(
            DeviceDialogMode.Confirmation,
            $"确定删除设备“{device.Name}”吗？",
            async () =>
            {
                if (commandService is null || !IsServerAvailable)
                {
                    SetOperationError("CATALOG_UNAVAILABLE", "Catalog API is unavailable.");
                    return;
                }

                try
                {
                    await commandService.DeleteDeviceAsync(device.Id, device.Revision)
                        .ConfigureAwait(true);
                    if (editingCatalogDevice?.Id == device.Id)
                    {
                        CloseCentralEditor();
                    }

                    RefreshCentralCatalogView();
                }
                catch (CatalogApiException exception)
                {
                    SetOperationError(exception.Code, exception.Code);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    SetOperationError("CATALOG_WRITE_FAILED", "Catalog write failed.");
                }
            });
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => RefreshCatalogView();

    private void RefreshCatalogView()
    {
        var selectedGroupId = selectedGroup?.Id;
        Groups.Clear();
        foreach (var group in catalog.GetGroups())
        {
            Groups.Add(group);
        }

        selectedGroup = selectedGroupId is { } id
            ? Groups.FirstOrDefault(group => group.Id == id)
            : null;
        selectedGroup ??= Groups.FirstOrDefault(group => group.Name == "备用1" && group.ParentId is not null)
            ?? Groups.Where(group => group.ParentId is not null).OrderBy(group => group.Sort).FirstOrDefault();
        OnPropertyChanged(nameof(SelectedGroup));
        RebuildGroupSections();
        RefreshDevices();
    }

    private static CameraDevice Clone(CameraDevice source)
    {
        var clone = new CameraDevice
        {
            Id = source.Id,
            Name = source.Name,
            GroupId = source.GroupId,
            IpAddress = source.IpAddress,
            SdkPort = source.SdkPort,
            RtspPort = source.RtspPort,
            Username = source.Username,
            Password = source.Password,
            Manufacturer = source.Manufacturer,
            Model = source.Model,
            TransportMode = source.TransportMode,
            Status = source.Status,
            Enabled = source.Enabled,
            Remark = source.Remark
        };
        foreach (var channel in source.Channels)
        {
            clone.Channels.Add(new CameraChannel
            {
                Id = channel.Id,
                DeviceId = channel.DeviceId,
                ChannelNo = channel.ChannelNo,
                ChannelName = channel.ChannelName,
                StreamType = channel.StreamType,
                StreamId = channel.StreamId,
                Enabled = channel.Enabled
            });
        }

        return clone;
    }
}
