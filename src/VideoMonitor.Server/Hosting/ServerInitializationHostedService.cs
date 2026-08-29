using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Server.Hosting;

public sealed class ServerInitializationHostedService : IHostedService
{
    private readonly ServerStorageLayout storageLayout;
    private readonly SqliteDatabaseInitializer databaseInitializer;
    private readonly IMasterKeyProvider masterKeyProvider;
    private readonly ServerReadinessState readiness;
    private readonly ILogger<ServerInitializationHostedService> logger;

    public ServerInitializationHostedService(
        ServerStorageLayout storageLayout,
        SqliteDatabaseInitializer databaseInitializer,
        IMasterKeyProvider masterKeyProvider,
        ServerReadinessState readiness,
        ILogger<ServerInitializationHostedService> logger)
    {
        this.storageLayout = storageLayout ?? throw new ArgumentNullException(nameof(storageLayout));
        this.databaseInitializer = databaseInitializer ??
            throw new ArgumentNullException(nameof(databaseInitializer));
        this.masterKeyProvider = masterKeyProvider ??
            throw new ArgumentNullException(nameof(masterKeyProvider));
        this.readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            storageLayout.EnsureCreated();
            await databaseInitializer.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            readiness.MarkDatabaseReady();

            await masterKeyProvider.GetOrCreateAsync(cancellationToken)
                .ConfigureAwait(false);
            readiness.MarkSecretProtectionReady();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "VideoMonitor Server initialization failed. ExceptionType={ExceptionType}",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
