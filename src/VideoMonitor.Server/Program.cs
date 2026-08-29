using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;
using VideoMonitor.Server.Hosting;

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
builder.Services.AddSingleton<ISqliteBackupService, SqliteBackupService>();
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

app.Run();

public partial class Program;
