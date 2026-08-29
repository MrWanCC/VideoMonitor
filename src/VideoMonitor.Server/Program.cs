var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "VideoMonitor.Server";
});

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live"
}));

app.Run();

public partial class Program;
