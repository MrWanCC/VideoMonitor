using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Wpf.Configuration;

public sealed class SingleCameraTestOptions
{
    public bool Enabled { get; set; }
}

public sealed record LocalPlaybackConfiguration(
    SingleCameraTestOptions SingleCameraTest,
    ZlmOptions Zlm,
    LocalDeviceOptions? Device);
