using VideoMonitor.Core.Models;

namespace VideoMonitor.Infrastructure.Hikvision;

public static class HikvisionRtspUrlBuilder
{
    public static Uri Build(CameraDevice device, CameraChannel channel)
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

        var streamSuffix = channel.StreamType == StreamType.Main ? 1 : 2;
        var channelCode = checked(channel.ChannelNo * 100 + streamSuffix);
        return new UriBuilder(
            "rtsp",
            device.IpAddress,
            device.RtspPort,
            $"Streaming/Channels/{channelCode}")
        {
            UserName = device.Username,
            Password = device.Password
        }.Uri;
    }

    public static string Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var builder = new UriBuilder(uri);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = "******";
        }

        return builder.Uri.ToString();
    }
}
