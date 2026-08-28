using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Configuration;

public sealed class LocalDeviceOptions
{
    public Guid DeviceId { get; set; } =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    public Guid ChannelId { get; set; } =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

    public string LocalIdentifier { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int RtspPort { get; set; } = 554;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int ChannelNo { get; set; } = 1;

    public StreamType StreamType { get; set; } = StreamType.Main;
}
