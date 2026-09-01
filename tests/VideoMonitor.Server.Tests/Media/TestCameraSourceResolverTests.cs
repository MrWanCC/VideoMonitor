using VideoMonitor.Core.Media;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestCameraSourceResolverTests
{
    private static readonly Guid DeviceId =
        Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("92000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task NewDraftReturnsSourceWithoutFormalIdentity()
    {
        var reader = new RecordingCredentialReader();
        var resolver = new TestCameraSourceResolver(reader, new RecordingCatalogRepository());

        var result = await resolver.ResolveAsync(Request(
            null,
            null,
            password: "draft-secret"));

        Assert.Null(result.ExistingDeviceId);
        Assert.Null(result.ExistingChannelId);
        Assert.Equal(1, result.ChannelNo);
        Assert.Equal(StreamType.Main, result.StreamType);
        Assert.Equal(
            "rtsp://admin:draft-secret@10.0.0.5:554/Streaming/Channels/101",
            result.SourceUri.AbsoluteUri);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public async Task ExistingBlankPasswordUsesSavedCredential()
    {
        var reader = new RecordingCredentialReader
        {
            Credential = Credential("saved-secret")
        };
        var resolver = new TestCameraSourceResolver(reader, new RecordingCatalogRepository());

        var result = await resolver.ResolveAsync(Request(
            DeviceId,
            ChannelId,
            password: " "));

        Assert.Equal(DeviceId, result.ExistingDeviceId);
        Assert.Equal(ChannelId, result.ExistingChannelId);
        Assert.Contains("admin:saved-secret@", result.SourceUri.AbsoluteUri);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task ExistingNonEmptyPasswordDoesNotDecryptSavedCredential()
    {
        var reader = new RecordingCredentialReader
        {
            Credential = Credential("saved-secret")
        };
        reader.ThrowOnRead = true;
        var resolver = new TestCameraSourceResolver(reader, new RecordingCatalogRepository());

        var result = await resolver.ResolveAsync(Request(
            DeviceId,
            ChannelId,
            password: "transient-secret"));

        Assert.Contains("admin:transient-secret@", result.SourceUri.AbsoluteUri);
        Assert.DoesNotContain("saved-secret", result.SourceUri.AbsoluteUri);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public async Task NewEmptyPasswordIsAllowed()
    {
        var reader = new RecordingCredentialReader();
        var resolver = new TestCameraSourceResolver(reader, new RecordingCatalogRepository());

        var result = await resolver.ResolveAsync(Request(
            null,
            null,
            password: string.Empty));

        Assert.Equal("rtsp://admin@10.0.0.5:554/Streaming/Channels/101", result.SourceUri.AbsoluteUri);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public async Task WrongExistingDeviceChannelRelationFailsBeforeSourceBuild()
    {
        var reader = new RecordingCredentialReader { ThrowOnRead = true };
        var resolver = new TestCameraSourceResolver(
            reader,
            new RecordingCatalogRepository { Device = null });

        var error = await Assert.ThrowsAsync<TestStreamOperationException>(
            () => resolver.ResolveAsync(Request(DeviceId, ChannelId, "transient-secret")));

        Assert.Equal(TestStreamErrorCode.CatalogUnavailable, error.Code);
        Assert.Equal(0, reader.ReadCalls);
    }

    private static TestStreamStartRequest Request(
        Guid? deviceId,
        Guid? channelId,
        string password) =>
        new(
            deviceId,
            channelId,
            new CameraDeviceDraftDto(
                "10.0.0.5",
                554,
                "admin",
                password,
                1,
                StreamType.Main,
                TransportMode.Auto),
            DateTimeOffset.UtcNow);

    private static CameraMediaCredential Credential(string password) =>
        new(
            DeviceId,
            ChannelId,
            "10.0.0.5",
            554,
            "admin",
            password,
            1,
            StreamType.Main,
            TransportMode.Auto);

    private sealed class RecordingCredentialReader : ICameraMediaCredentialReader
    {
        public CameraMediaCredential? Credential { get; init; }

        public bool ThrowOnRead { get; set; }

        public int ReadCalls { get; private set; }

        public Task<CameraMediaCredential> ReadAsync(
            Guid deviceId,
            Guid channelId,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("credential reader must not be called");
            }

            return Task.FromResult(Credential
                ?? throw new InvalidDataException("credential not configured"));
        }
    }

    private sealed class RecordingCatalogRepository : ICentralCatalogRepository
    {
        public CameraDeviceDto? Device { get; init; } = new(
            DeviceId,
            Guid.Parse("93000000-0000-0000-0000-000000000001"),
            "Test Device",
            "10.0.0.5",
            8000,
            554,
            "admin",
            true,
            "",
            "",
            TransportMode.Auto,
            true,
            "",
            1,
            new[]
            {
                new CameraChannelDto(
                    ChannelId,
                    DeviceId,
                    1,
                    "Main",
                    StreamType.Main,
                    true)
            });

        public Task<CameraDeviceDto?> GetDeviceAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(id == DeviceId ? Device : null);

        private static Task<T> Unsupported<T>() =>
            Task.FromException<T>(new NotSupportedException());

        public Task<CatalogSnapshotDto> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Unsupported<CatalogSnapshotDto>();

        public Task<DeviceGroupDto?> GetGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
            Unsupported<DeviceGroupDto?>();

        public Task<CatalogRepositoryResult<DeviceGroupDto>> CreateGroupAsync(DeviceGroup group, CancellationToken cancellationToken = default) =>
            Unsupported<CatalogRepositoryResult<DeviceGroupDto>>();

        public Task<CatalogRepositoryResult<CameraDeviceDto>> CreateDeviceAsync(CameraDevice device, CancellationToken cancellationToken = default) =>
            Unsupported<CatalogRepositoryResult<CameraDeviceDto>>();

        public Task<CatalogRepositoryResult<DeviceGroupDto>> UpdateGroupAsync(DeviceGroup group, long expectedRevision, CancellationToken cancellationToken = default) =>
            Unsupported<CatalogRepositoryResult<DeviceGroupDto>>();

        public Task<CatalogRepositoryDeleteResult> DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default) =>
            Unsupported<CatalogRepositoryDeleteResult>();

        public Task<CatalogRepositoryResult<CameraDeviceDto>> UpdateDeviceAsync(CameraDevice device, string? newPassword, long expectedRevision, CancellationToken cancellationToken = default) =>
            Unsupported<CatalogRepositoryResult<CameraDeviceDto>>();

        public Task<CatalogRepositoryDeleteResult> DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default) =>
            Unsupported<CatalogRepositoryDeleteResult>();
    }
}
