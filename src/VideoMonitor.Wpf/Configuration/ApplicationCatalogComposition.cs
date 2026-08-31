using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Configuration;

public sealed class ApplicationCatalogComposition : IAsyncDisposable
{
    private readonly HttpClient? centralHttpClient;
    private readonly HttpClient? localHttpClient;
    private readonly CancellationTokenSource? centralCancellation;
    private readonly object lifecycleGate = new();
    private Task? coordinatorRunTask;
    private Task? disposalTask;

    private ApplicationCatalogComposition(
        bool isFormalCentralMode,
        IDeviceCatalogReadModel readModel,
        IDeviceCatalogCommandService commandService,
        IClientSettingsStore? clientSettings,
        ServerConnectionCoordinator? coordinator,
        ServerStatusViewModel? serverStatus,
        InMemoryDeviceCatalog? localCatalog,
        DeviceCatalogPersistenceCoordinator? persistenceCoordinator,
        IPlaybackSourceProvider? localPlaybackSource,
        HttpClient? centralHttpClient,
        HttpClient? localHttpClient,
        CancellationTokenSource? centralCancellation,
        string? catalogMigrationWarning,
        bool catalogRecoveryOccurred)
    {
        IsFormalCentralMode = isFormalCentralMode;
        ReadModel = readModel;
        CommandService = commandService;
        ClientSettingsStore = clientSettings;
        Coordinator = coordinator;
        ServerStatus = serverStatus;
        LocalCatalog = localCatalog;
        PersistenceCoordinator = persistenceCoordinator;
        LocalPlaybackSource = localPlaybackSource;
        this.centralHttpClient = centralHttpClient;
        this.localHttpClient = localHttpClient;
        this.centralCancellation = centralCancellation;
        CatalogMigrationWarning = catalogMigrationWarning;
        CatalogRecoveryOccurred = catalogRecoveryOccurred;
    }

    public sealed class Dependencies
    {
        public Func<IClientSettingsStore> ClientSettingsStoreFactory { get; init; } =
            static () => new JsonClientSettingsStore();

        public Func<HttpClient> CentralHttpClientFactory { get; init; } =
            static () => new HttpClient { Timeout = TimeSpan.FromSeconds(7) };

        public Func<HttpClient, CatalogApiClient> CatalogApiClientFactory { get; init; } =
            static httpClient => new CatalogApiClient(httpClient);

        public Func<CatalogApiClient, ICatalogConnectionClient> CatalogConnectionClientFactory { get; init; } =
            static apiClient => apiClient;

        public Func<IDeviceCatalogStore> LocalCatalogStoreFactory { get; init; } =
            static () => new JsonDeviceCatalogStore();

        public Func<IUiDispatcher> UiDispatcherFactory { get; init; } =
            static () => new WpfUiDispatcher(Dispatcher.CurrentDispatcher);

        public Func<IClientConnectionClock> ConnectionClockFactory { get; init; } =
            static () => new SystemClientConnectionClock();
    }

    public bool IsFormalCentralMode { get; }

    public IDeviceCatalogReadModel ReadModel { get; }

    public IDeviceCatalogCommandService CommandService { get; }

    public IClientSettingsStore? ClientSettingsStore { get; }

    public ServerConnectionCoordinator? Coordinator { get; }

    public ServerStatusViewModel? ServerStatus { get; }

    public InMemoryDeviceCatalog? LocalCatalog { get; }

    public DeviceCatalogPersistenceCoordinator? PersistenceCoordinator { get; }

    public IPlaybackSourceProvider? LocalPlaybackSource { get; }

    public Task? CoordinatorRunTask => coordinatorRunTask;

    public string? CatalogMigrationWarning { get; }

    public bool CatalogRecoveryOccurred { get; }

    public static async Task<ApplicationCatalogComposition> CreateAsync(
        LocalPlaybackConfiguration configuration,
        Dependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.SingleCameraTest);
        ArgumentNullException.ThrowIfNull(configuration.Zlm);
        ArgumentNullException.ThrowIfNull(dependencies);

        return configuration.SingleCameraTest.Enabled
            ? await CreateLocalAsync(configuration, dependencies, cancellationToken)
                .ConfigureAwait(false)
            : CreateCentral(dependencies);
    }

    public void StartCentralCoordinator()
    {
        if (!IsFormalCentralMode)
        {
            throw new InvalidOperationException(
                "Central Server coordinator is not available in local mode.");
        }

        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposalTask is not null, this);
            coordinatorRunTask ??= Coordinator!.RunAsync(centralCancellation!.Token);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new ValueTask(disposalTask);
        }
    }

    private static ApplicationCatalogComposition CreateCentral(
        Dependencies dependencies)
    {
        var settingsStore = dependencies.ClientSettingsStoreFactory()
            ?? throw new InvalidOperationException("Client settings store factory returned null.");
        var httpClient = dependencies.CentralHttpClientFactory()
            ?? throw new InvalidOperationException("Central HTTP client factory returned null.");
        var apiClient = dependencies.CatalogApiClientFactory(httpClient)
            ?? throw new InvalidOperationException("Catalog API client factory returned null.");
        var connectionClient = dependencies.CatalogConnectionClientFactory(apiClient)
            ?? throw new InvalidOperationException("Catalog connection client factory returned null.");
        var dispatcher = dependencies.UiDispatcherFactory()
            ?? throw new InvalidOperationException("UI dispatcher factory returned null.");
        var cache = new ClientCatalogCache(
            new CatalogSnapshotDto([], []),
            dispatcher);
        var coordinator = new ServerConnectionCoordinator(
            settingsStore,
            connectionClient,
            cache,
            dispatcher,
            dependencies.ConnectionClockFactory());
        var commandService = new RemoteDeviceCatalogCommandService(
            cache,
            apiClient,
            coordinator);
        var serverStatus = new ServerStatusViewModel(coordinator);

        return new ApplicationCatalogComposition(
            true,
            cache,
            commandService,
            settingsStore,
            coordinator,
            serverStatus,
            null,
            null,
            null,
            httpClient,
            null,
            new CancellationTokenSource(),
            null,
            false);
    }

    private static async Task<ApplicationCatalogComposition> CreateLocalAsync(
        LocalPlaybackConfiguration configuration,
        Dependencies dependencies,
        CancellationToken cancellationToken)
    {
        var store = dependencies.LocalCatalogStoreFactory()
            ?? throw new InvalidOperationException("Local catalog store factory returned null.");
        var bootstrapper = new DeviceCatalogBootstrapper(store);
        var catalog = await bootstrapper
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
        var readModel = new LegacyDeviceCatalogReadModel(catalog);
        var commandService = new LegacyDeviceCatalogCommandService(catalog);
        var persistenceCoordinator = new DeviceCatalogPersistenceCoordinator(
            catalog,
            store);
        var localHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(7)
        };
        var zlmClient = new ZlmClient(localHttpClient, configuration.Zlm);
        var playbackSource = new LocalZlmPlaybackSourceProvider(
            catalog,
            zlmClient,
            configuration.Zlm,
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(250));

        return new ApplicationCatalogComposition(
            false,
            readModel,
            commandService,
            null,
            null,
            null,
            catalog,
            persistenceCoordinator,
            playbackSource,
            null,
            localHttpClient,
            null,
            bootstrapper.LastMigrationWarning,
            bootstrapper.RecoveryOccurred);
    }

    private async Task DisposeCoreAsync()
    {
        centralCancellation?.Cancel();
        var exceptions = new List<Exception>();

        try
        {
            if (CommandService is IDisposable disposableCommandService)
            {
                disposableCommandService.Dispose();
            }
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }

        try
        {
            ServerStatus?.Dispose();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }

        try
        {
            if (Coordinator is not null)
            {
                await Coordinator.DisposeAsync().ConfigureAwait(false);
            }

            if (PersistenceCoordinator is not null)
            {
                await PersistenceCoordinator.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
        finally
        {
            centralHttpClient?.Dispose();
            localHttpClient?.Dispose();
            centralCancellation?.Dispose();
        }

        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException("Application catalog composition cleanup failed.", exceptions);
        }
    }
}
