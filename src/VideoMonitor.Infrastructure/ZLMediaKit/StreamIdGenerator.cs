using VideoMonitor.Core.Models;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public static class StreamIdGenerator
{
    public static string Generate(CameraDevice device, CameraChannel channel)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.ChannelNo < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel.ChannelNo,
                "通道号必须大于零。");
        }

        return $"device_{device.Id:N}_channel_{channel.ChannelNo}_{channel.StreamType.ToString().ToLowerInvariant()}";
    }
}
