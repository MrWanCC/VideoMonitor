using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class DeviceManagementViewModel : ObservableObject
{
    private readonly IDeviceCatalog catalog;
    private Guid? pendingNewGroupId;
    private Action? pendingDialogAction;
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

    public DeviceManagementViewModel(IDeviceCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Groups = [];
        Devices = [];
        GroupSections = [];
        EditDraft = new DeviceEditDraftViewModel();

        SelectGroupCommand = new RelayCommand<DeviceGroup>(SelectGroup);
        BeginAddGroupCommand = new RelayCommand<DeviceGroup>(BeginAddGroup);
        BeginRenameGroupCommand = new RelayCommand<DeviceGroup>(BeginRenameGroup);
        CommitGroupEditCommand = new RelayCommand(CommitGroupEdit);
        CancelGroupEditCommand = new RelayCommand(CancelGroupEdit);
        DeleteGroupCommand = new RelayCommand<DeviceGroup>(DeleteGroup);
        ConfirmDialogCommand = new RelayCommand(ConfirmDialog);
        CancelDialogCommand = new RelayCommand(ClearDialog);
        AddDeviceCommand = new RelayCommand(AddDevice);
        EditDeviceCommand = new RelayCommand<CameraDevice>(EditDevice);
        DeleteDeviceCommand = new RelayCommand<CameraDevice>(DeleteDevice);
        SaveDeviceCommand = new RelayCommand(SaveDevice);
        CancelEditCommand = new RelayCommand(CancelEdit);

        catalog.Changed += OnCatalogChanged;
        RefreshCatalogView();
    }

    public ObservableCollection<DeviceGroup> Groups { get; }

    public ObservableCollection<DeviceGroupTreeItemViewModel> GroupSections { get; }

    public ObservableCollection<CameraDevice> Devices { get; }

    public DeviceEditDraftViewModel EditDraft { get; }

    public IEnumerable<DeviceGroup> EditableGroups => Groups
        .Where(group => group.ParentId is not null && group.Enabled)
        .OrderBy(group => Groups.First(root => root.Id == group.ParentId).Sort)
        .ThenBy(group => group.Sort);

    public IReadOnlyList<StreamType> StreamTypes { get; } = Enum.GetValues<StreamType>();

    public IReadOnlyList<TransportMode> TransportModes { get; } = Enum.GetValues<TransportMode>();

    public IRelayCommand<DeviceGroup> SelectGroupCommand { get; }

    public IRelayCommand<DeviceGroup> BeginAddGroupCommand { get; }

    public IRelayCommand<DeviceGroup> BeginRenameGroupCommand { get; }

    public IRelayCommand<DeviceGroup> AddGroupCommand => BeginAddGroupCommand;

    public IRelayCommand<DeviceGroup> RenameGroupCommand => BeginRenameGroupCommand;

    public IRelayCommand CommitGroupEditCommand { get; }

    public IRelayCommand CancelGroupEditCommand { get; }

    public IRelayCommand<DeviceGroup> DeleteGroupCommand { get; }

    public IRelayCommand ConfirmDialogCommand { get; }

    public IRelayCommand CancelDialogCommand { get; }

    public IRelayCommand AddDeviceCommand { get; }

    public IRelayCommand<CameraDevice> EditDeviceCommand { get; }

    public IRelayCommand<CameraDevice> DeleteDeviceCommand { get; }

    public IRelayCommand SaveDeviceCommand { get; }

    public IRelayCommand CancelEditCommand { get; }

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
                RefreshDevices();
            }
        }
    }

    public Guid? EditingGroupId
    {
        get => editingGroupId;
        private set => SetProperty(ref editingGroupId, value);
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

    public string EditorTitle => IsEditing && SelectedDevice is not null
        ? $"编辑设备：{SelectedDevice.Name}"
        : "新增设备";

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    private void SelectGroup(DeviceGroup? group)
    {
        if (group?.ParentId is not null)
        {
            SelectedGroup = group;
        }
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

    private void BeginAddGroup(DeviceGroup? root)
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

    private void BeginRenameGroup(DeviceGroup? group)
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

    private void CommitGroupEdit()
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

    private void CancelGroupEdit()
    {
        if (pendingNewGroupId is { } pendingId)
        {
            catalog.DeleteGroup(pendingId);
        }

        ClearGroupEditState();
        RebuildGroupSections();
    }

    private void DeleteGroup(DeviceGroup? group)
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
            });
    }

    private void ShowDialog(DeviceDialogMode mode, string message, Action? action)
    {
        pendingDialogAction = action;
        DialogMode = mode;
        DialogMessage = message;
        IsDialogOpen = true;
    }

    private void ConfirmDialog()
    {
        var action = DialogMode == DeviceDialogMode.Confirmation ? pendingDialogAction : null;
        ClearDialog();
        action?.Invoke();
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

    private void EditDevice(CameraDevice? device)
    {
        if (device is null)
        {
            return;
        }

        SelectedDevice = device;
        IsEditing = true;
        EditDraft.Load(device);
        ValidationMessage = string.Empty;
        IsEditPanelOpen = true;
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
        device.Password = EditDraft.Password;
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

    private void CancelEdit() => CloseEditor();

    private void CloseEditor()
    {
        IsEditPanelOpen = false;
        IsEditing = false;
        SelectedDevice = null;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(EditorTitle));
    }

    private void DeleteDevice(CameraDevice? device)
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
