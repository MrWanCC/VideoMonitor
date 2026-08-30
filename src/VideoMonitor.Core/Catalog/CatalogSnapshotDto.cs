namespace VideoMonitor.Core.Catalog;

public sealed record CatalogSnapshotDto(
    IReadOnlyList<DeviceGroupDto> Groups,
    IReadOnlyList<CameraDeviceDto> Devices);
