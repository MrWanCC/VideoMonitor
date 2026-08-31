namespace VideoMonitor.Core.Catalog;

public interface IDeviceCatalogReadModel
{
    IReadOnlyList<DeviceGroupDto> GetGroups();

    IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId);

    CameraDeviceDto? GetDevice(Guid deviceId);

    event EventHandler? Changed;
}
