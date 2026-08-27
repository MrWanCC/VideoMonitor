using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class DeviceManagementViewModel : ObservableObject
{
    private readonly ObservableCollection<CameraDevice> allDevices;
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

    public DeviceManagementViewModel(
        IEnumerable<DeviceGroup> groups,
        IEnumerable<CameraDevice> devices)
    {
        Groups = new ObservableCollection<DeviceGroup>(groups);
        allDevices = new ObservableCollection<CameraDevice>(devices);
        Devices = [];
        GroupSections = [];

        SelectGroupCommand = new RelayCommand<DeviceGroup>(SelectGroup);
        BeginAddGroupCommand = new RelayCommand<DeviceGroup>(BeginAddGroup);
        BeginRenameGroupCommand = new RelayCommand<DeviceGroup>(BeginRenameGroup);
        CommitGroupEditCommand = new RelayCommand(CommitGroupEdit);
        CancelGroupEditCommand = new RelayCommand(CancelGroupEdit);
        DeleteGroupCommand = new RelayCommand<DeviceGroup>(DeleteGroup);
        ConfirmDialogCommand = new RelayCommand(ConfirmDialog);
        CancelDialogCommand = new RelayCommand(ClearDialog);

        selectedGroup = Groups.FirstOrDefault(group => group.Name == "备用1" && group.ParentId is not null)
            ?? Groups.Where(group => group.ParentId is not null).OrderBy(group => group.Sort).FirstOrDefault();
        RebuildGroupSections();
        RefreshDevices();
    }

    public ObservableCollection<DeviceGroup> Groups { get; }

    public ObservableCollection<DeviceGroupTreeItemViewModel> GroupSections { get; }

    public ObservableCollection<CameraDevice> Devices { get; }

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
        foreach (var device in allDevices.Where(device =>
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
        Groups.Add(group);
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
        SelectedGroup = group;
        ClearGroupEditState();
        RebuildGroupSections();
    }

    private void CancelGroupEdit()
    {
        if (pendingNewGroupId is { } pendingId)
        {
            var pending = Groups.FirstOrDefault(group => group.Id == pendingId);
            if (pending is not null)
            {
                Groups.Remove(pending);
            }
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

        if (allDevices.Any(device => device.GroupId == group.Id))
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
                Groups.Remove(group);
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
    }
}
