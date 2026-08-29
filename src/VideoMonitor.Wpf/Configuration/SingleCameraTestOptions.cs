using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Wpf.Configuration;

public sealed class SingleCameraTestOptions
{
    public bool Enabled { get; set; }

    public Guid DeviceId { get; set; } =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    public Guid ChannelId { get; set; } =
        Guid.Parse("60000000-0000-0000-0000-000000000001");
}

public sealed record LocalPlaybackConfiguration(
    SingleCameraTestOptions SingleCameraTest,
    ZlmOptions Zlm);
