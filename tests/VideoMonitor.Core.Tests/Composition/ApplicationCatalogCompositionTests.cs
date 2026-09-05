using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Composition;

[Collection("Wpf")]
public sealed class ApplicationCatalogCompositionTests
{
    private static readonly SemaphoreSlim WpfGate = new(1, 1);

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
    public async Task FormalMode_ExposesMediaSettingsApiClient()
    {
        await using var formal = await CreateAsync(false);
        await using var singleCamera = await CreateAsync(true);

        Assert.NotNull(formal.MediaSettingsApiClient);
        Assert.Null(singleCamera.MediaSettingsApiClient);
    }

    [Fact]
    public async Task CentralCompositionProvidesDiagnosticsButLocalModeDoesNot()
    {
        await using var central = await CreateAsync(false);
        await using var local = await CreateAsync(true);

        Assert.NotNull(central.MediaDiagnosticsApiClient);
        Assert.Null(local.MediaDiagnosticsApiClient);
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

    [Fact]
    public async Task TestPreviewDisposalThroughApplicationCompositionStaysOnUiDispatcher()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var engine = new ThreadRecordingPlaybackEngine(dispatcher);
            var httpClient = new HttpClient(
                new TestPreviewHttpMessageHandler());
            var dependencies = new TestDependencies
            {
                Settings = new TestClientSettingsStore(
                    new ClientSettings(
                        new ClientServerSettings("https://server-a"))),
                CentralHttpClient = httpClient,
                ConnectionClient = new CatalogApiClient(httpClient),
                UiDispatcher = new WpfUiDispatcher(dispatcher),
                CentralPlaybackEngine = engine
            };

            await using var composition = await CreateAsync(false, dependencies);
            composition.StartCentralCoordinator();
            await EventuallyAsync(() =>
                composition.Coordinator!.Status.State
                    == ServerConnectionState.Connected);

            var propertyChangedOnUi = new List<bool>();
            composition.TestPreview!.PropertyChanged += (_, _) =>
                propertyChangedOnUi.Add(dispatcher.CheckAccess());

            await composition.TestPreview.StartAsync(
                new TestStreamStartRequest(
                    null,
                    null,
                    new CameraDeviceDraftDto(
                        "10.0.0.5",
                        554,
                        "admin",
                        "secret",
                        1,
                        StreamType.Main,
                        TransportMode.Auto),
                    DateTimeOffset.UtcNow));

            await Task.Run(async () => await composition.DisposeAsync());

            Assert.True(engine.StartedOnUi);
            Assert.True(engine.StoppedOnUi);
            Assert.True(engine.DisposedOnUi);
            Assert.NotEmpty(propertyChangedOnUi);
            Assert.All(propertyChangedOnUi, Assert.True);
            Assert.Equal(TestPreviewState.Idle, composition.TestPreview.State);
            Assert.Null(composition.TestPreview.Session);
        });
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

        public IUiDispatcher? UiDispatcher { get; init; }

        public IPlaybackEngine? CentralPlaybackEngine { get; init; }

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
                UiDispatcherFactory = () => UiDispatcher ?? new InlineDispatcher(),
                CentralPlaybackEngineFactory = () => CentralPlaybackEngine
                    ?? throw new InvalidOperationException(
                        "Central playback engine was not configured."),
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

    private sealed class TestPreviewHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == "/health/ready")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
            }

            if (path == "/api/v1/catalog")
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    JsonSerializer.Serialize(new CatalogSnapshotDto([], []))));
            }

            if (request.Method == HttpMethod.Post
                && path == "/api/v1/test-streams")
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    JsonSerializer.Serialize(new TestSessionDto(
                        Guid.Parse("94000000-0000-0000-0000-000000000001"),
                        null,
                        null,
                        "videomonitor-test",
                        "test_0123456789abcdef0123456789abcdef",
                        new Uri("rtsp://playback.example/live"),
                        DateTimeOffset.UtcNow.AddMinutes(2)))));
            }

            if (request.Method == HttpMethod.Delete
                && path?.StartsWith("/api/v1/test-streams/", StringComparison.Ordinal)
                    == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(
            HttpStatusCode statusCode,
            string content) =>
            new(statusCode)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed class ThreadRecordingPlaybackEngine : IPlaybackEngine, IDisposable
    {
        private readonly Dispatcher dispatcher;

        public ThreadRecordingPlaybackEngine(Dispatcher dispatcher) =>
            this.dispatcher = dispatcher;

        public bool StartedOnUi { get; private set; }

        public bool StoppedOnUi { get; private set; }

        public bool DisposedOnUi { get; private set; }

        public PlaybackSession Start(PlaybackSource source)
        {
            StartedOnUi = dispatcher.CheckAccess();
            return new PlaybackSession(source, null, null);
        }

        public void Stop(PlaybackSession session)
        {
            StoppedOnUi = dispatcher.CheckAccess();
            session.Dispose();
        }

        public void Dispose() => DisposedOnUi = dispatcher.CheckAccess();
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        await WpfGate.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                Exception? failure = null;
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(dispatcher));
                    _ = RunAsync();
                    Dispatcher.Run();

                    if (failure is null)
                    {
                        completion.SetResult(null);
                    }
                    else
                    {
                        completion.SetException(failure);
                    }

                    async Task RunAsync()
                    {
                        try
                        {
                            await action();
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                        finally
                        {
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                        }
                    }
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            })
            {
                IsBackground = true
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task;
        }
        finally
        {
            WpfGate.Release();
        }
    }
}
