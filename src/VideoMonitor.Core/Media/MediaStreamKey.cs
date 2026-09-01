using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Media;

public readonly record struct MediaStreamKey(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType)
{
    public string ToFormalStreamId() =>
        $"vm_{DeviceId:N}_{ChannelId:N}_{StreamType.ToString().ToLowerInvariant()}";
}
