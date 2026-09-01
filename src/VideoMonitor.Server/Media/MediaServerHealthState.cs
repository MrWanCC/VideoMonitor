using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed class MediaServerHealthState
{
    private int health = (int)MediaServerHealth.Unconfigured;

    public MediaServerHealth Health =>
        (MediaServerHealth)Volatile.Read(ref health);

    public void MarkUnconfigured() => Set(MediaServerHealth.Unconfigured);

    public void MarkHealthy() => Set(MediaServerHealth.Healthy);

    public void MarkUnavailable() => Set(MediaServerHealth.Unavailable);

    public void MarkConfigurationError() => Set(MediaServerHealth.ConfigurationError);

    private void Set(MediaServerHealth value) =>
        Volatile.Write(ref health, (int)value);
}
