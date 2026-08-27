using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class DeviceGroupTreeItemViewModel : ObservableObject
{
    private bool isExpanded = true;
    private bool isSelected;
    private bool isEditing;

    public DeviceGroupTreeItemViewModel(
        DeviceGroup group,
        IEnumerable<DeviceGroupTreeItemViewModel>? children = null)
    {
        Group = group;
        Children = new ObservableCollection<DeviceGroupTreeItemViewModel>(children ?? []);
    }

    public DeviceGroup Group { get; }

    public ObservableCollection<DeviceGroupTreeItemViewModel> Children { get; }

    public bool IsRoot => Group.ParentId is null;

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public bool IsEditing
    {
        get => isEditing;
        set => SetProperty(ref isEditing, value);
    }
}
