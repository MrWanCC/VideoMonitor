using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Core.Tests.Services;

public sealed class DeviceCatalogSnapshotFactoryTests
{
    [Fact]
    public void Create_ClearsRuntimeFieldsWithoutMutatingCatalog()
    {
        var root = new DeviceGroup
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "根分组",
            Enabled = true
        };
        var group = new DeviceGroup
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Name = "设备分组",
            ParentId = root.Id,
            Enabled = true
        };
        var device = new CameraDevice
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Name = "测试设备",
            GroupId = group.Id,
            Status = CameraStatus.Online
        };
        var channel = new CameraChannel
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
            DeviceId = device.Id,
            StreamId = "abc"
        };
        device.Channels.Add(channel);
        var catalog = new InMemoryDeviceCatalog([root, group], [device]);

        var snapshot = DeviceCatalogSnapshotFactory.Create(catalog);

        var snapshotDevice = Assert.Single(snapshot.Devices);
        var snapshotChannel = Assert.Single(snapshotDevice.Channels);
        Assert.Equal(CameraStatus.Unknown, snapshotDevice.Status);
        Assert.Equal(string.Empty, snapshotChannel.StreamId);
        Assert.Equal(CameraStatus.Online, device.Status);
        Assert.Equal("abc", channel.StreamId);
    }
}
