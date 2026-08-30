namespace VideoMonitor.Infrastructure.Persistence;

public enum CatalogRepositoryStatus
{
    Success,
    NotFound,
    RevisionConflict,
    GroupNotEmpty,
    ChannelConflict
}

public sealed record CatalogRepositoryResult<T>(
    CatalogRepositoryStatus Status,
    T? Value = default,
    long? CurrentRevision = null);

public sealed record CatalogRepositoryDeleteResult(
    CatalogRepositoryStatus Status,
    long? CurrentRevision = null);
