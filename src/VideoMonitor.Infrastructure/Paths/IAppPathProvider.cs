namespace VideoMonitor.Infrastructure.Paths;

public interface IAppPathProvider
{
    string RootDirectory { get; }
    string DataDirectory { get; }
    string DatabasePath { get; }
    string SecurityDirectory { get; }
    string MasterKeyPath { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string SettingsPath { get; }
}
