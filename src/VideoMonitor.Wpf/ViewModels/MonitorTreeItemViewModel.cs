using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MonitorTreeItemViewModel : ObservableObject
{
    private bool isSelected;
    private bool isExpanded;
    public MonitorTreeItemViewModel(
        string name,
        MonitorGroup? group = null,
        IEnumerable<MonitorTreeItemViewModel>? children = null,
        string countText = "",
        CameraStatus status = CameraStatus.Unknown,
        bool isExpanded = false,
        Guid? itemId = null)
    {
        Name = name;
        Group = group;
        ItemId = itemId ?? group?.GroupId;
        Children = new ObservableCollection<MonitorTreeItemViewModel>(children ?? []);
        CountText = countText;
        Status = status;
        this.isExpanded = isExpanded;
    }

    public string Name { get; }

    public MonitorGroup? Group { get; }

    public Guid? ItemId { get; }

    public ObservableCollection<MonitorTreeItemViewModel> Children { get; }

    public string CountText { get; }

    public CameraStatus Status { get; }

    public bool HasChildren => Children.Count > 0;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }
}
