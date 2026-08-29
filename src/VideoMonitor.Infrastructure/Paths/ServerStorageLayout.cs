namespace VideoMonitor.Infrastructure.Paths;

public sealed class ServerStorageLayout
{
    private readonly IAppPathProvider paths;

    public ServerStorageLayout(IAppPathProvider paths)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(paths.RootDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.SecurityDirectory);
        Directory.CreateDirectory(paths.BackupsDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
    }
}
