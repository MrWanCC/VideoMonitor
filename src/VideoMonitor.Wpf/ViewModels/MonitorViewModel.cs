using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MonitorViewModel : ObservableObject
{
    private readonly IDeviceCatalogReadModel catalog;
    private readonly Func<IReadOnlyList<MonitorGroup>> projectGroups;
    private readonly MonitorSwitchService switchService;
    private string currentChuteName = "未配置";
    private string currentTunnelName = "未配置";
    private MonitorTreeItemViewModel? selectedTreeItem;
    private bool isSingleTileMode;
    private bool isDetailPanelCollapsed = true;
    private VideoTileViewModel selectedVideoSlot = null!;
    private IReadOnlyList<MonitorGroup> groups = [];
    private readonly Func<VideoTileViewModel, FormalPlaybackCoordinator>? formalCoordinatorFactory;
    private readonly Dictionary<VideoTileViewModel, FormalPlaybackCoordinator> formalCoordinators = [];
    private readonly SemaphoreSlim playbackLifecycleGate = new(1, 1);
    private bool playbackActive;

    public MonitorViewModel(
        MonitorSwitchService switchService,
        IDeviceCatalogReadModel catalog,
        Func<VideoTileViewModel, FormalPlaybackCoordinator>? formalCoordinatorFactory = null)
    {
        this.switchService = switchService
            ?? throw new ArgumentNullException(nameof(switchService));
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        this.formalCoordinatorFactory = formalCoordinatorFactory;
        projectGroups = () => MonitorCatalogProjection.CreateGroups(this.catalog);
        var projectedGroups = projectGroups();
        this.switchService.ReplaceGroups(projectedGroups);
        Initialize(projectedGroups);
    }

    // Compatibility constructor for the pre-central local WPF path.
    public MonitorViewModel(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups,
        IDeviceCatalog deviceCatalog)
    {
        this.switchService = switchService
            ?? throw new ArgumentNullException(nameof(switchService));
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        formalCoordinatorFactory = null;
        catalog = new LegacyDeviceCatalogReadModel(deviceCatalog);
        projectGroups = () => MonitorCatalogProjection.CreateGroups(deviceCatalog);
        this.switchService.ReplaceGroups(groups);
        Initialize(groups);
    }

    private void Initialize(IReadOnlyList<MonitorGroup> initialGroups)
    {
        Groups = initialGroups.ToArray();
        MainTiles = new ObservableCollection<VideoTileViewModel>(
            Enumerable.Range(0, 4).Select(_ => new VideoTileViewModel()));
        TreeSections = CreateTree(Groups);
        SelectGroupCommand = new RelayCommand<MonitorTreeItemViewModel>(SelectGroup);
        ToggleSingleTileCommand = new RelayCommand<VideoTileViewModel>(ToggleSingleTile);
        ExitSingleTileModeCommand = new RelayCommand(() => IsSingleTileMode = false);
        ToggleDetailPanelCommand = new RelayCommand(() => IsDetailPanelCollapsed = !IsDetailPanelCollapsed);
        SelectedVideoSlot = MainTiles[0];

        switchService.LayoutChanged += OnLayoutChanged;
        catalog.Changed += OnCatalogChanged;
        Render(switchService.Current);
        var initialSelection = GetInitialSelection();
        RestoreSelectedTreeItem(initialSelection.Id, initialSelection.Type);
    }

    public IReadOnlyList<MonitorGroup> Groups
    {
        get => groups;
        private set => SetProperty(ref groups, value);
    }

    public ObservableCollection<VideoTileViewModel> MainTiles { get; private set; } = null!;

    public ObservableCollection<MonitorTreeItemViewModel> TreeSections { get; private set; } = null!;

    public IRelayCommand<MonitorTreeItemViewModel> SelectGroupCommand { get; private set; } = null!;

    public IRelayCommand<VideoTileViewModel> ToggleSingleTileCommand { get; private set; } = null!;

    public IRelayCommand ExitSingleTileModeCommand { get; private set; } = null!;

    public IRelayCommand ToggleDetailPanelCommand { get; private set; } = null!;

    public async ValueTask DisposeAsync()
    {
        await DeactivatePlaybackAsync().ConfigureAwait(false);
        switchService.LayoutChanged -= OnLayoutChanged;
        catalog.Changed -= OnCatalogChanged;

        try
        {
            foreach (var coordinator in formalCoordinators.Values)
            {
                await coordinator.DisposeAsync().ConfigureAwait(false);
            }

            formalCoordinators.Clear();
        }
        finally
        {
            playbackLifecycleGate.Dispose();
        }
    }

    public async Task ActivatePlaybackAsync()
    {
        await playbackLifecycleGate.WaitAsync();
        try
        {
            if (playbackActive)
            {
                return;
            }

            playbackActive = true;
            Render(switchService.Current);
        }
        finally
        {
            playbackLifecycleGate.Release();
        }
    }

    public async Task DeactivatePlaybackAsync()
    {
        await playbackLifecycleGate.WaitAsync();
        try
        {
            playbackActive = false;
            await Task.WhenAll(
                    formalCoordinators.Values.Select(coordinator => coordinator.StopAsync()));
            foreach (var tile in MainTiles)
            {
                tile.ShowPlaceholder();
            }
        }
        finally
        {
            playbackLifecycleGate.Release();
        }
    }

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
        var rootGroups = groups
            .GroupBy(group => group.RootGroupId)
            .OrderBy(group => group.Min(item => item.RootSort))
            .ThenBy(group => group.Key)
            .ToArray();
        var sections = new ObservableCollection<MonitorTreeItemViewModel>();

        foreach (var rootGroup in rootGroups)
        {
            var orderedChildren = rootGroup
                .OrderBy(group => group.Sort)
                .ThenBy(group => group.GroupId)
                .ToArray();
            var first = orderedChildren[0];
            var children = orderedChildren.Select(group =>
                new MonitorTreeItemViewModel(
                    group.Name,
                    group,
                    itemId: group.GroupId,
                    status: CameraStatus.Unknown));
            var total = orderedChildren.Sum(group => group.Cameras.Count);

            sections.Add(new MonitorTreeItemViewModel(
                first.RootName,
                children: children,
                countText: $"({total})",
                status: CameraStatus.Unknown,
                isExpanded: true,
                itemId: first.RootGroupId));
        }

        return sections;
    }

    private void SelectGroup(MonitorTreeItemViewModel? item)
    {
        if (item?.Group is not { } group)
        {
            return;
        }

        switch (group.Type)
        {
            case MonitorGroupType.Chute:
                switchService.SwitchChuteGroup(group.GroupId);
                break;
            case MonitorGroupType.Tunnel:
                switchService.SwitchTunnelGroup(group.GroupId);
                break;
            case MonitorGroupType.UnloadingStation:
                switchService.SwitchUnloadingGroup(group.GroupId);
                break;
            default:
                return;
        }

        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = false;
        }

        selectedTreeItem = item;
        selectedTreeItem.IsSelected = true;
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
        var expandedRoots = TreeSections
            .Where(section => section.ItemId is not null)
            .ToDictionary(section => section.ItemId!.Value, section => section.IsExpanded);
        var selectedGroupId = GetSelectedGroupId();
        var selectedGroupType = GetSelectedGroupType();

        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = false;
        }

        var refreshedGroups = projectGroups();
        Groups = refreshedGroups;
        switchService.ReplaceGroups(refreshedGroups);
        TreeSections.Clear();
        foreach (var section in CreateTree(refreshedGroups))
        {
            if (section.ItemId is { } rootId
                && expandedRoots.TryGetValue(rootId, out var isExpanded))
            {
                section.IsExpanded = isExpanded;
            }

            TreeSections.Add(section);
        }

        RestoreSelectedTreeItem(selectedGroupId, selectedGroupType);
        Render(switchService.Current);
    }

    private void RestoreSelectedTreeItem(Guid? selectedGroupId, MonitorGroupType? selectedGroupType)
    {
        var fallbackId = selectedGroupType switch
        {
            MonitorGroupType.Chute => switchService.SelectedChuteGroupId,
            MonitorGroupType.Tunnel => switchService.SelectedTunnelGroupId,
            MonitorGroupType.UnloadingStation => switchService.SelectedUnloadingGroupId,
            _ => GetSelectedGroupId()
        };
        var targetId = selectedGroupId is { } requested
            && Groups.Any(group => group.GroupId == requested && group.Type == selectedGroupType)
            ? requested
            : fallbackId;
        var target = targetId is { } id
            ? TreeSections
                .SelectMany(section => section.Children)
                .FirstOrDefault(item => item.ItemId == id)
            : null;

        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = false;
        }

        selectedTreeItem = target;
        if (selectedTreeItem is not null)
        {
            selectedTreeItem.IsSelected = true;
        }
    }

    private void Render(MonitorLayoutSnapshot snapshot)
    {
        for (var index = 0; index < MainTiles.Count; index++)
        {
            var camera = index < snapshot.MainSlots.Count
                ? snapshot.MainSlots[index]
                : null;
            if (camera is null)
            {
                MainTiles[index].ResetUnconfigured();
                _ = StopFormalPlaybackAsync(MainTiles[index]);
                continue;
            }

            var device = catalog.GetDevice(camera.DeviceId);
            var channel = device?.Channels.SingleOrDefault(item => item.Id == camera.ChannelId);
            MainTiles[index].Update(camera, device, channel, camera.Status);
            if (channel is not null && playbackActive)
            {
                _ = StartFormalPlaybackAsync(
                    MainTiles[index],
                    camera.DeviceId,
                    camera.ChannelId,
                    channel.StreamType);
            }
            else
            {
                _ = StopFormalPlaybackAsync(MainTiles[index]);
            }
        }

        CurrentChuteName = GetSelectedGroupName(
            switchService.SelectedChuteGroupId,
            MonitorGroupType.Chute);
        CurrentTunnelName = GetSelectedGroupName(
            switchService.SelectedTunnelGroupId,
            MonitorGroupType.Tunnel);
    }

    private Guid? GetSelectedGroupId() => selectedTreeItem?.Group?.GroupId;

    private MonitorGroupType? GetSelectedGroupType() => selectedTreeItem?.Group?.Type;

    private (Guid? Id, MonitorGroupType? Type) GetInitialSelection()
    {
        if (switchService.SelectedChuteGroupId is { } chuteId)
        {
            return (chuteId, MonitorGroupType.Chute);
        }

        if (switchService.SelectedTunnelGroupId is { } tunnelId)
        {
            return (tunnelId, MonitorGroupType.Tunnel);
        }

        return (switchService.SelectedUnloadingGroupId, MonitorGroupType.UnloadingStation);
    }

    private string GetSelectedGroupName(Guid? groupId, MonitorGroupType type) =>
        groupId is { } id
        && Groups.FirstOrDefault(group => group.GroupId == id && group.Type == type) is { } group
            ? group.Name
            : "未配置";

    private FormalPlaybackCoordinator GetFormalCoordinator(VideoTileViewModel tile)
    {
        if (!formalCoordinators.TryGetValue(tile, out var coordinator))
        {
            coordinator = formalCoordinatorFactory!(tile);
            formalCoordinators.Add(tile, coordinator);
        }

        return coordinator;
    }

    private async Task StartFormalPlaybackAsync(
        VideoTileViewModel tile,
        Guid deviceId,
        Guid channelId,
        VideoMonitor.Core.Models.StreamType streamType)
    {
        if (formalCoordinatorFactory is null)
        {
            return;
        }

        try
        {
            await GetFormalCoordinator(tile)
                .StartAsync(deviceId, channelId, streamType)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StopFormalPlaybackAsync(VideoTileViewModel tile)
    {
        if (formalCoordinators.TryGetValue(tile, out var coordinator))
        {
            await coordinator.StopAsync().ConfigureAwait(false);
        }
    }
}
