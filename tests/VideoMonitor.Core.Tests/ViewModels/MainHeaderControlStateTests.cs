using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MainHeaderControlStateTests
{
    [Fact]
    public void ToggleSignalLinkageCommand_TogglesFalseTrueFalse()
    {
        var (main, _, _, _) = CreateFixture();

        Assert.False(main.IsSignalLinkageEnabled);

        main.ToggleSignalLinkageCommand.Execute(null);
        Assert.True(main.IsSignalLinkageEnabled);

        main.ToggleSignalLinkageCommand.Execute(null);
        Assert.False(main.IsSignalLinkageEnabled);
    }

    [Fact]
    public void ToggleSecondaryScreenCommand_TogglesVisibilityState()
    {
        var (main, _, _, _) = CreateFixture();

        Assert.False(main.IsSecondaryScreenVisible);

        main.ToggleSecondaryScreenCommand.Execute(null);
        Assert.True(main.IsSecondaryScreenVisible);

        main.ToggleSecondaryScreenCommand.Execute(null);
        Assert.False(main.IsSecondaryScreenVisible);
    }

    [Fact]
    public void HeaderToggles_DoNotChangeMonitorOrSecondarySelections()
    {
        var (main, monitor, secondary, service) = CreateFixture();
        var before = Snapshot(monitor, secondary, service);

        main.ToggleSignalLinkageCommand.Execute(null);
        main.ToggleSecondaryScreenCommand.Execute(null);
        main.ToggleSignalLinkageCommand.Execute(null);
        main.ToggleSecondaryScreenCommand.Execute(null);

        Assert.Equal(before, Snapshot(monitor, secondary, service));
    }

    [Fact]
    public void LegacyMainViewModel_HidesCentralServerUi()
    {
        var (main, _, _, _) = CreateFixture();

        Assert.False(main.IsCentralServerUiAvailable);
        Assert.Null(main.ServerStatus);
    }

    [Fact]
    public async Task CentralMainViewModel_ExposesServerStatus()
    {
        await using var fixture = CentralFixture.Create();
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var service = new MonitorSwitchService(
            Group(groups, "备用1"),
            Group(groups, "Z-1#巷"),
            Group(groups, "2#主溜井"));
        var monitor = new MonitorViewModel(service, groups, catalog);
        var main = new MainViewModel(
            monitor,
            new DeviceManagementViewModel(catalog),
            fixture.Status,
            () => new ServerSettingsViewModel(
                fixture.Coordinator,
                fixture.Settings,
                () => false));

        Assert.True(main.IsCentralServerUiAvailable);
        Assert.Same(fixture.Status, main.ServerStatus);
        Assert.NotNull(main.CreateServerSettingsViewModel());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ServerFailure_DoesNotExposeFalseHealthyServerState()
    {
        await using var fixture = await CentralFixture.CreateConnectedAsync();
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var service = new MonitorSwitchService(
            Group(groups, "备用1"),
            Group(groups, "Z-1#巷"),
            Group(groups, "2#主溜井"));
        var main = new MainViewModel(
            new MonitorViewModel(service, groups, catalog),
            new DeviceManagementViewModel(catalog),
            fixture.Status,
            () => new ServerSettingsViewModel(
                fixture.Coordinator,
                fixture.Settings,
                () => false));
        fixture.Api.CatalogHandler = (_, _) =>
            Task.FromException<CatalogSnapshotDto>(
                new CatalogApiException("CATALOG_UNAVAILABLE"));

        await fixture.Coordinator.RefreshNowAsync();

        Assert.Equal(ServerConnectionState.Unavailable, main.ServerStatus!.State);
        Assert.Equal("连接失败", main.ServerStatus.StateText);
        Assert.NotEqual("系统运行正常", main.ServerStatus.StateText);
        Assert.NotEqual("安全运行中", main.ServerStatus.StateText);
    }

    private static (MainViewModel Main, MonitorViewModel Monitor, SecondaryMonitorViewModel Secondary, MonitorSwitchService Service) CreateFixture()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var service = new MonitorSwitchService(
            Group(groups, "备用1"),
            Group(groups, "Z-1#巷"),
            Group(groups, "2#主溜井"));
        var monitor = new MonitorViewModel(service, groups, catalog);
        var secondary = new SecondaryMonitorViewModel(service, groups, catalog);
        var main = new MainViewModel(
            monitor,
            new DeviceManagementViewModel(catalog));

        return (main, monitor, secondary, service);
    }

    private static string Snapshot(
        MonitorViewModel monitor,
        SecondaryMonitorViewModel secondary,
        MonitorSwitchService service)
    {
        var mainSlots = string.Join('|', service.Current.MainSlots.Select(camera => camera.Name));
        var secondarySlots = string.Join('|', service.Current.SecondarySlots.Select(camera => camera.Name));
        return $"{monitor.CurrentChuteName};{monitor.CurrentTunnelName};{secondary.CurrentGroupName};{mainSlots};{secondarySlots}";
    }

    private static MonitorGroup Group(IReadOnlyList<MonitorGroup> groups, string name) =>
        groups.Single(group => group.Name == name);

    private sealed class CentralFixture : IAsyncDisposable
    {
        private CentralFixture()
        {
            Settings = new HeaderSettingsStore
            {
                Settings = new(new ClientServerSettings("https://server-a"))
            };
            Api = new HeaderCatalogApi();
            Dispatcher = new HeaderDispatcher();
            Cache = new ClientCatalogCache(
                new CatalogSnapshotDto([], []),
                Dispatcher);
            Coordinator = new ServerConnectionCoordinator(
                Settings,
                Api,
                Cache,
                Dispatcher,
                new HeaderClock());
            Status = new ServerStatusViewModel(Coordinator);
        }

        public HeaderSettingsStore Settings { get; }
        public HeaderCatalogApi Api { get; }
        public HeaderDispatcher Dispatcher { get; }
        public ClientCatalogCache Cache { get; }
        public ServerConnectionCoordinator Coordinator { get; }
        public ServerStatusViewModel Status { get; }

        public static CentralFixture Create() => new();

        public static async Task<CentralFixture> CreateConnectedAsync()
        {
            var fixture = new CentralFixture();
            await fixture.Coordinator.SwitchServerAsync(
                new Uri("https://server-a"),
                () => false);
            return fixture;
        }

        public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
    }

    private sealed class HeaderSettingsStore : IClientSettingsStore
    {
        public ClientSettings Settings { get; set; } = ClientSettings.Empty;

        public int SaveCount { get; private set; }

        public ClientSettings Load() => Settings;

        public Task SaveAsync(
            ClientSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class HeaderCatalogApi : ICatalogConnectionClient
    {
        public Func<Uri, CancellationToken, Task<CatalogSnapshotDto>>? CatalogHandler { get; set; }

        public Task CheckReadyAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<CatalogSnapshotDto> GetCatalogAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            CatalogHandler?.Invoke(baseUri, cancellationToken)
            ?? Task.FromResult(new CatalogSnapshotDto([], []));
    }

    private sealed class HeaderDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class HeaderClock : IClientConnectionClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public double NextJitterUnit() => 0.5;
    }
}
