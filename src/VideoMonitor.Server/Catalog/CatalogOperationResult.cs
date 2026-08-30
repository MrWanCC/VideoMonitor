using VideoMonitor.Core.Catalog;

namespace VideoMonitor.Server.Catalog;

public sealed record CatalogOperationResult<T>(
    bool IsSuccess,
    T? Value,
    int StatusCode,
    CatalogErrorDto? Error);
