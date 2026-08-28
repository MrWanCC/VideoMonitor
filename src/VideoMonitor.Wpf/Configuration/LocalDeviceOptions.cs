using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Configuration;

public sealed class LocalDeviceOptions
{
    public string LocalIdentifier { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int RtspPort { get; set; } = 554;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int ChannelNo { get; set; } = 1;

    public StreamType StreamType { get; set; } = StreamType.Main;
}
