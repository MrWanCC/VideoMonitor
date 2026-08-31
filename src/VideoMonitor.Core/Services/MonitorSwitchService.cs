using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public sealed class MonitorSwitchService
{
    private IReadOnlyList<MonitorGroup> groups;
    private Guid? selectedChuteGroupId;
    private Guid? selectedTunnelGroupId;
    private Guid? selectedUnloadingGroupId;

    public MonitorSwitchService(IReadOnlyList<MonitorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        this.groups = groups.ToArray();
        selectedChuteGroupId = FindDefaultGroupId(MonitorGroupType.Chute);
        selectedTunnelGroupId = FindDefaultGroupId(MonitorGroupType.Tunnel);
        selectedUnloadingGroupId = FindDefaultGroupId(MonitorGroupType.UnloadingStation);
        CurrentLayout = CreateLayout();
    }

    // Compatibility constructor for the pre-central WPF path.
    public MonitorSwitchService(
        MonitorGroup defaultChuteGroup,
        MonitorGroup defaultTunnelGroup,
        MonitorGroup defaultUnloadingGroup)
        : this(new[] { defaultChuteGroup, defaultTunnelGroup, defaultUnloadingGroup })
    {
    }

    public MonitorLayoutSnapshot CurrentLayout { get; private set; }

    public MonitorLayoutSnapshot Current => CurrentLayout;

    public Guid? SelectedChuteGroupId => selectedChuteGroupId;

    public Guid? SelectedTunnelGroupId => selectedTunnelGroupId;

    public Guid? SelectedUnloadingGroupId => selectedUnloadingGroupId;

    public event EventHandler<MonitorLayoutSnapshot>? LayoutChanged;

    public void ReplaceGroups(IReadOnlyList<MonitorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        this.groups = groups.ToArray();
        selectedChuteGroupId = PreserveOrFindDefault(
            selectedChuteGroupId,
            MonitorGroupType.Chute);
        selectedTunnelGroupId = PreserveOrFindDefault(
            selectedTunnelGroupId,
            MonitorGroupType.Tunnel);
        selectedUnloadingGroupId = PreserveOrFindDefault(
            selectedUnloadingGroupId,
            MonitorGroupType.UnloadingStation);
        CurrentLayout = CreateLayout();
        OnLayoutChanged();
    }

    public void SwitchChuteGroup(Guid groupId) =>
        SwitchGroup(groupId, MonitorGroupType.Chute, id => selectedChuteGroupId = id);

    public void SwitchTunnelGroup(Guid groupId) =>
        SwitchGroup(groupId, MonitorGroupType.Tunnel, id => selectedTunnelGroupId = id);

    public void SwitchUnloadingGroup(Guid groupId) =>
        SwitchGroup(
            groupId,
            MonitorGroupType.UnloadingStation,
            id => selectedUnloadingGroupId = id);

    // Compatibility overloads delegate by identity and never look up by name.
    public void SwitchChuteGroup(MonitorGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EnsureCompatibilityGroup(group, MonitorGroupType.Chute);
        SwitchChuteGroup(group.GroupId);
    }

    public void SwitchTunnel(MonitorGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EnsureCompatibilityGroup(group, MonitorGroupType.Tunnel);
        SwitchTunnelGroup(group.GroupId);
    }

    public void SwitchTunnelGroup(MonitorGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EnsureCompatibilityGroup(group, MonitorGroupType.Tunnel);
        SwitchTunnelGroup(group.GroupId);
    }

    public void SwitchUnloadingGroup(MonitorGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EnsureCompatibilityGroup(group, MonitorGroupType.UnloadingStation);
        SwitchUnloadingGroup(group.GroupId);
    }

    private void EnsureCompatibilityGroup(
        MonitorGroup group,
        MonitorGroupType expectedType)
    {
        if (group.Type != expectedType)
        {
            throw new ArgumentException(
                $"监控分组“{group.Name}”类型不匹配，期望 {expectedType}。",
                nameof(group));
        }

        if (!groups.Any(item => item.GroupId == group.GroupId))
        {
            groups = groups.Append(group).ToArray();
        }
    }

    private void SwitchGroup(
        Guid groupId,
        MonitorGroupType expectedType,
        Action<Guid> setSelection)
    {
        var group = groups.FirstOrDefault(item => item.GroupId == groupId);
        if (group is null)
        {
            throw new ArgumentException(
                $"不存在监控分组 {groupId}。",
                nameof(groupId));
        }

        if (group.Type != expectedType)
        {
            throw new ArgumentException(
                $"监控分组“{group.Name}”类型不匹配，期望 {expectedType}。",
                nameof(groupId));
        }

        setSelection(groupId);
        CurrentLayout = CreateLayout();
        OnLayoutChanged();
    }

    private Guid? PreserveOrFindDefault(
        Guid? selectedGroupId,
        MonitorGroupType type)
    {
        if (selectedGroupId is { } selected
            && groups.Any(group =>
                group.GroupId == selected
                && group.Type == type))
        {
            return selected;
        }

        return FindDefaultGroupId(type);
    }

    private Guid? FindDefaultGroupId(MonitorGroupType type) =>
        groups
            .Where(group => group.Type == type)
            .OrderBy(group => group.RootSort)
            .ThenBy(group => group.Sort)
            .ThenBy(group => group.GroupId)
            .Select(group => (Guid?)group.GroupId)
            .FirstOrDefault();

    private MonitorLayoutSnapshot CreateLayout()
    {
        var chute = FindSelected(selectedChuteGroupId, MonitorGroupType.Chute);
        var tunnel = FindSelected(selectedTunnelGroupId, MonitorGroupType.Tunnel);
        var unloading = FindSelected(
            selectedUnloadingGroupId,
            MonitorGroupType.UnloadingStation);

        return new MonitorLayoutSnapshot(
            Pad(chute?.Cameras, 3)
                .Concat(Pad(tunnel?.Cameras, 1))
                .ToArray(),
            Pad(unloading?.Cameras, 3).ToArray());
    }

    private MonitorGroup? FindSelected(
        Guid? selectedGroupId,
        MonitorGroupType type) =>
        selectedGroupId is { } id
            ? groups.FirstOrDefault(group =>
                group.GroupId == id
                && group.Type == type)
            : null;

    private static IEnumerable<CameraInfo?> Pad(
        IReadOnlyList<CameraInfo>? cameras,
        int count)
    {
        var existing = cameras?.Take(count).Cast<CameraInfo?>()
            ?? Enumerable.Empty<CameraInfo?>();
        return existing.Concat(
            Enumerable.Repeat<CameraInfo?>(null, Math.Max(0, count - (cameras?.Count ?? 0))));
    }

    private void OnLayoutChanged() => LayoutChanged?.Invoke(this, CurrentLayout);
}
