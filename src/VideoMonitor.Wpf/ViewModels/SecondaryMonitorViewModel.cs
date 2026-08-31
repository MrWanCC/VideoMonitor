using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class SecondaryMonitorViewModel : ObservableObject
{
    private readonly IDeviceCatalogReadModel catalog;
    private readonly Func<IReadOnlyList<MonitorGroup>> projectGroups;
    private readonly MonitorSwitchService switchService;
    private IReadOnlyList<MonitorGroup> groups = [];
    private Guid? selectedGroupId;
    private string currentGroupName = "未配置";

    public SecondaryMonitorViewModel(
        MonitorSwitchService switchService,
        IDeviceCatalogReadModel catalog)
    {
        this.switchService = switchService
            ?? throw new ArgumentNullException(nameof(switchService));
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        projectGroups = () => MonitorCatalogProjection.CreateGroups(this.catalog);
        var projectedGroups = projectGroups();
        this.switchService.ReplaceGroups(projectedGroups);
        Initialize(projectedGroups);
    }

    // Compatibility constructor for the pre-central local WPF path.
    public SecondaryMonitorViewModel(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups,
        IDeviceCatalog deviceCatalog)
    {
        this.switchService = switchService
            ?? throw new ArgumentNullException(nameof(switchService));
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        catalog = new LegacyDeviceCatalogReadModel(deviceCatalog);
        projectGroups = () => MonitorCatalogProjection.CreateGroups(deviceCatalog);
        this.switchService.ReplaceGroups(groups);
        Initialize(groups);
    }

    private void Initialize(IReadOnlyList<MonitorGroup> initialGroups)
    {
        groups = initialGroups.ToArray();
        Tiles = new ObservableCollection<VideoTileViewModel>(
            Enumerable.Range(0, 3).Select(_ => new VideoTileViewModel()));
        UnloadingGroups = new ObservableCollection<MonitorTreeItemViewModel>();
        SelectGroupCommand = new RelayCommand<Guid?>(SelectGroup);
        switchService.LayoutChanged += OnLayoutChanged;
        catalog.Changed += OnCatalogChanged;
        selectedGroupId = switchService.SelectedUnloadingGroupId;
        RebuildUnloadingGroups();
        Render(switchService.Current);
    }

    public ObservableCollection<VideoTileViewModel> Tiles { get; private set; } = null!;

    public ObservableCollection<MonitorTreeItemViewModel> UnloadingGroups { get; private set; } = null!;

    public IRelayCommand<Guid?> SelectGroupCommand { get; private set; } = null!;

    public Guid? SelectedGroupId
    {
        get => selectedGroupId;
        private set
        {
            if (SetProperty(ref selectedGroupId, value))
            {
                UpdateSelectionStates();
                CurrentGroupName = GetSelectedGroupName();
            }
        }
    }

    public string CurrentGroupName
    {
        get => currentGroupName;
        private set => SetProperty(ref currentGroupName, value);
    }

    private void SelectGroup(Guid? groupId)
    {
        if (groupId is not { } id)
        {
            return;
        }

        switchService.SwitchUnloadingGroup(id);
        SelectedGroupId = switchService.SelectedUnloadingGroupId;
    }

    private void OnLayoutChanged(object? sender, MonitorLayoutSnapshot snapshot) => Render(snapshot);

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        var previousSelection = SelectedGroupId;
        var refreshedGroups = projectGroups();
        groups = refreshedGroups.ToArray();
        switchService.ReplaceGroups(refreshedGroups);
        RebuildUnloadingGroups();
        SelectedGroupId = groups.Any(group =>
            group.Type == MonitorGroupType.UnloadingStation
            && group.GroupId == previousSelection)
            ? previousSelection
            : switchService.SelectedUnloadingGroupId;
        Render(switchService.Current);
    }

    private void RebuildUnloadingGroups()
    {
        UnloadingGroups.Clear();
        foreach (var group in groups
            .Where(group => group.Type == MonitorGroupType.UnloadingStation)
            .OrderBy(group => group.RootSort)
            .ThenBy(group => group.RootGroupId)
            .ThenBy(group => group.Sort)
            .ThenBy(group => group.GroupId))
        {
            UnloadingGroups.Add(new MonitorTreeItemViewModel(
                group.Name,
                group,
                itemId: group.GroupId,
                status: CameraStatus.Unknown,
                isExpanded: false));
        }

        UpdateSelectionStates();
    }

    private void UpdateSelectionStates()
    {
        foreach (var item in UnloadingGroups)
        {
            item.IsSelected = item.ItemId == SelectedGroupId;
        }
    }

    private void Render(MonitorLayoutSnapshot snapshot)
    {
        for (var index = 0; index < Tiles.Count; index++)
        {
            var camera = index < snapshot.SecondarySlots.Count
                ? snapshot.SecondarySlots[index]
                : null;
            if (camera is null)
            {
                Tiles[index].ResetUnconfigured();
                continue;
            }

            var device = catalog.GetDevice(camera.DeviceId);
            var channel = device?.Channels.SingleOrDefault(item => item.Id == camera.ChannelId);
            Tiles[index].Update(camera, device, channel, camera.Status);
        }

        CurrentGroupName = GetSelectedGroupName();
    }

    private string GetSelectedGroupName() =>
        SelectedGroupId is { } id
        && groups.FirstOrDefault(group =>
            group.GroupId == id
            && group.Type == MonitorGroupType.UnloadingStation) is { } group
            ? group.Name
            : "未配置";
}
