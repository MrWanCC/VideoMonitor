using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Composition;

public sealed class ApplicationCatalogCompositionTests
{
    [Fact]
    public async Task FormalMode_DoesNotInstantiateJsonCompatibilityPath()
    {
        var dependencies = new TestDependencies();

        await using var composition = await CreateAsync(
            singleCameraEnabled: false,
            dependencies);

        Assert.True(composition.IsFormalCentralMode);
        Assert.IsType<ClientCatalogCache>(composition.ReadModel);
        Assert.Null(composition.LocalCatalog);
        Assert.Null(composition.PersistenceCoordinator);
        Assert.Null(composition.LocalPlaybackSource);
        Assert.False(dependencies.LocalStoreCreated);
    }

    [Fact]
    public async Task FormalMode_UsesClientCatalogCache()
    {
        await using var composition = await CreateAsync(false);

        Assert.IsType<ClientCatalogCache>(composition.ReadModel);
    }

    [Fact]
    public async Task FormalMode_UsesRemoteDeviceCatalogCommandService()
    {
        await using var composition = await CreateAsync(false);

        Assert.IsType<RemoteDeviceCatalogCommandService>(
            composition.CommandService);
    }

    [Fact]
    public async Task FormalMode_ExposesCentralCoordinatorAndStatus()
    {
        await using var composition = await CreateAsync(false);

        Assert.NotNull(composition.Coordinator);
        Assert.IsType<ServerStatusViewModel>(composition.ServerStatus);
        Assert.NotNull(composition.ClientSettingsStore);
    }

    [Fact]
    public async Task FormalMode_DoesNotCreatePersistenceCoordinator()
    {
        await using var composition = await CreateAsync(false);

        Assert.Null(composition.PersistenceCoordinator);
    }

    [Fact]
    public async Task FormalMode_DoesNotCreateLocalPlaybackSource()
    {
        await using var composition = await CreateAsync(false);

        Assert.Null(composition.LocalPlaybackSource);
    }

    [Fact]
    public async Task FormalMode_StartsWithEmptySafeCatalog()
    {
        await using var composition = await CreateAsync(false);

        Assert.Empty(composition.ReadModel.GetGroups());
        Assert.Empty(composition.ReadModel.GetDevices(Guid.Empty));
        Assert.Empty(((ClientCatalogCache)composition.ReadModel).Snapshot.Devices);
    }

    [Fact]
    public async Task FormalMode_ServerOffline_DoesNotRequireJsonFallback()
    {
        var dependencies = new TestDependencies
        {
            Settings = new TestClientSettingsStore(
                new ClientSettings(new ClientServerSettings("https://server-a"))),
            ConnectionClient = new OfflineCatalogConnectionClient()
        };

        await using var composition = await CreateAsync(false, dependencies);
        composition.StartCentralCoordinator();

        await EventuallyAsync(() =>
            composition.Coordinator!.Status.State
                == ServerConnectionState.Unavailable);

        Assert.IsType<ClientCatalogCache>(composition.ReadModel);
        Assert.Empty(composition.ReadModel.GetGroups());
        Assert.Null(composition.LocalCatalog);
        Assert.Null(composition.PersistenceCoordinator);
    }

    [Fact]
    public async Task SingleCameraTest_UsesLocalCatalogCompatibilityPath()
    {
        await using var composition = await CreateAsync(true);

        Assert.False(composition.IsFormalCentralMode);
        Assert.NotNull(composition.LocalCatalog);
        Assert.IsType<LegacyDeviceCatalogReadModel>(composition.ReadModel);
        Assert.IsType<LegacyDeviceCatalogCommandService>(
            composition.CommandService);
    }

    [Fact]
    public async Task SingleCameraTest_DoesNotCreateCentralCoordinator()
    {
        await using var composition = await CreateAsync(true);

        Assert.Null(composition.Coordinator);
        Assert.Null(composition.ServerStatus);
    }

    [Fact]
    public async Task SingleCameraTest_DoesNotCreateRemoteCommandService()
    {
        await using var composition = await CreateAsync(true);

        Assert.IsNotType<RemoteDeviceCatalogCommandService>(
            composition.CommandService);
    }

    [Fact]
    public async Task SingleCameraTest_OwnsPersistenceCoordinator()
    {
        await using var composition = await CreateAsync(true);

        Assert.NotNull(composition.PersistenceCoordinator);
    }

    [Fact]
    public async Task SingleCameraTest_PreservesLocalPlaybackSource()
    {
        await using var composition = await CreateAsync(true);

        Assert.IsType<LocalZlmPlaybackSourceProvider>(
            composition.LocalPlaybackSource);
    }

    [Fact]
    public async Task FormalShutdown_DisposesCentralResourcesOnce()
    {
        var handler = new TrackingHttpMessageHandler();
        var dependencies = new TestDependencies
        {
            CentralHttpClient = new HttpClient(handler)
        };

        await using var composition = await CreateAsync(false, dependencies);
        await composition.DisposeAsync();
        await composition.DisposeAsync();

        Assert.Equal(1, handler.DisposeCount);
    }

    [Fact]
    public async Task LocalShutdown_DisposesPersistenceOnce()
    {
        var dependencies = new TestDependencies();
        await using var composition = await CreateAsync(true, dependencies);
        var catalog = composition.LocalCatalog!;
        var savesBeforeDispose = dependencies.LocalStore!.SaveCount;

        await composition.DisposeAsync();
        await composition.DisposeAsync();

        catalog.AddGroup(new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "after-shutdown"
        });

        Assert.Equal(savesBeforeDispose, dependencies.LocalStore.SaveCount);
    }

    [Fact]
    public async Task FormalEmptyCatalog_CanConstructShellViewModels()
    {
        await using var composition = await CreateAsync(false);
        var groups = MonitorCatalogProjection.CreateGroups(composition.ReadModel);
        var switchService = new MonitorSwitchService(groups);

        var monitor = new MonitorViewModel(
            switchService,
            composition.ReadModel);
        var deviceManagement = new DeviceManagementViewModel(
            composition.ReadModel,
            composition.CommandService!);
        var secondary = new SecondaryMonitorViewModel(
            switchService,
            composition.ReadModel);

        Assert.Empty(groups);
        Assert.Equal(4, monitor.MainTiles.Count);
        Assert.Equal(3, secondary.Tiles.Count);
        Assert.Empty(deviceManagement.CatalogGroups);
    }

    private static Task<ApplicationCatalogComposition> CreateAsync(
        bool singleCameraEnabled,
        TestDependencies? dependencies = null) =>
        ApplicationCatalogComposition.CreateAsync(
            new LocalPlaybackConfiguration(
                new SingleCameraTestOptions
                {
                    Enabled = singleCameraEnabled
                },
                new ZlmOptions()),
            (dependencies ?? new TestDependencies()).Build());

    private static async Task EventuallyAsync(
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class TestDependencies
    {
        public bool LocalStoreCreated { get; private set; }

        public TrackingDeviceCatalogStore? LocalStore { get; private set; }

        public IClientSettingsStore? Settings { get; init; }

        public ICatalogConnectionClient? ConnectionClient { get; init; }

        public HttpClient? CentralHttpClient { get; init; }

        public ApplicationCatalogComposition.Dependencies Build() =>
            new()
            {
                ClientSettingsStoreFactory = () =>
                    Settings ?? new TestClientSettingsStore(ClientSettings.Empty),
                CentralHttpClientFactory = () =>
                    CentralHttpClient ?? new HttpClient(
                        new TrackingHttpMessageHandler()),
                CatalogConnectionClientFactory = _ =>
                    ConnectionClient ?? new OfflineCatalogConnectionClient(),
                LocalCatalogStoreFactory = () =>
                {
                    LocalStoreCreated = true;
                    return LocalStore = new TrackingDeviceCatalogStore();
                },
                UiDispatcherFactory = () => new InlineDispatcher(),
                ConnectionClockFactory = () => new TestConnectionClock()
            };
    }

    private sealed class OfflineCatalogConnectionClient : ICatalogConnectionClient
    {
        public Task CheckReadyAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));

        public Task<CatalogSnapshotDto> GetCatalogAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CatalogSnapshotDto>(
                new CatalogApiException("CATALOG_UNAVAILABLE"));
    }

    private sealed class TestClientSettingsStore : IClientSettingsStore
    {
        public TestClientSettingsStore(ClientSettings settings)
        {
            Settings = settings;
        }

        public ClientSettings Settings { get; }

        public ClientSettings Load() => Settings;

        public Task SaveAsync(
            ClientSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TrackingDeviceCatalogStore : IDeviceCatalogStore
    {
        private DeviceCatalogSnapshot? snapshot;

        public int SaveCount { get; private set; }

        public Task<DeviceCatalogSnapshot?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveAsync(
            DeviceCatalogSnapshot next,
            CancellationToken cancellationToken = default)
        {
            snapshot = next;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InlineDispatcher : IUiDispatcher
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

    private sealed class TestConnectionClock : IClientConnectionClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.Delay(delay, cancellationToken);

        public double NextJitterUnit() => 0.5;
    }

    private sealed class TrackingHttpMessageHandler : HttpMessageHandler
    {
        public int DisposeCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
