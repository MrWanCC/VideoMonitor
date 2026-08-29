namespace VideoMonitor.Infrastructure.Persistence;

public sealed record SqliteBackupResult(
    string DirectoryPath,
    string DatabasePath,
    string ManifestPath,
    string DatabaseSha256);
