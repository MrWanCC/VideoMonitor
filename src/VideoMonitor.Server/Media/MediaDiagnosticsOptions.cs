namespace VideoMonitor.Server.Media;

public sealed class MediaDiagnosticsOptions
{
    public const int DefaultFreshnessSeconds = 90;

    public int FreshnessSeconds { get; set; } = DefaultFreshnessSeconds;
}
