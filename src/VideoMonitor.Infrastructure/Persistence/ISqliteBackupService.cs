namespace VideoMonitor.Infrastructure.Persistence;

public interface ISqliteBackupService
{
    Task<SqliteBackupResult> CreateBackupAsync(
        CancellationToken cancellationToken = default);
}
