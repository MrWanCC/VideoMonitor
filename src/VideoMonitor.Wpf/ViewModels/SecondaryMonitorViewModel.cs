using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class SecondaryMonitorViewModel : ObservableObject
{
    private readonly IDeviceCatalog deviceCatalog;
    private readonly MonitorSwitchService switchService;
    private readonly IReadOnlyList<MonitorGroup> groups;
    private string currentGroupName = string.Empty;

    public SecondaryMonitorViewModel(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups,
        IDeviceCatalog deviceCatalog)
    {
        this.switchService = switchService;
        this.groups = groups;
        this.deviceCatalog = deviceCatalog ?? throw new ArgumentNullException(nameof(deviceCatalog));
        Tiles = new ObservableCollection<VideoTileViewModel>(
            Enumerable.Range(0, 3).Select(_ => new VideoTileViewModel()));
        SwitchGroupCommand = new RelayCommand<string>(SwitchGroup);

        switchService.LayoutChanged += OnLayoutChanged;
        deviceCatalog.Changed += OnCatalogChanged;
        Render(switchService.Current);
    }

    public ObservableCollection<VideoTileViewModel> Tiles { get; }

    public IRelayCommand<string> SwitchGroupCommand { get; }

    public string CurrentGroupName
    {
        get => currentGroupName;
        private set => SetProperty(ref currentGroupName, value);
    }

    private void SwitchGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        var group = groups.Single(item => item.Name == groupName);
        switchService.SwitchUnloadingGroup(group);
    }

    private void OnLayoutChanged(object? sender, MonitorLayoutSnapshot snapshot) => Render(snapshot);

    private void OnCatalogChanged(object? sender, EventArgs e) => Render(switchService.Current);

    private void Render(MonitorLayoutSnapshot snapshot)
    {
        for (var index = 0; index < Tiles.Count; index++)
        {
            var camera = snapshot.SecondarySlots[index];
            var device = deviceCatalog.GetDevice(camera.DeviceId);
            var channel = device?.Channels.SingleOrDefault(item => item.Id == camera.ChannelId);
            Tiles[index].Update(camera, device, channel);
        }

        CurrentGroupName = snapshot.SecondarySlots[0].GroupName;
    }
}
