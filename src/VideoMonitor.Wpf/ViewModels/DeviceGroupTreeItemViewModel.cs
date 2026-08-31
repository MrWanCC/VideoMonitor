using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Catalog;
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

    public DeviceGroupTreeItemViewModel(
        DeviceGroupDto group,
        IEnumerable<DeviceGroupTreeItemViewModel>? children = null)
    {
        CatalogGroup = group;
        Children = new ObservableCollection<DeviceGroupTreeItemViewModel>(children ?? []);
    }

    public DeviceGroup? Group { get; }

    public DeviceGroupDto? CatalogGroup { get; }

    public ObservableCollection<DeviceGroupTreeItemViewModel> Children { get; }

    public bool IsRoot => CatalogGroup?.ParentId is null && CatalogGroup is not null
        || Group?.ParentId is null;

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
