namespace VideoMonitor.Infrastructure.Persistence;

public sealed record MediaRuntimeSettings(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string ZlmSecret,
    int NoReaderGraceSeconds,
    long Revision);
