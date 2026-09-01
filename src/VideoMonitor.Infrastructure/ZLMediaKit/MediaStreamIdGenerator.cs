using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public static class MediaStreamIdGenerator
{
    public static string GenerateFormal(MediaStreamKey key) =>
        key.ToFormalStreamId();

    public static bool TryParseFormal(
        string value,
        out MediaStreamKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split('_');
        if (parts.Length != 4
            || !string.Equals(parts[0], "vm", StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[1], "N", out var deviceId)
            || !Guid.TryParseExact(parts[2], "N", out var channelId))
        {
            return false;
        }

        var streamType = parts[3] switch
        {
            "main" => StreamType.Main,
            "sub" => StreamType.Sub,
            _ => (StreamType?)null
        };
        if (streamType is null)
        {
            return false;
        }

        key = new MediaStreamKey(deviceId, channelId, streamType.Value);
        return true;
    }
}
