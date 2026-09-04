using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;
using VideoMonitor.Server.Media;
using VideoMonitor.Server.Playback;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "VideoMonitor.Server";
});

builder.Services.AddSingleton(new ServerStorageOptions
{
    RootPath = builder.Configuration["Storage:RootPath"]
});
builder.Services.AddSingleton<IAppPathProvider>(serviceProvider =>
    new DefaultAppPathProvider(
        serviceProvider.GetRequiredService<ServerStorageOptions>()));
builder.Services.AddSingleton<ServerStorageLayout>();

builder.Services.AddSingleton<IMachineSecretProtector>(_ =>
    OperatingSystem.IsWindows()
        ? new DpapiMachineSecretProtector()
        : new UnsupportedMachineSecretProtector());
builder.Services.AddSingleton<IMasterKeyProvider, MasterKeyProvider>();
builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<SqliteDatabaseInitializer>();
builder.Services.AddSingleton<IDeviceCatalogStore, SqliteDeviceCatalogStore>();
builder.Services.AddSingleton<
    ICentralCatalogRepository,
    SqliteCentralCatalogRepository>();
builder.Services.AddSingleton<IMediaSettingsRepository, SqliteMediaSettingsRepository>();
builder.Services.AddSingleton<
    IMediaRuntimeSettingsProvider,
    SqliteMediaRuntimeSettingsProvider>();
builder.Services.AddSingleton<ZlmServerHttpTransport>();
builder.Services.AddSingleton<IZlmMediaGateway>(serviceProvider =>
    new ZlmClient(
        serviceProvider.GetRequiredService<ZlmServerHttpTransport>(),
        serviceProvider.GetRequiredService<IMediaRuntimeSettingsProvider>()));
builder.Services.AddSingleton<
    ICameraMediaCredentialReader,
    SqliteCameraMediaCredentialReader>();
builder.Services.AddSingleton<ICameraSourceResolver, CameraSourceResolver>();
builder.Services.AddSingleton<ITestCameraSourceResolver, TestCameraSourceResolver>();
builder.Services.AddSingleton<ITestStreamProxyController, TestStreamProxyController>();
builder.Services.AddSingleton<TestSessionRegistry>();
builder.Services.AddSingleton<ITestStreamService, TestStreamService>();
builder.Services.AddSingleton<TestStreamOrphanReconcileContributor>();
builder.Services.AddSingleton<SourceBindingVerifier>();
builder.Services.AddSingleton<MediaRuntimeRegistry>();
builder.Services.AddSingleton<IMediaRuntimeStore>(serviceProvider =>
    serviceProvider.GetRequiredService<MediaRuntimeRegistry>());
builder.Services.AddSingleton<IMediaObservationRecorder>(serviceProvider =>
    serviceProvider.GetRequiredService<MediaRuntimeRegistry>());
builder.Services.AddSingleton<MediaServerHealthState>();
builder.Services.AddSingleton<MediaStreamGate>();
builder.Services.AddSingleton<MediaOwnershipClassifier>();
builder.Services.AddSingleton<StreamManager>();
builder.Services.AddSingleton<IStreamManager>(serviceProvider =>
    serviceProvider.GetRequiredService<StreamManager>());
builder.Services.AddSingleton<IMediaReconcileContributor>(serviceProvider =>
    serviceProvider.GetRequiredService<StreamManager>());
builder.Services.AddSingleton<IMediaReconcileContributor>(serviceProvider =>
    serviceProvider.GetRequiredService<TestStreamOrphanReconcileContributor>());
builder.Services.AddSingleton<IZlmHookTrustPolicy, LoopbackZlmHookTrustPolicy>();
builder.Services.AddSingleton<MediaEventProcessor>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<MediaEventProcessor>());
builder.Services.AddSingleton<MediaReconcilerHostedService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<MediaReconcilerHostedService>());
builder.Services.AddSingleton<CatalogApplicationService>();
builder.Services.AddSingleton<IMediaSettingsProbe, MediaSettingsProbe>();
builder.Services.AddSingleton<IMediaSettingsService, MediaSettingsService>();
builder.Services.AddSingleton<ISqliteBackupService, SqliteBackupService>();
builder.Services.AddSingleton<
    IPlaybackSigningKeyProvider,
    SqlitePlaybackSigningKeyProvider>();
builder.Services.AddSingleton<IPlaybackTicketIssuer, PlaybackTicketIssuer>();
builder.Services.AddSingleton<PlaybackTicketValidator>();
builder.Services.AddSingleton<IPlaybackTicketValidator>(serviceProvider =>
    serviceProvider.GetRequiredService<PlaybackTicketValidator>());
builder.Services.AddSingleton<IPlaybackUrlBuilder, PlaybackUrlBuilder>();
builder.Services.AddSingleton<
    IFormalStreamEnsureService,
    FormalStreamEnsureService>();
builder.Services.AddSingleton<IPlaybackStreamService, PlaybackStreamService>();
builder.Services.AddSingleton<ServerReadinessState>();
builder.Services.AddHostedService<ServerInitializationHostedService>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live"
}));

app.MapGet("/health/ready", (ServerReadinessState readiness) =>
{
    var response = new
    {
        status = readiness.IsReady ? "ready" : "not-ready",
        databaseReady = readiness.DatabaseReady,
        secretProtectionReady = readiness.SecretProtectionReady
    };

    return readiness.IsReady
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapCatalogEndpoints();
app.MapMediaSettingsEndpoints();
app.MapMediaRuntimeEndpoints();
app.MapMediaHookEndpoints();
app.MapPlaybackAuthorizationEndpoints();
app.MapTestStreamEndpoints();

app.Run();

public partial class Program;
