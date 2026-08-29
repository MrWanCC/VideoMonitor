using System.Collections.Concurrent;
using System.Text.Json;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Services;

public sealed class DeviceCatalogPersistenceCoordinatorTests
{
    private static readonly Guid RootGroupId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceGroupId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task AddGroup_QueuesSnapshotAndSavesIt()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var group = new DeviceGroup
        {
            Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Name = "新增分组",
            ParentId = RootGroupId,
            Sort = 2,
            Enabled = true
        };

        catalog.AddGroup(group);
        await coordinator.FlushAsync();

        Assert.Contains(store.Snapshots.Single().Groups, item => item.Name == "新增分组");
    }

    [Fact]
    public async Task UpdateGroup_QueuesSnapshotAndSavesIt()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var group = catalog.GetGroups().Single(item => item.Id == DeviceGroupId);
        group.Name = "修改分组";

        catalog.UpdateGroup(group);
        await coordinator.FlushAsync();

        Assert.Equal("修改分组", store.Snapshots.Single().Groups.Single(item => item.Id == DeviceGroupId).Name);
    }

    [Fact]
    public async Task DeleteGroup_QueuesSnapshotAndSavesIt()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        var removableGroup = new DeviceGroup
        {
            Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Name = "待删除分组",
            ParentId = RootGroupId,
            Sort = 2,
            Enabled = true
        };
        catalog.AddGroup(removableGroup);
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);

        Assert.True(catalog.DeleteGroup(removableGroup.Id));
        await coordinator.FlushAsync();

        Assert.DoesNotContain(store.Snapshots.Single().Groups, item => item.Id == removableGroup.Id);
    }

    [Fact]
    public async Task AddDevice_QueuesSnapshotAndSavesIt()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var device = CreateDevice(
            Guid.Parse("50000000-0000-0000-0000-000000000002"),
            "新增设备",
            DeviceGroupId,
            Guid.Parse("60000000-0000-0000-0000-000000000002"));

        catalog.AddDevice(device);
        await coordinator.FlushAsync();

        Assert.Contains(store.Snapshots.Single().Devices, item => item.Id == device.Id);
    }

    [Fact]
    public async Task UpdateDevice_QueuesSnapshotAndSavesIt()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var device = CloneDevice(catalog.GetDevice(DeviceId)!);
        device.Name = "修改设备";

        catalog.UpdateDevice(device);
        await coordinator.FlushAsync();

        Assert.Equal("修改设备", store.Snapshots.Single().Devices.Single(item => item.Id == DeviceId).Name);
    }

    [Fact]
    public async Task DeleteDevice_QueuesSnapshotAndSavesIt()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);

        Assert.True(catalog.DeleteDevice(DeviceId));
        await coordinator.FlushAsync();

        Assert.DoesNotContain(store.Snapshots.Single().Devices, item => item.Id == DeviceId);
    }

    [Fact]
    public async Task ConsecutiveUpdatesAreSavedInChangedOrderAndLatestSnapshotWins()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);

        foreach (var name in new[] { "设备A", "设备B", "设备C" })
        {
            var device = CloneDevice(catalog.GetDevice(DeviceId)!);
            device.Name = name;
            catalog.UpdateDevice(device);
        }

        await coordinator.FlushAsync();

        Assert.Equal(
            new[] { "设备A", "设备B", "设备C" },
            store.Snapshots.Select(snapshot => snapshot.Devices.Single().Name));
    }

    [Fact]
    public async Task SnapshotIsCreatedBeforeLaterBusinessObjectMutation()
    {
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore
        {
            SaveBehavior = async (snapshot, callNumber, _) =>
            {
                if (callNumber == 1)
                {
                    firstSaveStarted.SetResult();
                    await releaseFirstSave.Task;
                }
            }
        };
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var device = CloneDevice(catalog.GetDevice(DeviceId)!);
        device.Name = "Changed时名称";

        catalog.UpdateDevice(device);
        await firstSaveStarted.Task;
        catalog.GetDevice(DeviceId)!.Name = "后续直接修改";
        releaseFirstSave.SetResult();

        await coordinator.FlushAsync();

        Assert.Equal("Changed时名称", store.Snapshots.Single().Devices.Single().Name);
    }

    [Fact]
    public async Task ModificationWhileSaveIsInProgressIsSavedAfterTheEarlierSnapshot()
    {
        var firstSaveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore
        {
            SaveBehavior = async (snapshot, callNumber, _) =>
            {
                if (callNumber == 1)
                {
                    firstSaveStarted.SetResult();
                    await releaseFirstSave.Task;
                }
            }
        };
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);

        var first = CloneDevice(catalog.GetDevice(DeviceId)!);
        first.Name = "保存中的第一次修改";
        catalog.UpdateDevice(first);
        await firstSaveStarted.Task;

        var second = CloneDevice(catalog.GetDevice(DeviceId)!);
        second.Name = "保存期间的第二次修改";
        catalog.UpdateDevice(second);
        releaseFirstSave.SetResult();

        await coordinator.FlushAsync();

        Assert.Equal(
            new[] { "保存中的第一次修改", "保存期间的第二次修改" },
            store.Snapshots.Select(snapshot => snapshot.Devices.Single().Name));
    }

    [Fact]
    public async Task SaveFailureDoesNotStopLaterSavesAndSuccessfulSaveClearsFailure()
    {
        var failureNotified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore
        {
            SaveBehavior = (snapshot, callNumber, _) =>
            {
                if (callNumber == 1)
                {
                    return Task.FromException(new InvalidOperationException("password-must-not-leak"));
                }

                return Task.CompletedTask;
            }
        };
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        coordinator.PersistenceFailed += (_, _) => failureNotified.TrySetResult();
        var first = CloneDevice(catalog.GetDevice(DeviceId)!);
        first.Name = "第一次修改";
        catalog.UpdateDevice(first);
        await failureNotified.Task;

        var second = CloneDevice(catalog.GetDevice(DeviceId)!);
        second.Name = "第二次修改";
        catalog.UpdateDevice(second);
        await coordinator.FlushAsync();

        Assert.Equal(2, store.SaveCallCount);
        Assert.Null(coordinator.LastPersistenceException);
        Assert.Equal("第二次修改", store.Snapshots.Last().Devices.Single().Name);
    }

    [Fact]
    public async Task FlushAsyncReportsFinalSaveFailureWithoutLeakingFailureText()
    {
        var store = new RecordingStore
        {
            SaveBehavior = (_, _, _) =>
                Task.FromException(new InvalidOperationException("password-must-not-leak"))
        };
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var failureNotified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.PersistenceFailed += (_, _) => failureNotified.TrySetResult();
        var device = CloneDevice(catalog.GetDevice(DeviceId)!);
        device.Name = "失败修改";
        catalog.UpdateDevice(device);
        await failureNotified.Task;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.FlushAsync());

        Assert.DoesNotContain("password-must-not-leak", exception.ToString(), StringComparison.Ordinal);
        Assert.NotNull(coordinator.LastPersistenceException);
    }

    [Fact]
    public async Task FlushAsyncPersistsPasswordAndExcludesRuntimeFields()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathOf("device-catalog.json");
        var store = new JsonDeviceCatalogStore(path);
        var catalog = CreateCatalog();
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var device = CloneDevice(catalog.GetDevice(DeviceId)!);
        device.Remark = "触发持久化";

        catalog.UpdateDevice(device);
        await coordinator.FlushAsync();

        var json = await File.ReadAllTextAsync(path);
        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.DoesNotContain("\"status\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtime-stream-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", json, StringComparison.Ordinal);
        Assert.Equal(CameraStatus.Unknown, loaded.Devices.Single().Status);
        Assert.Equal(string.Empty, loaded.Devices.Single().Channels.Single().StreamId);
        Assert.Equal("test-password", catalog.GetDevice(DeviceId)!.Password);
    }

    [Fact]
    public async Task DisposeAsyncUnsubscribesFromCatalogChanges()
    {
        var store = new RecordingStore();
        var catalog = CreateCatalog();
        var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);

        await coordinator.DisposeAsync();
        var group = catalog.GetGroups().Single(item => item.Id == DeviceGroupId);
        group.Name = "Dispose后修改";
        catalog.UpdateGroup(group);

        Assert.Empty(store.Snapshots);
    }

    private static InMemoryDeviceCatalog CreateCatalog()
    {
        var root = new DeviceGroup
        {
            Id = RootGroupId,
            Name = "根分组",
            Enabled = true
        };
        var group = new DeviceGroup
        {
            Id = DeviceGroupId,
            Name = "设备分组",
            ParentId = RootGroupId,
            Enabled = true
        };
        return new InMemoryDeviceCatalog(
            [root, group],
            [CreateDevice(DeviceId, "原始设备", DeviceGroupId, ChannelId)]);
    }

    private static CameraDevice CreateDevice(
        Guid deviceId,
        string name,
        Guid groupId,
        Guid channelId) {
        var device = new CameraDevice
        {
            Id = deviceId,
            Name = name,
            GroupId = groupId,
            IpAddress = "192.0.2.10",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "test-user",
            Password = "test-password",
            Manufacturer = "测试厂商",
            Model = "测试型号",
            TransportMode = TransportMode.Tcp,
            Status = CameraStatus.Warning,
            Enabled = true,
            Remark = "测试备注"
        };
        device.Channels.Add(new CameraChannel
        {
            Id = channelId,
            DeviceId = deviceId,
            ChannelNo = 1,
            ChannelName = "通道1",
            StreamType = StreamType.Main,
            StreamId = "runtime-stream-id",
            Enabled = true
        });
        return device;
    }

    private static CameraDevice CloneDevice(CameraDevice source)
    {
        var clone = new CameraDevice
        {
            Id = source.Id,
            Name = source.Name,
            GroupId = source.GroupId,
            IpAddress = source.IpAddress,
            SdkPort = source.SdkPort,
            RtspPort = source.RtspPort,
            Username = source.Username,
            Password = source.Password,
            Manufacturer = source.Manufacturer,
            Model = source.Model,
            TransportMode = source.TransportMode,
            Status = source.Status,
            Enabled = source.Enabled,
            Remark = source.Remark
        };
        foreach (var channel in source.Channels)
        {
            clone.Channels.Add(new CameraChannel
            {
                Id = channel.Id,
                DeviceId = channel.DeviceId,
                ChannelNo = channel.ChannelNo,
                ChannelName = channel.ChannelName,
                StreamType = channel.StreamType,
                StreamId = channel.StreamId,
                Enabled = channel.Enabled
            });
        }

        return clone;
    }

    private sealed class RecordingStore : IDeviceCatalogStore
    {
        private readonly ConcurrentQueue<DeviceCatalogSnapshot> snapshots = new();

        public Func<DeviceCatalogSnapshot, int, CancellationToken, Task>? SaveBehavior { get; init; }

        public int SaveCallCount => Volatile.Read(ref saveCallCount);

        public IReadOnlyList<DeviceCatalogSnapshot> Snapshots => snapshots.ToArray();

        private int saveCallCount;

        public Task<DeviceCatalogSnapshot?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceCatalogSnapshot?>(null);

        public async Task SaveAsync(
            DeviceCatalogSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            snapshots.Enqueue(snapshot);
            var callNumber = Interlocked.Increment(ref saveCallCount);
            if (SaveBehavior is not null)
            {
                await SaveBehavior(snapshot, callNumber, cancellationToken);
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"VideoMonitor.PersistenceCoordinator.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathOf(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
