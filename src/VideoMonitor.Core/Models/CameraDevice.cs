namespace VideoMonitor.Core.Models;

public sealed class CameraDevice
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid GroupId { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public int SdkPort { get; set; } = 8000;

    public int RtspPort { get; set; } = 554;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public TransportMode TransportMode { get; set; } = TransportMode.Auto;

    public CameraStatus Status { get; set; } = CameraStatus.Online;

    public bool Enabled { get; set; } = true;

    public string Remark { get; set; } = string.Empty;

    public List<CameraChannel> Channels { get; } = [];
}
