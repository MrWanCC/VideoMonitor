namespace VideoMonitor.Core.Models;

public sealed class DeviceGroup
{
    public Guid Id { get; set; }

    public long Revision { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public int Sort { get; set; }

    public bool Enabled { get; set; } = true;

    public MonitorGroupType? Kind { get; set; }
}
