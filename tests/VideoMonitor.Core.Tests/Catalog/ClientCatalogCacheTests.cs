using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class ClientCatalogCacheTests
{
    [Fact]
    public async Task IdenticalSnapshot_DoesNotRaiseChanged()
    {
        var initial = EmptySnapshot();
        var cache = new ClientCatalogCache(initial, new InlineUiDispatcher());
        var changed = 0;
        cache.Changed += (_, _) => changed++;

        await cache.ReplaceAsync(EmptySnapshot());

        Assert.Equal(0, changed);
        Assert.Same(initial, cache.Snapshot);
    }

    [Fact]
    public async Task ChangedHandler_SeesSnapshotOnlyAfterDispatcherCommit()
    {
        var dispatcher = new CapturingUiDispatcher();
        var initial = EmptySnapshot();
        var next = SnapshotWithOneGroup();
        var cache = new ClientCatalogCache(initial, dispatcher);
        CatalogSnapshotDto? observed = null;
        cache.Changed += (_, _) => observed = cache.Snapshot;

        await cache.ReplaceAsync(next);

        Assert.Same(initial, cache.Snapshot);
        dispatcher.RunPending();

        Assert.Same(next, cache.Snapshot);
        Assert.Same(next, observed);
    }

    [Fact]
    public async Task ChangedSnapshot_RaisesChangedExactlyOnce()
    {
        var cache = new ClientCatalogCache(EmptySnapshot(), new InlineUiDispatcher());
        var changed = 0;
        cache.Changed += (_, _) => changed++;

        await cache.ReplaceAsync(SnapshotWithOneGroup());

        Assert.Equal(1, changed);
    }

    [Fact]
    public void GetDevices_UsesGroupGuid()
    {
        var groupA = new DeviceGroupDto(
            Guid.NewGuid(),
            "same name",
            null,
            0,
            true,
            MonitorGroupType.Chute,
            1);
        var groupB = groupA with { Id = Guid.NewGuid() };
        var deviceA = DeviceDto(groupA.Id, Guid.NewGuid(), "A");
        var deviceB = DeviceDto(groupB.Id, Guid.NewGuid(), "B");
        var cache = new ClientCatalogCache(
            new CatalogSnapshotDto([groupA, groupB], [deviceA, deviceB]),
            new InlineUiDispatcher());

        var result = cache.GetDevices(groupA.Id);

        var actual = Assert.Single(result);
        Assert.Equal(deviceA.Id, actual.Id);
        Assert.DoesNotContain(result, device => device.Id == deviceB.Id);
    }

    [Fact]
    public void GetDevice_UsesDeviceGuid()
    {
        var groupId = Guid.NewGuid();
        var deviceA = DeviceDto(groupId, Guid.NewGuid(), "same name");
        var deviceB = DeviceDto(groupId, Guid.NewGuid(), "same name");
        var cache = new ClientCatalogCache(
            new CatalogSnapshotDto([], [deviceA, deviceB]),
            new InlineUiDispatcher());

        var result = cache.GetDevice(deviceB.Id);

        Assert.NotNull(result);
        Assert.Equal(deviceB.Id, result!.Id);
    }

    [Fact]
    public async Task ChannelChange_RaisesChanged()
    {
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var initial = new CatalogSnapshotDto(
            [],
            [DeviceDto(
                groupId,
                deviceId,
                "Device",
                new CameraChannelDto(channelId, deviceId, 1, "Main", StreamType.Main, true))]);
        var next = initial with
        {
            Devices =
            [DeviceDto(
                groupId,
                deviceId,
                "Device",
                new CameraChannelDto(channelId, deviceId, 1, "Updated", StreamType.Main, true))]
        };
        var cache = new ClientCatalogCache(initial, new InlineUiDispatcher());
        var changed = 0;
        cache.Changed += (_, _) => changed++;

        await cache.ReplaceAsync(next);

        Assert.Equal(1, changed);
        Assert.Equal("Updated", Assert.Single(cache.GetDevice(deviceId)!.Channels).ChannelName);
    }

    [Fact]
    public void CacheAndReadModel_DoNotExposePasswordProperties()
    {
        Assert.DoesNotContain(
            "Password",
            typeof(ClientCatalogCache).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "PasswordCiphertext",
            typeof(ClientCatalogCache).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "Password",
            typeof(IDeviceCatalogReadModel).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "PasswordCiphertext",
            typeof(IDeviceCatalogReadModel).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "Password",
            typeof(CameraDeviceDto).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "PasswordCiphertext",
            typeof(CameraDeviceDto).GetProperties().Select(property => property.Name));
    }

    private static CatalogSnapshotDto EmptySnapshot() =>
        new(Array.Empty<DeviceGroupDto>(), Array.Empty<CameraDeviceDto>());

    private static CatalogSnapshotDto SnapshotWithOneGroup() =>
        new(
            [new DeviceGroupDto(
                Guid.NewGuid(),
                "Root",
                null,
                0,
                true,
                MonitorGroupType.Chute,
                1)],
            []);

    private static CameraDeviceDto DeviceDto(
        Guid groupId,
        Guid deviceId,
        string name,
        params CameraChannelDto[] channels) =>
        new(
            deviceId,
            groupId,
            name,
            "192.0.2.10",
            8000,
            554,
            "user",
            true,
            "Vendor",
            "Model",
            TransportMode.Auto,
            true,
            string.Empty,
            1,
            channels);

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingUiDispatcher : IUiDispatcher
    {
        private Action? pending;

        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            pending = action;
            return Task.CompletedTask;
        }

        public void RunPending() =>
            (pending ?? throw new InvalidOperationException("No pending UI action.")).Invoke();
    }
}
