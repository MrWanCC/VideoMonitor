using System.Text.Json.Serialization;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Catalog;

public sealed record DeviceGroupDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Sort,
    bool Enabled,
    MonitorGroupType? Kind,
    long Revision)
{
    [JsonConstructor]
    public DeviceGroupDto(
        Guid id,
        string name,
        Guid? parentId,
        int sort,
        bool enabled,
        long revision)
        : this(id, name, parentId, sort, enabled, null, revision)
    {
    }
}
