namespace VideoMonitor.Infrastructure.Persistence;

public sealed record SqliteBackupManifest(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string DatabaseSha256);
