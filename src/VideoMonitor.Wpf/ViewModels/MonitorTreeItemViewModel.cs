using System.Collections.ObjectModel;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MonitorTreeItemViewModel
{
    public MonitorTreeItemViewModel(
        string name,
        MonitorGroup? group = null,
        IEnumerable<MonitorTreeItemViewModel>? children = null)
    {
        Name = name;
        Group = group;
        Children = new ObservableCollection<MonitorTreeItemViewModel>(children ?? []);
    }

    public string Name { get; }

    public MonitorGroup? Group { get; }

    public ObservableCollection<MonitorTreeItemViewModel> Children { get; }
}
