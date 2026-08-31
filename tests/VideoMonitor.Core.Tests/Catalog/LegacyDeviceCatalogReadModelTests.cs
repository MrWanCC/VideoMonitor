using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class LegacyDeviceCatalogReadModelTests
{
    [Fact]
    public void NonEmptyPassword_MapsOnlyToHasPassword()
    {
        var device = CreateDevice(password: "TEST-LOCAL-PASSWORD");
        var catalog = CreateCatalog(device);
        var readModel = new LegacyDeviceCatalogReadModel(catalog);

        var dto = Assert.Single(readModel.GetDevices(device.GroupId));

        Assert.True(dto.HasPassword);
        Assert.DoesNotContain(
            "Password",
            typeof(CameraDeviceDto).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void EmptyPassword_MapsHasPasswordFalse()
    {
        var device = CreateDevice(password: string.Empty);
        var readModel = new LegacyDeviceCatalogReadModel(CreateCatalog(device));

        var dto = Assert.Single(readModel.GetDevices(device.GroupId));

        Assert.False(dto.HasPassword);
    }

    [Fact]
    public void MapsExistingKindWithoutNameInference()
    {
        var root = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "arbitrary root",
            Kind = MonitorGroupType.Chute
        };
        var unclassified = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Chute",
            Kind = null
        };
        var catalog = new InMemoryDeviceCatalog([root, unclassified], []);
        var readModel = new LegacyDeviceCatalogReadModel(catalog);

        var groups = readModel.GetGroups();

        Assert.Equal(MonitorGroupType.Chute, Assert.Single(groups, group => group.Id == root.Id).Kind);
        Assert.Null(Assert.Single(groups, group => group.Id == unclassified.Id).Kind);
    }

    [Fact]
    public void LegacyCatalogChanged_IsForwarded()
    {
        var root = new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Kind = MonitorGroupType.Chute
        };
        var catalog = new InMemoryDeviceCatalog([root], []);
        var readModel = new LegacyDeviceCatalogReadModel(catalog);
        var changed = 0;
        readModel.Changed += (_, _) => changed++;

        catalog.AddGroup(new DeviceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            ParentId = root.Id
        });

        Assert.Equal(1, changed);
    }

    private static InMemoryDeviceCatalog CreateCatalog(CameraDevice device) =>
        new(
            [new DeviceGroup
            {
                Id = device.GroupId,
                Name = "Root",
                Kind = MonitorGroupType.Chute
            }],
            [device]);

    private static CameraDevice CreateDevice(string password)
    {
        var deviceId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        return new CameraDevice
        {
            Id = deviceId,
            GroupId = groupId,
            Name = "Device",
            IpAddress = "192.0.2.10",
            Username = "user",
            Password = password,
            Channels =
            {
                new CameraChannel
                {
                    Id = Guid.NewGuid(),
                    DeviceId = deviceId,
                    ChannelNo = 1,
                    ChannelName = "Main",
                    StreamType = StreamType.Main
                }
            }
        };
    }
}
