using VideoMonitor.Core.Models;
using VideoMonitor.Core.Catalog;

namespace VideoMonitor.Core.Tests.Models;

public sealed class CatalogContractTests
{
    [Fact]
    public void CatalogContracts_Exist()
    {
        var assembly = typeof(CameraDevice).Assembly;

        Assert.NotNull(assembly.GetType("VideoMonitor.Core.Catalog.CatalogSnapshotDto"));
        Assert.NotNull(assembly.GetType("VideoMonitor.Core.Catalog.DeviceGroupDto"));
        Assert.NotNull(assembly.GetType("VideoMonitor.Core.Catalog.CameraDeviceDto"));
        Assert.NotNull(assembly.GetType("VideoMonitor.Core.Catalog.CameraChannelDto"));
        Assert.NotNull(assembly.GetType("VideoMonitor.Core.Catalog.CreateDeviceRequest"));
    }

    [Fact]
    public void ReadDtos_ExposeOnlyPasswordSafeCatalogFields()
    {
        var cameraDeviceProperties = PropertyNames<CameraDeviceDto>();
        Assert.Contains(nameof(CameraDeviceDto.Id), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.GroupId), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Name), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.IpAddress), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.SdkPort), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.RtspPort), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Username), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.HasPassword), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Manufacturer), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Model), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.TransportMode), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Enabled), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Remark), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Revision), cameraDeviceProperties);
        Assert.Contains(nameof(CameraDeviceDto.Channels), cameraDeviceProperties);

        AssertSafeResultSurface<CameraDeviceDto>();
        AssertSafeResultSurface<CameraChannelDto>();
        AssertSafeResultSurface<DeviceGroupDto>();
        AssertSafeResultSurface<CatalogSnapshotDto>();
        AssertSafeResultSurface<CatalogErrorDto>();
    }

    [Fact]
    public void RequestDtos_AreTheOnlyContractsThatCarryPasswordWrites()
    {
        Assert.Contains(nameof(CreateDeviceRequest.Password), PropertyNames<CreateDeviceRequest>());
        Assert.Contains(nameof(UpdateDeviceRequest.NewPassword), PropertyNames<UpdateDeviceRequest>());

        Assert.DoesNotContain(nameof(CreateDeviceRequest.Password), PropertyNames<CatalogSnapshotDto>());
        Assert.DoesNotContain(nameof(UpdateDeviceRequest.NewPassword), PropertyNames<DeviceGroupDto>());
        Assert.DoesNotContain(nameof(UpdateDeviceRequest.NewPassword), PropertyNames<CameraDeviceDto>());
        Assert.DoesNotContain(nameof(UpdateDeviceRequest.NewPassword), PropertyNames<CameraChannelDto>());
        Assert.DoesNotContain(nameof(UpdateDeviceRequest.NewPassword), PropertyNames<CatalogErrorDto>());
    }

    [Fact]
    public void CatalogContracts_PreserveAggregateRevisionAndRuntimeBoundaries()
    {
        Assert.Equal(typeof(long), typeof(DeviceGroupDto).GetProperty(nameof(DeviceGroupDto.Revision))!.PropertyType);
        Assert.Equal(typeof(long), typeof(CameraDeviceDto).GetProperty(nameof(CameraDeviceDto.Revision))!.PropertyType);
        Assert.Equal(
            typeof(IReadOnlyList<CameraChannelDto>),
            typeof(CameraDeviceDto).GetProperty(nameof(CameraDeviceDto.Channels))!.PropertyType);
        Assert.Equal(
            typeof(IReadOnlyList<DeviceGroupDto>),
            typeof(CatalogSnapshotDto).GetProperty(nameof(CatalogSnapshotDto.Groups))!.PropertyType);
        Assert.Equal(
            typeof(IReadOnlyList<CameraDeviceDto>),
            typeof(CatalogSnapshotDto).GetProperty(nameof(CatalogSnapshotDto.Devices))!.PropertyType);
        Assert.Equal(
            typeof(long),
            typeof(UpdateDeviceRequest).GetProperty(nameof(UpdateDeviceRequest.ExpectedRevision))!.PropertyType);
        Assert.Equal(
            typeof(long),
            typeof(UpdateGroupRequest).GetProperty(nameof(UpdateGroupRequest.ExpectedRevision))!.PropertyType);

        Assert.DoesNotContain("Revision", PropertyNames<CameraChannelDto>());
        Assert.DoesNotContain("Status", PropertyNames<CameraDeviceDto>());
        Assert.DoesNotContain("CameraStatus", PropertyNames<CameraDeviceDto>());
        Assert.DoesNotContain("StreamId", PropertyNames<CameraChannelDto>());
        Assert.DoesNotContain("Password", PropertyNames<CameraChannelDto>());
    }

    private static string[] PropertyNames<T>() =>
        typeof(T).GetProperties().Select(property => property.Name).ToArray();

    private static void AssertSafeResultSurface<T>()
    {
        var names = PropertyNames<T>();
        Assert.DoesNotContain("Password", names);
        Assert.DoesNotContain("NewPassword", names);
        Assert.DoesNotContain("PasswordCiphertext", names);
        Assert.DoesNotContain("Status", names);
        Assert.DoesNotContain("CameraStatus", names);
        Assert.DoesNotContain("StreamId", names);
        Assert.DoesNotContain("ZlmSecret", names);
        Assert.DoesNotContain("MasterKey", names);
    }
}
