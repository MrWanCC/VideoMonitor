namespace VideoMonitor.Core.Models;

public sealed class CameraChannel
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public int ChannelNo { get; set; } = 1;

    public string ChannelName { get; set; } = string.Empty;

    public StreamType StreamType { get; set; } = StreamType.Main;

    public string StreamId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
