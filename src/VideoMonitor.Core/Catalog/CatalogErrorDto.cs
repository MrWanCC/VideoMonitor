namespace VideoMonitor.Core.Catalog;

public sealed record CatalogErrorDto(
    string Code,
    string Message,
    long? CurrentRevision = null);
