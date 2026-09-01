namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed record ZlmMediaEvidence(
    string Schema,
    string Vhost,
    string App,
    string Stream,
    int? OriginType,
    string? OriginTypeStr,
    string? OriginUrl,
    long? CreateStamp,
    long? AliveSecond,
    int TotalReaderCount);
