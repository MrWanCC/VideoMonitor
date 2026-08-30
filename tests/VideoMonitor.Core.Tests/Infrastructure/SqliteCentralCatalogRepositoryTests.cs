using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteCentralCatalogRepositoryTests
{
    [Fact]
    public void Task3Contracts_ExistWithReadCreateOnly()
    {
        var assembly = typeof(SqliteConnectionFactory).Assembly;
        var repositoryContract = assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.ICentralCatalogRepository");
        var resultType = assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.CatalogRepositoryResult`1");

        Assert.NotNull(repositoryContract);
        Assert.NotNull(resultType);

        var methodNames = repositoryContract!
            .GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CreateDeviceAsync",
                "CreateGroupAsync",
                "GetCatalogAsync",
                "GetDeviceAsync",
                "GetGroupAsync"
            },
            methodNames);
        Assert.Null(assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.CatalogRepositoryDeleteResult"));
    }

    [Fact]
    public async Task GetCatalogAsync_EmptyDatabaseReturnsEmptySnapshot()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());

        var snapshot = await GetCatalogAsync(repository);

        Assert.Empty(snapshot.Groups);
        Assert.Empty(snapshot.Devices);
    }

    [Fact]
    public async Task CreateGroupAsync_StoresRevisionOneAndGetGroupReadsIt()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = new DeviceGroup
        {
            Id = Guid.Parse("91000000-0000-0000-0000-000000000001"),
            Name = "Test Group",
            Sort = 4,
            Enabled = true,
            Revision = 77
        };

        var result = await InvokeAsync(
            repository,
            "CreateGroupAsync",
            group,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var created = Assert.IsType<DeviceGroupDto>(GetProperty(result, "Value"));
        Assert.Equal(group.Id, created.Id);
        Assert.Equal(group.Name, created.Name);
        Assert.Equal(1L, created.Revision);

        var loaded = await GetGroupAsync(repository, group.Id);
        Assert.NotNull(loaded);
        Assert.Equal(1L, loaded!.Revision);
        Assert.Equal(group.Name, loaded.Name);

        await using var connection = context.Factory.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(
            "1",
            await ReadScalarAsync(
                connection,
                "SELECT revision FROM device_groups WHERE id = $id;",
                ("$id", group.Id.ToString("N"))));
    }

    [Fact]
    public async Task CreateDeviceAsync_ReturnsSafeDtoAndReadsMainAndSubChannels()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Password-Only-In-Memory");

        var result = await InvokeAsync(
            repository,
            "CreateDeviceAsync",
            device,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var created = Assert.IsType<CameraDeviceDto>(GetProperty(result, "Value"));
        Assert.Equal(device.Id, created.Id);
        Assert.Equal(device.Name, created.Name);
        Assert.Equal(1L, created.Revision);
        Assert.True(created.HasPassword);
        Assert.Equal(2, created.Channels.Count);
        Assert.Equal(StreamType.Main, created.Channels[0].StreamType);
        Assert.Equal(StreamType.Sub, created.Channels[1].StreamType);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);

        var rawCiphertext = await ReadScalarAsync(
            context,
            "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
            ("$id", device.Id.ToString("N")));
        Assert.StartsWith("test-protected:", rawCiphertext, StringComparison.Ordinal);
        Assert.DoesNotContain("Password-Only-In-Memory", rawCiphertext, StringComparison.Ordinal);

        var loaded = await GetDeviceAsync(repository, device.Id);
        Assert.NotNull(loaded);
        Assert.Equal(device.Name, loaded!.Name);
        Assert.True(loaded.HasPassword);
        Assert.Equal(2, loaded.Channels.Count);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task GetCatalogAsync_ReturnsConsistentSafeSnapshotWithoutDecrypting()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, "Catalog-Password");
        await InvokeAsync(repository, "CreateDeviceAsync", device, CancellationToken.None);

        protector.ResetUnprotectCalls();
        var snapshot = await GetCatalogAsync(repository);

        var loadedGroup = Assert.Single(snapshot.Groups);
        var loadedDevice = Assert.Single(snapshot.Devices);
        Assert.Equal(group.Id, loadedGroup.Id);
        Assert.Equal(device.Id, loadedDevice.Id);
        Assert.Equal(group.Id, loadedDevice.GroupId);
        Assert.Equal(2, loadedDevice.Channels.Count);
        Assert.True(loadedDevice.HasPassword);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task GetDeviceAsync_ReadsOnlyRequestedDeviceChannels_WhenOtherDevicesExist()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        var deviceB = CreateDevice(
            group.Id,
            string.Empty,
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000002"),
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000003"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Other Camera");

        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceB, CancellationToken.None);
        protector.ResetUnprotectCalls();

        var loaded = await GetDeviceAsync(repository, deviceA.Id);

        Assert.NotNull(loaded);
        Assert.Equal(deviceA.Id, loaded!.Id);
        Assert.Equal(2, loaded.Channels.Count);
        Assert.All(loaded.Channels, channel => Assert.Equal(deviceA.Id, channel.DeviceId));
        Assert.Equal(
            deviceA.Channels.Select(channel => channel.Id),
            loaded.Channels.Select(channel => channel.Id));
        Assert.DoesNotContain(
            loaded.Channels,
            channel => deviceB.Channels.Any(other => other.Id == channel.Id));
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task CreateDeviceAsync_DuplicateDeviceId_IsNotChannelConflict()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        var duplicate = CreateDevice(
            group.Id,
            string.Empty,
            mainChannelId: Guid.Parse("97000000-0000-0000-0000-000000000003"),
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Duplicate Device");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InvokeAsync(
            repository,
            "CreateDeviceAsync",
            duplicate,
            CancellationToken.None));

        Assert.NotEqual(2067, exception.SqliteExtendedErrorCode);
        Assert.Equal("Central Camera", (await GetDeviceAsync(repository, deviceA.Id))!.Name);
    }

    [Fact]
    public async Task CreateDeviceAsync_DuplicateChannelPrimaryKey_RollsBackAndIsNotChannelConflict()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var deviceA = CreateDevice(group.Id, string.Empty);
        await InvokeAsync(repository, "CreateDeviceAsync", deviceA, CancellationToken.None);
        var deviceB = CreateDevice(
            group.Id,
            string.Empty,
            deviceId: Guid.Parse("96000000-0000-0000-0000-000000000002"),
            mainChannelId: deviceA.Channels[0].Id,
            subChannelId: Guid.Parse("97000000-0000-0000-0000-000000000004"),
            name: "Second Device");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InvokeAsync(
            repository,
            "CreateDeviceAsync",
            deviceB,
            CancellationToken.None));

        Assert.NotEqual(2067, exception.SqliteExtendedErrorCode);
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_devices WHERE id = $id;",
                ("$id", deviceB.Id.ToString("N"))));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_channels WHERE device_id = $id;",
                ("$id", deviceB.Id.ToString("N"))));
        Assert.Equal("Central Camera", (await GetDeviceAsync(repository, deviceA.Id))!.Name);
        Assert.Equal(2, (await GetDeviceAsync(repository, deviceA.Id))!.Channels.Count);
    }

    [Fact]
    public async Task CreateDeviceAsync_EmptyPasswordStoresEmptyWithoutProtecting()
    {
        await using var context = await TestContext.CreateAsync();
        var protector = new CountingSecretProtector();
        var repository = CreateRepository(context, protector);
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);

        var result = await InvokeAsync(
            repository,
            "CreateDeviceAsync",
            device,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        var created = Assert.IsType<CameraDeviceDto>(GetProperty(result, "Value"));
        Assert.False(created.HasPassword);
        Assert.Equal(0, protector.ProtectCalls);
        Assert.Equal(
            string.Empty,
            await ReadScalarAsync(
                context,
                "SELECT password_ciphertext FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task CreateDeviceAsync_DuplicateChannelIdentityRollsBackAggregate()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());
        var group = await CreateGroupAsync(repository);
        var device = CreateDevice(group.Id, string.Empty);
        device.Channels.Add(new CameraChannel
        {
            Id = Guid.Parse("93000000-0000-0000-0000-000000000003"),
            DeviceId = device.Id,
            ChannelNo = 1,
            ChannelName = "Duplicate Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-duplicate",
            Enabled = true
        });

        var result = await InvokeAsync(
            repository,
            "CreateDeviceAsync",
            device,
            CancellationToken.None);

        Assert.Equal(CatalogRepositoryStatus.ChannelConflict, GetStatus(result));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_devices WHERE id = $id;",
                ("$id", device.Id.ToString("N"))));
        Assert.Equal(
            "0",
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM camera_channels WHERE device_id = $id;",
                ("$id", device.Id.ToString("N"))));
    }

    [Fact]
    public async Task GetMissingGroupAndDevice_ReturnNull()
    {
        await using var context = await TestContext.CreateAsync();
        var repository = CreateRepository(context, new CountingSecretProtector());

        Assert.Null(await GetGroupAsync(repository, Guid.Parse("94000000-0000-0000-0000-000000000001")));
        Assert.Null(await GetDeviceAsync(repository, Guid.Parse("95000000-0000-0000-0000-000000000001")));
    }

    private static object CreateRepository(TestContext context, ISecretProtector protector)
    {
        var repositoryType = typeof(SqliteConnectionFactory).Assembly.GetType(
            "VideoMonitor.Infrastructure.Persistence.SqliteCentralCatalogRepository");
        Assert.NotNull(repositoryType);

        var repository = Activator.CreateInstance(
            repositoryType!,
            context.Factory,
            protector);
        Assert.NotNull(repository);
        return repository!;
    }

    private static async Task<CatalogSnapshotDto> GetCatalogAsync(object repository) =>
        Assert.IsType<CatalogSnapshotDto>(await InvokeAsync(
            repository,
            "GetCatalogAsync",
            CancellationToken.None));

    private static async Task<DeviceGroupDto?> GetGroupAsync(object repository, Guid id) =>
        (DeviceGroupDto?)await InvokeAsync(
            repository,
            "GetGroupAsync",
            id,
            CancellationToken.None);

    private static async Task<CameraDeviceDto?> GetDeviceAsync(object repository, Guid id) =>
        (CameraDeviceDto?)await InvokeAsync(
            repository,
            "GetDeviceAsync",
            id,
            CancellationToken.None);

    private static async Task<DeviceGroupDto> CreateGroupAsync(object repository)
    {
        var group = new DeviceGroup
        {
            Id = Guid.Parse("92000000-0000-0000-0000-000000000001"),
            Name = "Device Group",
            Sort = 1,
            Enabled = true,
            Revision = 15
        };
        var result = await InvokeAsync(
            repository,
            "CreateGroupAsync",
            group,
            CancellationToken.None);
        Assert.Equal(CatalogRepositoryStatus.Success, GetStatus(result));
        return Assert.IsType<DeviceGroupDto>(GetProperty(result, "Value"));
    }

    private static CameraDevice CreateDevice(
        Guid groupId,
        string password,
        Guid? deviceId = null,
        Guid? mainChannelId = null,
        Guid? subChannelId = null,
        string name = "Central Camera")
    {
        var actualDeviceId = deviceId ??
            Guid.Parse("96000000-0000-0000-0000-000000000001");
        var actualMainChannelId = mainChannelId ??
            Guid.Parse("97000000-0000-0000-0000-000000000001");
        var actualSubChannelId = subChannelId ??
            Guid.Parse("97000000-0000-0000-0000-000000000002");
        var device = new CameraDevice
        {
            Id = actualDeviceId,
            Revision = 99,
            Name = name,
            GroupId = groupId,
            IpAddress = "192.0.2.10",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "camera-user",
            Password = password,
            Manufacturer = "Hikvision",
            Model = "DS-2CD",
            TransportMode = TransportMode.Tcp,
            Status = CameraStatus.Online,
            Enabled = true,
            Remark = "Task 3 test"
        };
        device.Channels.Add(new CameraChannel
        {
            Id = actualMainChannelId,
            DeviceId = actualDeviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Main",
            StreamType = StreamType.Main,
            StreamId = "runtime-main",
            Enabled = true
        });
        device.Channels.Add(new CameraChannel
        {
            Id = actualSubChannelId,
            DeviceId = actualDeviceId,
            ChannelNo = 1,
            ChannelName = "CH1 Sub",
            StreamType = StreamType.Sub,
            StreamId = "runtime-sub",
            Enabled = false
        });
        return device;
    }

    private static CatalogRepositoryStatus GetStatus(object? result) =>
        Assert.IsType<CatalogRepositoryStatus>(GetProperty(result, "Status"));

    private static object? GetProperty(object? source, string name)
    {
        Assert.NotNull(source);
        var property = source!.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property!.GetValue(source);
    }

    private static async Task<object?> InvokeAsync(
        object repository,
        string methodName,
        params object[] arguments)
    {
        var method = repository.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        object? invocation;
        try
        {
            invocation = method!.Invoke(repository, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static async Task<string> ReadScalarAsync(
        TestContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = context.Factory.CreateConnection();
        await connection.OpenAsync();
        return await ReadScalarAsync(connection, sql, parameters);
    }

    private static async Task<string> ReadScalarAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result) ?? string.Empty;
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(string root)
        {
            Provider = new DefaultAppPathProvider(new ServerStorageOptions { RootPath = root });
            new ServerStorageLayout(Provider).EnsureCreated();
            Factory = new SqliteConnectionFactory(Provider);
            Initializer = new SqliteDatabaseInitializer(Factory);
        }

        public DefaultAppPathProvider Provider { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteDatabaseInitializer Initializer { get; }

        public static async Task<TestContext> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VideoMonitorCentralCatalogRepositoryTests",
                Guid.NewGuid().ToString("N"));
            var context = new TestContext(root);
            await context.Initializer.InitializeAsync();
            return context;
        }

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            await Task.Yield();
            try
            {
                if (Directory.Exists(Provider.RootDirectory))
                {
                    Directory.Delete(Provider.RootDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Microsoft.Data.Sqlite may retain a pooled native handle briefly.
            }
        }
    }

    private sealed class CountingSecretProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }

        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            ProtectCalls++;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
            return Task.FromResult($"test-protected:{encoded}");
        }

        public Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            UnprotectCalls++;
            throw new InvalidOperationException("UnprotectAsync must not be called by catalog reads.");
        }

        public void ResetUnprotectCalls() => UnprotectCalls = 0;
    }
}
