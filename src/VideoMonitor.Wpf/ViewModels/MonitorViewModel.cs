using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private readonly IDeviceCatalog deviceCatalog;
    private readonly MonitorSwitchService switchService;
    private string currentChuteName = string.Empty;
    private string currentTunnelName = string.Empty;
    private MonitorTreeItemViewModel? selectedTreeItem;
    private bool isSingleTileMode;
    private bool isDetailPanelCollapsed = true;
    private VideoTileViewModel selectedVideoSlot = null!;
    private IReadOnlyList<MonitorGroup> groups = [];

    public MonitorViewModel(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups,
        IDeviceCatalog deviceCatalog)
    {
        this.switchService = switchService;
        this.deviceCatalog = deviceCatalog ?? throw new ArgumentNullException(nameof(deviceCatalog));
        Groups = groups;
        MainTiles = new ObservableCollection<VideoTileViewModel>(
            Enumerable.Range(0, 4).Select(_ => new VideoTileViewModel()));
        TreeSections = CreateTree(groups);
        SelectGroupCommand = new RelayCommand<MonitorTreeItemViewModel>(SelectGroup);
        ToggleSingleTileCommand = new RelayCommand<VideoTileViewModel>(ToggleSingleTile);
        ExitSingleTileModeCommand = new RelayCommand(() => IsSingleTileMode = false);
        ToggleDetailPanelCommand = new RelayCommand(() => IsDetailPanelCollapsed = !IsDetailPanelCollapsed);
        SelectedVideoSlot = MainTiles[0];

        switchService.LayoutChanged += OnLayoutChanged;
        deviceCatalog.Changed += OnCatalogChanged;
        Render(switchService.Current);
        var initialGroup = TreeSections
            .SelectMany(section => section.Children)
            .FirstOrDefault(item => item.Name == switchService.Current.MainSlots[0].GroupName);
        selectedTreeItem = initialGroup;
        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = true;
        }
    }

    public IReadOnlyList<MonitorGroup> Groups
    {
        get => groups;
        private set => SetProperty(ref groups, value);
    }

    public ObservableCollection<VideoTileViewModel> MainTiles { get; }

    public ObservableCollection<MonitorTreeItemViewModel> TreeSections { get; }

    public IRelayCommand<MonitorTreeItemViewModel> SelectGroupCommand { get; }

    public IRelayCommand<VideoTileViewModel> ToggleSingleTileCommand { get; }

    public IRelayCommand ExitSingleTileModeCommand { get; }

    public IRelayCommand ToggleDetailPanelCommand { get; }


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

    public bool IsSingleTileMode
    {
        get => isSingleTileMode;
        private set => SetProperty(ref isSingleTileMode, value);
    }

    public bool IsDetailPanelCollapsed
    {
        get => isDetailPanelCollapsed;
        private set => SetProperty(ref isDetailPanelCollapsed, value);
    }

    public VideoTileViewModel SelectedVideoSlot
    {
        get => selectedVideoSlot;
        private set => SetProperty(ref selectedVideoSlot, value);
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
        var children = matchingGroups.Select(group => new MonitorTreeItemViewModel(group.Name, group));
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

        selectedTreeItem = item;
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

    private void ToggleSingleTile(VideoTileViewModel? slot)
    {
        if (slot is null || !MainTiles.Contains(slot))
        {
            return;
        }

        if (IsSingleTileMode && ReferenceEquals(SelectedVideoSlot, slot))
        {
            IsSingleTileMode = false;
            return;
        }

        SelectedVideoSlot = slot;
        IsSingleTileMode = true;
    }

    private void OnLayoutChanged(object? sender, MonitorLayoutSnapshot snapshot) => Render(snapshot);

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        RefreshCatalogProjection();
        Render(switchService.Current);
    }

    private void Render(MonitorLayoutSnapshot snapshot)
    {
        var mainCameras = snapshot.MainSlots
            .Select(ResolveProjectedCamera)
            .ToArray();
        for (var index = 0; index < MainTiles.Count; index++)
        {
            var camera = mainCameras[index];
            var device = deviceCatalog.GetDevice(camera.DeviceId);
            var channel = device?.Channels.SingleOrDefault(item => item.Id == camera.ChannelId);
            MainTiles[index].Update(camera, device, channel);
        }

        CurrentChuteName = mainCameras[0].GroupName;
        CurrentTunnelName = mainCameras[3].GroupName;
    }

    private void RefreshCatalogProjection()
    {
        var expandedSections = TreeSections.ToDictionary(
            section => section.Name,
            section => section.IsExpanded);
        var selectedGroupId = selectedTreeItem?.Group?.GroupId;
        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = false;
        }

        var refreshedGroups = MonitorCatalogProjection.CreateGroups(deviceCatalog);
        Groups = refreshedGroups;
        TreeSections.Clear();
        selectedTreeItem = null;

        foreach (var section in CreateTree(refreshedGroups))
        {
            if (expandedSections.TryGetValue(section.Name, out var isExpanded))
            {
                section.IsExpanded = isExpanded;
            }

            var selectedItem = section.Children.FirstOrDefault(item =>
                item.Group?.GroupId == selectedGroupId);
            if (selectedItem is not null)
            {
                selectedItem.IsSelected = true;
                selectedTreeItem = selectedItem;
            }

            TreeSections.Add(section);
        }
    }

    private CameraInfo ResolveProjectedCamera(CameraInfo camera) =>
        Groups
            .SelectMany(group => group.Cameras)
            .FirstOrDefault(item =>
                item.DeviceId == camera.DeviceId
                && item.ChannelId == camera.ChannelId)
        ?? camera;
}
