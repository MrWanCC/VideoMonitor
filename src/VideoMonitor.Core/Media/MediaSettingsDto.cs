namespace VideoMonitor.Core.Media;

public sealed record MediaSettingsDto(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    bool HasSecret,
    int NoReaderGraceSeconds,
    long Revision);

public sealed record MediaSettingsTestResult(
    bool IsReachable,
    string? FailureCode);
