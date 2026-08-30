namespace VideoMonitor.Core.Catalog;

public sealed record DeviceGroupDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Sort,
    bool Enabled,
    long Revision);
