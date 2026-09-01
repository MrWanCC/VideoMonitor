using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteCameraMediaCredentialReaderTests
{
    [Fact]
    public async Task ReadDecryptsSavedCredentialInternally()
    {
        await using var fixture = await CredentialFixture.CreateAsync();

        var credential = await fixture.Reader.ReadAsync(
            fixture.Device.Id,
            fixture.Channel.Id);

        Assert.Equal(fixture.Device.Id, credential.DeviceId);
        Assert.Equal(fixture.Channel.Id, credential.ChannelId);
        Assert.Equal(fixture.Device.IpAddress, credential.IpAddress);
        Assert.Equal(fixture.Device.RtspPort, credential.RtspPort);
        Assert.Equal(fixture.Device.Username, credential.Username);
        Assert.Equal(fixture.PasswordMarker, credential.Password);
        Assert.Equal(fixture.Channel.ChannelNo, credential.ChannelNo);
        Assert.Equal(fixture.Channel.StreamType, credential.StreamType);
        Assert.Equal(fixture.Device.TransportMode, credential.TransportMode);
        Assert.Equal(
            $"camera-password:{fixture.Device.Id:N}",
            fixture.Protector.LastUnprotectPurpose);
        Assert.Equal(1, fixture.Protector.UnprotectCalls);
    }

    [Fact]
    public async Task WrongDeviceChannelRelationFailsSafely()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var other = await fixture.CreateSecondDeviceAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Reader.ReadAsync(fixture.Device.Id, other.Channels.Single().Id));

        Assert.DoesNotContain(fixture.PasswordMarker, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ciphertext", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Protector.UnprotectCalls);
    }

    private sealed class CredentialFixture : IAsyncDisposable
    {
        private CredentialFixture(string root)
        {
            Provider = new DefaultAppPathProvider(new ServerStorageOptions { RootPath = root });
            new ServerStorageLayout(Provider).EnsureCreated();
            Factory = new SqliteConnectionFactory(Provider);
            Initializer = new SqliteDatabaseInitializer(Factory);
            Protector = new RecordingProtector();
            Repository = new SqliteCentralCatalogRepository(Factory, Protector);
            Reader = new SqliteCameraMediaCredentialReader(Factory, Protector);
        }

        public DefaultAppPathProvider Provider { get; }
        public SqliteConnectionFactory Factory { get; }
        public SqliteDatabaseInitializer Initializer { get; }
        public RecordingProtector Protector { get; }
        public SqliteCentralCatalogRepository Repository { get; }
        public SqliteCameraMediaCredentialReader Reader { get; }
        public CameraDevice Device { get; private set; } = null!;
        public CameraChannel Channel => Device.Channels.Single();
        public string PasswordMarker { get; } = "fake-camera-password";

        public static async Task<CredentialFixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VideoMonitorCredentialReaderTests",
                Guid.NewGuid().ToString("N"));
            var fixture = new CredentialFixture(root);
            await fixture.Initializer.InitializeAsync();

            var groupResult = await fixture.Repository.CreateGroupAsync(new DeviceGroup
            {
                Id = Guid.NewGuid(),
                Name = "Credential Test Group"
            });
            var group = groupResult.Value!;
            fixture.Device = CreateDevice(group.Id, fixture.PasswordMarker);
            var result = await fixture.Repository.CreateDeviceAsync(fixture.Device);
            Assert.Equal(CatalogRepositoryStatus.Success, result.Status);
            return fixture;
        }

        public async Task<CameraDevice> CreateSecondDeviceAsync()
        {
            var second = CreateDevice(
                Device.GroupId,
                "other-password",
                Guid.NewGuid(),
                Guid.NewGuid());
            var result = await Repository.CreateDeviceAsync(second);
            Assert.Equal(CatalogRepositoryStatus.Success, result.Status);
            return second;
        }

        public async ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Provider.RootDirectory))
            {
                Directory.Delete(Provider.RootDirectory, recursive: true);
            }

            await Task.CompletedTask;
        }

        private static CameraDevice CreateDevice(
            Guid groupId,
            string password,
            Guid? deviceId = null,
            Guid? channelId = null)
        {
            var actualDeviceId = deviceId ?? Guid.NewGuid();
            return new CameraDevice
            {
                Id = actualDeviceId,
                GroupId = groupId,
                Name = "Credential Camera",
                IpAddress = "192.168.0.20",
                RtspPort = 554,
                Username = "camera-user",
                Password = password,
                TransportMode = TransportMode.Tcp,
                Channels =
                {
                    new CameraChannel
                    {
                        Id = channelId ?? Guid.NewGuid(),
                        DeviceId = actualDeviceId,
                        ChannelNo = 1,
                        ChannelName = "Main",
                        StreamType = StreamType.Main
                    }
                }
            };
        }
    }

    private sealed class RecordingProtector : ISecretProtector
    {
        public int UnprotectCalls { get; private set; }
        public string? LastUnprotectPurpose { get; private set; }

        public Task<string> ProtectAsync(
            string plaintext,
            string purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"test-protected:{Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(plaintext))}");

        public Task<string> UnprotectAsync(
            string protectedValue,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            UnprotectCalls++;
            LastUnprotectPurpose = purpose;
            if (!protectedValue.StartsWith("test-protected:", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid protected test value.");
            }

            return Task.FromResult(System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(protectedValue["test-protected:".Length..])));
        }
    }
}
