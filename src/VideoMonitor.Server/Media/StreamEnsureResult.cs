namespace VideoMonitor.Server.Media;

public sealed record StreamEnsureResult(
    bool IsSuccess,
    FormalStreamDescriptor? Stream,
    string? FailureCode);
