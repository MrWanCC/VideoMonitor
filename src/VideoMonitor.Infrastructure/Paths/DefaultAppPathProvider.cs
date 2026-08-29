namespace VideoMonitor.Infrastructure.Paths;

public sealed class DefaultAppPathProvider : IAppPathProvider
{
    public DefaultAppPathProvider(ServerStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredRoot = options.RootPath;
        RootDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredRoot)
                ? GetPlatformDefaultRoot()
                : configuredRoot);

        DataDirectory = Path.Combine(RootDirectory, "data");
        DatabasePath = Path.Combine(DataDirectory, "videomonitor.db");
        SecurityDirectory = Path.Combine(RootDirectory, "security");
        MasterKeyPath = Path.Combine(SecurityDirectory, "master-key.protected");
        BackupsDirectory = Path.Combine(RootDirectory, "backups");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        SettingsPath = Path.Combine(RootDirectory, "server-settings.json");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string SecurityDirectory { get; }
    public string MasterKeyPath { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsPath { get; }

    private static string GetPlatformDefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VideoMonitor",
                "Server");
        }

        if (OperatingSystem.IsLinux())
        {
            return "/var/lib/videomonitor/server";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoMonitor",
            "Server");
    }
}
