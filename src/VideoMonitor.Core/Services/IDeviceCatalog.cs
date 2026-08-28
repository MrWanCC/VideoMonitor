using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public interface IDeviceCatalog
{
    IReadOnlyList<DeviceGroup> GetGroups();

    IReadOnlyList<CameraDevice> GetDevices(Guid groupId);

    CameraDevice? GetDevice(Guid deviceId);

    void AddGroup(DeviceGroup group);

    void UpdateGroup(DeviceGroup group);

    bool DeleteGroup(Guid groupId);

    void AddDevice(CameraDevice device);

    void UpdateDevice(CameraDevice device);

    bool DeleteDevice(Guid deviceId);

    event EventHandler? Changed;
}
