using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Media;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Playback;

public sealed class FormalStreamEnsureServiceTests
{
    [Fact]
    public async Task ValidFormalEnsureReturnsMediaIdentity()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.EnsuredStream.Vhost, result.Value?.MediaIdentity.Vhost);
        Assert.Equal(fixture.EnsuredStream.App, result.Value?.MediaIdentity.App);
        Assert.Equal(fixture.EnsuredStream.Stream, result.Value?.MediaIdentity.Stream);
        Assert.Equal(StreamRuntimeState.Ready, result.Value?.RuntimeState);
    }

    [Fact]
    public async Task InvalidStableIdentityIsRejected()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.EnsureAsync(
            new FormalStreamEnsureRequest(Guid.Empty, fixture.ChannelId, StreamType.Main));

        AssertFailure(result, StatusCodes.Status400BadRequest, "CATALOG_VALIDATION_FAILED");
        Assert.Equal(0, fixture.StreamManager.EnsureCalls);
    }

    [Fact]
    public async Task DeviceNotFoundIsSafe()
    {
        var fixture = CreateFixture(includeDevice: false);

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status404NotFound, "PLAYBACK_DEVICE_NOT_FOUND");
    }

    [Fact]
    public async Task ChannelNotFoundIsSafe()
    {
        var fixture = CreateFixture(channels: Array.Empty<CameraChannelDto>());

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status404NotFound, "PLAYBACK_CHANNEL_NOT_FOUND");
    }

    [Fact]
    public async Task WrongDeviceChannelRelationIsRejected()
    {
        var fixture = CreateFixture(channelDeviceId: Guid.NewGuid());

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status400BadRequest, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task DisabledDeviceIsRejected()
    {
        var fixture = CreateFixture(deviceEnabled: false);

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status400BadRequest, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task DisabledChannelIsRejected()
    {
        var fixture = CreateFixture(channelEnabled: false);

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status400BadRequest, "CATALOG_VALIDATION_FAILED");
    }

    [Fact]
    public async Task SourceResolutionFailureIsSafe()
    {
        var fixture = CreateFixture(sourceException: new InvalidOperationException(
            "rtsp://camera-password-secret"));

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status503ServiceUnavailable, "MEDIA_UNAVAILABLE");
        Assert.DoesNotContain("camera-password-secret", result.Error?.Message ?? "");
    }

    [Fact]
    public async Task StreamManagerEnsureFailureIsMappedSafely()
    {
        var fixture = CreateFixture(streamFailureCode: "MediaStreamIdentityConflict");

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status409Conflict, "MediaStreamIdentityConflict");
    }

    [Fact]
    public async Task RuntimeNotReadyReturnsMediaUnavailable()
    {
        var fixture = CreateFixture(runtimeState: StreamRuntimeState.Starting);

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        AssertFailure(result, StatusCodes.Status503ServiceUnavailable, "MEDIA_UNAVAILABLE");
    }

    [Fact]
    public async Task FormalEnsureResultContainsNoPlaybackTicket()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.EnsureAsync(fixture.Request);
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("PlaybackTicket", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TicketExpiresUtc", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FormalEnsureResultContainsNoPlaybackUrl()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.EnsureAsync(fixture.Request);
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("PlaybackUrl", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FormalEnsureUsesEnsuredStreamIdentity()
    {
        var fixture = CreateFixture(
            ensuredStream: new FormalStreamDescriptor(
                "ensured-vhost",
                "ensured-app",
                "ensured-stream",
                Key));

        var result = await fixture.Service.EnsureAsync(fixture.Request);

        Assert.True(result.IsSuccess);
        Assert.Equal("ensured-vhost", result.Value?.MediaIdentity.Vhost);
        Assert.Equal("ensured-app", result.Value?.MediaIdentity.App);
        Assert.Equal("ensured-stream", result.Value?.MediaIdentity.Stream);
        Assert.Equal(
            Key.ToFormalStreamId(),
            fixture.StreamManager.LastRequest?.Stream);
    }

    private static readonly MediaStreamKey Key = new(
        Guid.Parse("95000000-0000-0000-0000-000000000001"),
        Guid.Parse("96000000-0000-0000-0000-000000000001"),
        StreamType.Main);

    private static Fixture CreateFixture(
        CameraDeviceDto? device = null,
        bool includeDevice = true,
        IReadOnlyList<CameraChannelDto>? channels = null,
        Guid? channelDeviceId = null,
        bool deviceEnabled = true,
        bool channelEnabled = true,
        Exception? sourceException = null,
        string? streamFailureCode = null,
        StreamRuntimeState runtimeState = StreamRuntimeState.Ready,
        FormalStreamDescriptor? ensuredStream = null)
    {
        CameraDeviceDto? actualDevice = device ?? new CameraDeviceDto(
            Key.DeviceId,
            Guid.NewGuid(),
            "camera",
            "192.0.2.10",
            8000,
            554,
            "admin",
            false,
            "manufacturer",
            "model",
            TransportMode.Auto,
            deviceEnabled,
            "",
            1,
            channels ?? new[]
            {
                new CameraChannelDto(
                    Key.ChannelId,
                    channelDeviceId ?? Key.DeviceId,
                    1,
                    "main",
                    StreamType.Main,
                    channelEnabled)
            });
        if (!includeDevice)
        {
            actualDevice = null;
        }
        var descriptor = ensuredStream ?? new FormalStreamDescriptor(
            "vhost",
            "videomonitor",
            Key.ToFormalStreamId(),
            Key);
        var streamManager = new FakeStreamManager(
            Key,
            descriptor,
            runtimeState,
            streamFailureCode);
        var service = new FormalStreamEnsureService(
            new FixedCatalogRepository(actualDevice),
            new FixedSourceResolver(sourceException),
            new FixedRuntimeSettingsProvider(new MediaRuntimeSettings(
                "http://zlm.example:1985",
                "rtsp://playback.example:8554",
                "vhost",
                "videomonitor",
                "videomonitor-test",
                "zlm-secret",
                30,
                1)),
            streamManager);

        return new Fixture(
            service,
            streamManager,
            new FormalStreamEnsureRequest(Key.DeviceId, Key.ChannelId, Key.StreamType),
            actualDevice,
            descriptor);
    }

    private static void AssertFailure<T>(
        CatalogOperationResult<T> result,
        int statusCode,
        string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(statusCode, result.StatusCode);
        Assert.Equal(code, result.Error?.Code);
        Assert.NotNull(result.Error?.Message);
    }

    private sealed record Fixture(
        FormalStreamEnsureService Service,
        FakeStreamManager StreamManager,
        FormalStreamEnsureRequest Request,
        CameraDeviceDto? Device,
        FormalStreamDescriptor EnsuredStream)
    {
        public Guid ChannelId => Request.ChannelId;
    }

    private sealed class FixedCatalogRepository : ICentralCatalogRepository
    {
        private readonly CameraDeviceDto? device;

        public FixedCatalogRepository(CameraDeviceDto? device) => this.device = device;

        public Task<CameraDeviceDto?> GetDeviceAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(id == device?.Id ? device : null);

        public Task<CatalogSnapshotDto> GetCatalogAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSnapshotDto(
                Array.Empty<DeviceGroupDto>(),
                device is null ? Array.Empty<CameraDeviceDto>() : new[] { device }));

        public Task<DeviceGroupDto?> GetGroupAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceGroupDto?>(null);

        public Task<CatalogRepositoryResult<DeviceGroupDto>> CreateGroupAsync(
            DeviceGroup group,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.NotFound));

        public Task<CatalogRepositoryResult<CameraDeviceDto>> CreateDeviceAsync(
            CameraDevice device,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.NotFound));

        public Task<CatalogRepositoryResult<DeviceGroupDto>> UpdateGroupAsync(
            DeviceGroup group,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogRepositoryResult<DeviceGroupDto>(
                CatalogRepositoryStatus.NotFound));

        public Task<CatalogRepositoryDeleteResult> DeleteGroupAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.NotFound));

        public Task<CatalogRepositoryResult<CameraDeviceDto>> UpdateDeviceAsync(
            CameraDevice device,
            string? newPassword,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogRepositoryResult<CameraDeviceDto>(
                CatalogRepositoryStatus.NotFound));

        public Task<CatalogRepositoryDeleteResult> DeleteDeviceAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogRepositoryDeleteResult(
                CatalogRepositoryStatus.NotFound));
    }

    private sealed class FixedSourceResolver : ICameraSourceResolver
    {
        private readonly Exception? exception;

        public FixedSourceResolver(Exception? exception) => this.exception = exception;

        public Task<ResolvedCameraSource> ResolveAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(new ResolvedCameraSource(
                key,
                new Uri("rtsp://camera.example/live"),
                "binding"));
        }
    }

    private sealed class FixedRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        private readonly MediaRuntimeSettings settings;

        public FixedRuntimeSettingsProvider(MediaRuntimeSettings settings) =>
            this.settings = settings;

        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }

    private sealed class FakeStreamManager : IStreamManager
    {
        private readonly MediaStreamKey key;
        private readonly FormalStreamDescriptor stream;
        private readonly StreamRuntimeState runtimeState;
        private readonly string? failureCode;

        public FakeStreamManager(
            MediaStreamKey key,
            FormalStreamDescriptor stream,
            StreamRuntimeState runtimeState,
            string? failureCode)
        {
            this.key = key;
            this.stream = stream;
            this.runtimeState = runtimeState;
            this.failureCode = failureCode;
        }

        public int EnsureCalls { get; private set; }

        public MediaStreamRequest? LastRequest { get; private set; }

        public Task<StreamEnsureResult> EnsureStreamAsync(
            MediaStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            LastRequest = request;
            return Task.FromResult(failureCode is null
                ? new StreamEnsureResult(true, stream, null)
                : new StreamEnsureResult(false, null, failureCode));
        }

        public Task CleanupOwnedStreamIfEligibleAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public MediaRuntimeSnapshot GetSnapshot() =>
            new(
                MediaServerHealth.Healthy,
                new[]
                {
                    new MediaStreamRuntimeInfo(
                        key,
                        runtimeState,
                        SourceObservation.Reachable,
                        new ViewerCount(0),
                        StreamOwnership.OwnedCurrentProcess,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        false)
                });
    }
}
