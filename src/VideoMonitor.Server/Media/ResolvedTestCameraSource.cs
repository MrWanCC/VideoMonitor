namespace VideoMonitor.Server.Media;

public sealed record ResolvedTestCameraSource(
    Uri SourceUri,
    Guid? ExistingDeviceId,
    Guid? ExistingChannelId,
    int ChannelNo,
    VideoMonitor.Core.Models.StreamType StreamType);
