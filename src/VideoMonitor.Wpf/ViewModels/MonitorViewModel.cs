using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private readonly MonitorSwitchService switchService;
    private string currentChuteName = string.Empty;
    private string currentTunnelName = string.Empty;
    private MonitorTreeItemViewModel? selectedTreeItem;

    public MonitorViewModel(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups)
    {
        this.switchService = switchService;
        Groups = groups;
        MainTiles = new ObservableCollection<VideoTileViewModel>(
            Enumerable.Range(0, 4).Select(_ => new VideoTileViewModel()));
        TreeSections = CreateTree(groups);
        SelectGroupCommand = new RelayCommand<MonitorTreeItemViewModel>(SelectGroup);

        switchService.LayoutChanged += OnLayoutChanged;
        Render(switchService.Current);
        var initialGroup = TreeSections
            .SelectMany(section => section.Children)
            .FirstOrDefault(item => item.Name == switchService.Current.MainSlots[0].GroupName);
        selectedTreeItem = initialGroup?.Children.FirstOrDefault() ?? initialGroup;
        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = true;
        }
    }

    public IReadOnlyList<MonitorGroup> Groups { get; }

    public ObservableCollection<VideoTileViewModel> MainTiles { get; }

    public ObservableCollection<MonitorTreeItemViewModel> TreeSections { get; }

    public IRelayCommand<MonitorTreeItemViewModel> SelectGroupCommand { get; }


    public string CurrentChuteName
    {
        get => currentChuteName;
        private set => SetProperty(ref currentChuteName, value);
    }

    public string CurrentTunnelName
    {
        get => currentTunnelName;
        private set => SetProperty(ref currentTunnelName, value);
    }

    private static ObservableCollection<MonitorTreeItemViewModel> CreateTree(
        IEnumerable<MonitorGroup> groups)
    {
        var groupList = groups.ToArray();
        return
        [
            CreateSection("卸矿站监控", MonitorGroupType.UnloadingStation, groupList),
            CreateSection("溜井监控", MonitorGroupType.Chute, groupList),
            CreateSection("巷道监控", MonitorGroupType.Tunnel, groupList)
        ];
    }

    private static MonitorTreeItemViewModel CreateSection(
        string title,
        MonitorGroupType type,
        IEnumerable<MonitorGroup> groups)
    {
        var matchingGroups = groups.Where(group => group.Type == type).ToArray();
        var children = matchingGroups.Select(group =>
        {
            var cameraItems = type == MonitorGroupType.Chute
                ? group.Cameras.Select(camera => new MonitorTreeItemViewModel(
                    camera.Name,
                    group,
                    status: camera.Status,
                    isCamera: true))
                : Enumerable.Empty<MonitorTreeItemViewModel>();

            return new MonitorTreeItemViewModel(
                group.Name,
                group,
                cameraItems,
                type == MonitorGroupType.Chute ? $"({group.Cameras.Count}/{group.Cameras.Count})" : "",
                isExpanded: group.Name == "备用1");
        });
        var total = type == MonitorGroupType.Chute
            ? matchingGroups.Sum(group => group.Cameras.Count)
            : matchingGroups.Length;

        return new MonitorTreeItemViewModel(title, children: children, countText: $"({total}/{total})", isExpanded: true);
    }

    private void SelectGroup(MonitorTreeItemViewModel? item)
    {
        if (item?.Group is not { } group)
        {
            return;
        }

        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = false;
        }

        if (item.HasChildren)
        {
            item.IsExpanded = true;
        }

        selectedTreeItem = item.Children.FirstOrDefault() ?? item;
        selectedTreeItem.IsSelected = true;

        switch (group.Type)
        {
            case MonitorGroupType.Chute:
                switchService.SwitchChuteGroup(group);
                break;
            case MonitorGroupType.Tunnel:
                switchService.SwitchTunnel(group);
                break;
            case MonitorGroupType.UnloadingStation:
                switchService.SwitchUnloadingGroup(group);
                break;
        }
    }

    private void OnLayoutChanged(object? sender, MonitorLayoutSnapshot snapshot) => Render(snapshot);

    private void Render(MonitorLayoutSnapshot snapshot)
    {
        for (var index = 0; index < MainTiles.Count; index++)
        {
            MainTiles[index].Update(snapshot.MainSlots[index]);
        }

        CurrentChuteName = snapshot.MainSlots[0].GroupName;
        CurrentTunnelName = snapshot.MainSlots[3].GroupName;
    }
}
