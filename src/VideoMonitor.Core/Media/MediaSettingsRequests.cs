namespace VideoMonitor.Core.Media;

public sealed record UpdateMediaSettingsRequest(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string? ZlmSecret,
    int NoReaderGraceSeconds,
    long ExpectedRevision);

public sealed record TestMediaSettingsRequest(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string? ZlmSecret,
    int NoReaderGraceSeconds);
