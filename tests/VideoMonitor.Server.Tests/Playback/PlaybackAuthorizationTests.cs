using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;
using VideoMonitor.Server.Media;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Playback;

public sealed class PlaybackAuthorizationTests
{
    [Fact]
    public async Task EnsureValidIdsReturnsSafePlaybackResponse()
    {
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var mediaKey = new MediaStreamKey(deviceId, channelId, StreamType.Main);
        var stream = new FormalStreamDescriptor(
            "vhost",
            "videomonitor",
            mediaKey.ToFormalStreamId(),
            mediaKey);
        var settings = new MediaRuntimeSettings(
            "http://zlm.example:1985",
            "rtsp://playback.example:8554",
            "vhost",
            "videomonitor",
            "videomonitor-test",
            "zlm-secret",
            30,
            1);
        var service = new PlaybackStreamService(
            new FixedCatalogRepository(new CameraDeviceDto(
                deviceId,
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
                true,
                "",
                1,
                new[]
                {
                    new CameraChannelDto(
                        channelId,
                        deviceId,
                        1,
                        "main",
                        StreamType.Main,
                        true)
                })),
            new FixedSourceResolver(new ResolvedCameraSource(
                mediaKey,
                new Uri("rtsp://camera.example/live"),
                "binding")),
            new FixedRuntimeSettingsProvider(settings),
            new ReadyStreamManager(mediaKey, stream),
            new PlaybackTicketIssuer(new FixedSigningKeyProvider(GetKey())),
            new PlaybackUrlBuilder(new FixedRuntimeSettingsProvider(settings)));

        var response = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleEnsureAsync(
                CreateJsonRequest(new EnsurePlaybackStreamRequest(
                    deviceId,
                    channelId,
                    StreamType.Main)).Request,
                ReadyState(),
                service));

        Assert.Equal(200, response.StatusCode);
        Assert.Contains(mediaKey.ToFormalStreamId(), response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin_params", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongDeviceChannelRelationIsRejected()
    {
        var response = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleEnsureAsync(
                CreateJsonRequest(new EnsurePlaybackStreamRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    VideoMonitor.Core.Models.StreamType.Main)).Request,
                ReadyState(),
                new FixedPlaybackStreamService(Failure(
                    400,
                    "CATALOG_VALIDATION_FAILED",
                    "Playback request validation failed."))));

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("CATALOG_VALIDATION_FAILED", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongStreamTypeIsRejected()
    {
        var response = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleEnsureAsync(
                CreateJsonRequest(new EnsurePlaybackStreamRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    VideoMonitor.Core.Models.StreamType.Sub)).Request,
                ReadyState(),
                new FixedPlaybackStreamService(Failure(
                    400,
                    "CATALOG_VALIDATION_FAILED",
                    "Playback request validation failed."))));

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("CATALOG_VALIDATION_FAILED", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotOwnedIdentityConflictIsSafe()
    {
        var response = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleEnsureAsync(
                CreateJsonRequest(new EnsurePlaybackStreamRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    VideoMonitor.Core.Models.StreamType.Main)).Request,
                ReadyState(),
                new FixedPlaybackStreamService(Failure(
                    409,
                    "MediaStreamIdentityConflict",
                    "Playback media identity is unavailable."))));

        Assert.Equal(409, response.StatusCode);
        Assert.DoesNotContain("originUrl", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZlmUnavailableIsSafe()
    {
        var response = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleEnsureAsync(
                CreateJsonRequest(new EnsurePlaybackStreamRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    VideoMonitor.Core.Models.StreamType.Main)).Request,
                ReadyState(),
                new FixedPlaybackStreamService(Failure(
                    503,
                    "MEDIA_UNAVAILABLE",
                    "Playback media is unavailable."))));

        Assert.Equal(503, response.StatusCode);
        Assert.DoesNotContain("connectionString", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zlm-secret", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaybackFailureLogsSafeDiagnosticsWithoutSensitiveValues()
    {
        var deviceId = Guid.Parse("93000000-0000-0000-0000-000000000001");
        var channelId = Guid.Parse("94000000-0000-0000-0000-000000000001");
        var key = new MediaStreamKey(deviceId, channelId, StreamType.Main);
        var logger = new RecordingLogger();
        var service = new PlaybackStreamService(
            new FixedCatalogRepository(new CameraDeviceDto(
                deviceId,
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
                true,
                "",
                1,
                new[]
                {
                    new CameraChannelDto(
                        channelId,
                        deviceId,
                        1,
                        "main",
                        StreamType.Main,
                        true)
                })),
            new ThrowingSourceResolver(),
            new FixedRuntimeSettingsProvider(new MediaRuntimeSettings(
                "http://zlm.example:1985",
                "rtsp://playback.example:8554",
                "vhost",
                "videomonitor",
                "videomonitor-test",
                "zlm-secret",
                30,
                1)),
            new ReadyStreamManager(key, new FormalStreamDescriptor(
                "vhost",
                "videomonitor",
                key.ToFormalStreamId(),
                key)),
            new PlaybackTicketIssuer(new FixedSigningKeyProvider(GetKey())),
            new PlaybackUrlBuilder(new FixedRuntimeSettingsProvider(new MediaRuntimeSettings(
                "http://zlm.example:1985",
                "rtsp://playback.example:8554",
                "vhost",
                "videomonitor",
                "videomonitor-test",
                "zlm-secret",
                30,
                1))),
            logger);

        var result = await service.EnsureAsync(new EnsurePlaybackStreamRequest(
            deviceId,
            channelId,
            StreamType.Main));

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("Playback stream failed safely.", message);
        Assert.Contains("FailureCode=MEDIA_UNAVAILABLE", message);
        Assert.Contains("Stage=ResolveSource", message);
        Assert.Contains(deviceId.ToString(), message);
        Assert.Contains(channelId.ToString(), message);
        Assert.Contains("StreamType=Main", message);
        Assert.Contains("ExceptionType=InvalidOperationException", message);
        Assert.Null(logger.Exceptions.Single());
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnPlayRequiresExactVhostAppAndStream()
    {
        var key = GetKey();
        var ticket = await IssueAsync(key, "vhost", "videomonitor", "stream");
        var validator = new PlaybackTicketValidator(new FixedSigningKeyProvider(key));

        var success = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleOnPlayAsync(
                CreateHookContext("vhost", "videomonitor", "stream", ticket.Value),
                new TrustPolicy(true),
                validator));
        var mismatch = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleOnPlayAsync(
                CreateHookContext("vhost", "videomonitor", "other", ticket.Value),
                new TrustPolicy(true),
                validator));

        Assert.Equal(200, success.StatusCode);
        Assert.Contains("\"code\":0", success.Body, StringComparison.Ordinal);
        Assert.Equal(200, mismatch.StatusCode);
        Assert.Contains("\"code\":1", mismatch.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPlayRejectsUntrustedCallerBeforeTicketValidation()
    {
        var keyProvider = new RecordingSigningKeyProvider();
        var validator = new PlaybackTicketValidator(keyProvider);
        var context = CreateHookContext(
            "vhost",
            "app",
            "stream",
            "ticket=not-read");
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");

        var result = await PlaybackAuthorizationEndpoints.HandleOnPlayAsync(
            context,
            new TrustPolicy(false),
            validator);

        var response = await ExecuteAsync(result);
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(0, keyProvider.Calls);
    }

    [Fact]
    public async Task FormalTicketCannotAuthorizeTest()
    {
        var key = GetKey();
        var ticket = await IssueAsync(key, "vhost", "videomonitor", "stream");
        var result = await PlaybackAuthorizationEndpoints.HandleOnPlayAsync(
            CreateHookContext("vhost", "videomonitor-test", "stream", ticket.Value),
            new TrustPolicy(true),
            new PlaybackTicketValidator(new FixedSigningKeyProvider(key)));

        var response = await ExecuteAsync(result);
        Assert.Contains("\"code\":1", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestTicketCannotAuthorizeFormal()
    {
        var key = GetKey();
        var ticket = await IssueAsync(key, "vhost", "videomonitor-test", "stream");
        var result = await PlaybackAuthorizationEndpoints.HandleOnPlayAsync(
            CreateHookContext("vhost", "videomonitor", "stream", ticket.Value),
            new TrustPolicy(true),
            new PlaybackTicketValidator(new FixedSigningKeyProvider(key)));

        var response = await ExecuteAsync(result);
        Assert.Contains("\"code\":1", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAdminBypassIsReturnedToClient()
    {
        var safe = new EnsurePlaybackStreamResponse(
            "vm_formal",
            new Uri("rtsp://playback.example/videomonitor/vm_formal?ticket=only-ticket"),
            DateTimeOffset.UtcNow.AddSeconds(60),
            StreamRuntimeState.Ready);
        var response = await ExecuteAsync(
            await PlaybackAuthorizationEndpoints.HandleEnsureAsync(
                CreateJsonRequest(new EnsurePlaybackStreamRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    VideoMonitor.Core.Models.StreamType.Main)).Request,
                ReadyState(),
                new FixedPlaybackStreamService(new CatalogOperationResult<EnsurePlaybackStreamResponse>(
                    true,
                    safe,
                    200,
                    null))));

        Assert.DoesNotContain("admin_params", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ZlmSecret", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera-password", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static CatalogOperationResult<EnsurePlaybackStreamResponse> Failure(
        int statusCode,
        string code,
        string message) =>
        new(false, null, statusCode, new CatalogErrorDto(code, message));

    private static ServerReadinessState ReadyState()
    {
        var readiness = new ServerReadinessState();
        readiness.MarkDatabaseReady();
        readiness.MarkSecretProtectionReady();
        return readiness;
    }

    private static DefaultHttpContext CreateJsonRequest<T>(T payload)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload));
        return context;
    }

    private static DefaultHttpContext CreateHookContext(
        string vhost,
        string app,
        string stream,
        string ticket)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var payload = JsonSerializer.Serialize(new
        {
            vhost,
            app,
            stream,
            @params = "ticket=" + Uri.EscapeDataString(ticket)
        });
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        return context;
    }

    private static async Task<PlaybackTicket> IssueAsync(
        byte[] key,
        string vhost,
        string app,
        string stream) =>
        await new PlaybackTicketIssuer(
                new FixedSigningKeyProvider(key),
                () => DateTimeOffset.UtcNow)
            .IssueAsync(new PlaybackMediaIdentity(vhost, app, stream));

    private static byte[] GetKey() =>
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return ((int)context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private sealed class FixedPlaybackStreamService : IPlaybackStreamService
    {
        private readonly CatalogOperationResult<EnsurePlaybackStreamResponse> result;

        public FixedPlaybackStreamService(
            CatalogOperationResult<EnsurePlaybackStreamResponse> result) =>
            this.result = result;

        public Task<CatalogOperationResult<EnsurePlaybackStreamResponse>> EnsureAsync(
            EnsurePlaybackStreamRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixedCatalogRepository : ICentralCatalogRepository
    {
        private readonly CameraDeviceDto device;

        public FixedCatalogRepository(CameraDeviceDto device) => this.device = device;

        public Task<CameraDeviceDto?> GetDeviceAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraDeviceDto?>(id == device.Id ? device : null);

        public Task<CatalogSnapshotDto> GetCatalogAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSnapshotDto(
                Array.Empty<DeviceGroupDto>(),
                new[] { device }));

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
        private readonly ResolvedCameraSource source;

        public FixedSourceResolver(ResolvedCameraSource source) => this.source = source;

        public Task<ResolvedCameraSource> ResolveAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(source);
    }

    private sealed class ThrowingSourceResolver : ICameraSourceResolver
    {
        public Task<ResolvedCameraSource> ResolveAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("rtsp://camera-password-secret");
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

    private sealed class ReadyStreamManager : IStreamManager
    {
        private readonly MediaStreamKey key;
        private readonly FormalStreamDescriptor stream;

        public ReadyStreamManager(MediaStreamKey key, FormalStreamDescriptor stream)
        {
            this.key = key;
            this.stream = stream;
        }

        public Task<StreamEnsureResult> EnsureStreamAsync(
            MediaStreamRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StreamEnsureResult(true, stream, null));

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
                        StreamRuntimeState.Ready,
                        SourceObservation.Reachable,
                        new ViewerCount(0),
                        StreamOwnership.OwnedCurrentProcess,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        false)
                });
    }

    private sealed class FixedSigningKeyProvider : IPlaybackSigningKeyProvider
    {
        private readonly byte[] key;

        public FixedSigningKeyProvider(byte[] key) => this.key = key;

        public Task<byte[]> GetOrCreateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult((byte[])key.Clone());
    }

    private sealed class RecordingSigningKeyProvider : IPlaybackSigningKeyProvider
    {
        public int Calls { get; private set; }

        public Task<byte[]> GetOrCreateAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(GetKey());
        }
    }

    private sealed class TrustPolicy : IZlmHookTrustPolicy
    {
        private readonly bool trusted;

        public TrustPolicy(bool trusted) => this.trusted = trusted;

        public bool IsTrusted(IPAddress? remoteAddress) => trusted;
    }

    private sealed class RecordingLogger : ILogger<PlaybackStreamService>
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

}
