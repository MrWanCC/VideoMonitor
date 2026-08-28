using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Services;

public sealed class MonitorSwitchService
{
    public MonitorSwitchService(
        MonitorGroup defaultShaftGroup,
        MonitorGroup defaultTunnelGroup,
        MonitorGroup defaultUnloadingGroup)
    {
        Validate(defaultShaftGroup, MonitorGroupType.Shaft, 3);
        Validate(defaultTunnelGroup, MonitorGroupType.Tunnel, 1);
        Validate(defaultUnloadingGroup, MonitorGroupType.UnloadingStation, 3);

        Current = new MonitorLayoutSnapshot(
            defaultShaftGroup.Cameras.Take(3)
                .Append(defaultTunnelGroup.Cameras[0])
                .ToArray(),
            defaultUnloadingGroup.Cameras.Take(3).ToArray());
    }

    public MonitorLayoutSnapshot Current { get; private set; }

    public event EventHandler<MonitorLayoutSnapshot>? LayoutChanged;

    public void SwitchShaftGroup(MonitorGroup group)
    {
        Validate(group, MonitorGroupType.Shaft, 3);
        Current = Current with
        {
            MainSlots = group.Cameras.Take(3)
                .Concat(Current.MainSlots.Skip(3))
                .ToArray()
        };
        OnLayoutChanged();
    }

    public void SwitchTunnel(MonitorGroup group)
    {
        Validate(group, MonitorGroupType.Tunnel, 1);
        Current = Current with
        {
            MainSlots = Current.MainSlots.Take(3)
                .Append(group.Cameras[0])
                .ToArray()
        };
        OnLayoutChanged();
    }

    public void SwitchUnloadingGroup(MonitorGroup group)
    {
        Validate(group, MonitorGroupType.UnloadingStation, 3);
        Current = Current with
        {
            SecondarySlots = group.Cameras.Take(3).ToArray()
        };
        OnLayoutChanged();
    }

    private static void Validate(MonitorGroup? group, MonitorGroupType expectedType, int requiredCameras)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.Type != expectedType)
        {
            throw new ArgumentException(
                $"监控组“{group.Name}”类型不匹配，期望 {expectedType}。",
                nameof(group));
        }

        if (group.Cameras.Count < requiredCameras)
        {
            throw new ArgumentException(
                $"监控组“{group.Name}”至少需要 {requiredCameras} 路摄像头。",
                nameof(group));
        }
    }

    private void OnLayoutChanged() => LayoutChanged?.Invoke(this, Current);
}
