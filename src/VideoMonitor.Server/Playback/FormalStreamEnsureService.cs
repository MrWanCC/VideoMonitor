using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Playback;

public sealed class FormalStreamEnsureService : IFormalStreamEnsureService
{
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string ValidationMessage = "Playback request validation failed.";
    private const string NotFoundMessage = "Playback identity was not found.";
    private const string UnavailableCode = "MEDIA_UNAVAILABLE";
    private const string UnavailableMessage = "Playback media is unavailable.";
    private const string ConflictCode = "MediaStreamIdentityConflict";
    private const string ConflictMessage = "Playback media identity is unavailable.";

    private readonly ICentralCatalogRepository catalogRepository;
    private readonly ICameraSourceResolver sourceResolver;
    private readonly IMediaRuntimeSettingsProvider settingsProvider;
    private readonly IStreamManager streamManager;
    private readonly ILogger<FormalStreamEnsureService> logger;

    public FormalStreamEnsureService(
        ICentralCatalogRepository catalogRepository,
        ICameraSourceResolver sourceResolver,
        IMediaRuntimeSettingsProvider settingsProvider,
        IStreamManager streamManager,
        ILogger<FormalStreamEnsureService>? logger = null)
    {
        this.catalogRepository = catalogRepository
            ?? throw new ArgumentNullException(nameof(catalogRepository));
        this.sourceResolver = sourceResolver
            ?? throw new ArgumentNullException(nameof(sourceResolver));
        this.settingsProvider = settingsProvider
            ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.streamManager = streamManager
            ?? throw new ArgumentNullException(nameof(streamManager));
        this.logger = logger ?? NullLogger<FormalStreamEnsureService>.Instance;
    }

    public async Task<CatalogOperationResult<FormalStreamEnsureResult>> EnsureAsync(
        FormalStreamEnsureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DeviceId == Guid.Empty
            || request.ChannelId == Guid.Empty
            || !Enum.IsDefined(request.StreamType))
        {
            return Failure<FormalStreamEnsureResult>(
                StatusCodes.Status400BadRequest,
                ValidationCode,
                ValidationMessage);
        }

        var stage = "ResolveCatalog";
        try
        {
            var device = await catalogRepository
                .GetDeviceAsync(request.DeviceId, cancellationToken)
                .ConfigureAwait(false);
            if (device is null)
            {
                return Failure<FormalStreamEnsureResult>(
                    StatusCodes.Status404NotFound,
                    "PLAYBACK_DEVICE_NOT_FOUND",
                    NotFoundMessage);
            }

            var channel = device.Channels.FirstOrDefault(
                candidate => candidate.Id == request.ChannelId);
            if (channel is null)
            {
                return Failure<FormalStreamEnsureResult>(
                    StatusCodes.Status404NotFound,
                    "PLAYBACK_CHANNEL_NOT_FOUND",
                    NotFoundMessage);
            }

            if (channel.DeviceId != device.Id
                || channel.StreamType != request.StreamType
                || !device.Enabled
                || !channel.Enabled)
            {
                return Failure<FormalStreamEnsureResult>(
                    StatusCodes.Status400BadRequest,
                    ValidationCode,
                    ValidationMessage);
            }

            var key = new MediaStreamKey(
                device.Id,
                channel.Id,
                channel.StreamType);
            stage = "ResolveSource";
            var resolvedSource = await sourceResolver
                .ResolveAsync(key, cancellationToken)
                .ConfigureAwait(false);
            var settings = await settingsProvider
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
            var mediaRequest = new MediaStreamRequest(
                MediaStreamNamespace.Formal,
                key,
                settings.Vhost,
                settings.FormalApp,
                MediaStreamIdGenerator.GenerateFormal(key),
                resolvedSource.SourceUri);

            stage = "AddProxy";
            var ensured = await streamManager
                .EnsureStreamAsync(mediaRequest, cancellationToken)
                .ConfigureAwait(false);
            if (!ensured.IsSuccess || ensured.Stream is null)
            {
                LogFailure(
                    request,
                    ensured.FailureCode ?? UnavailableCode,
                    stage,
                    "None");
                return MapEnsureFailure(ensured.FailureCode);
            }

            stage = "WaitRegistration";
            var runtime = streamManager.GetSnapshot().Streams.FirstOrDefault(
                candidate => candidate.Key == key);
            if (runtime is null || runtime.RuntimeState != StreamRuntimeState.Ready)
            {
                LogFailure(request, UnavailableCode, stage, "None");
                return Failure<FormalStreamEnsureResult>(
                    StatusCodes.Status503ServiceUnavailable,
                    UnavailableCode,
                    UnavailableMessage);
            }

            var identity = new PlaybackMediaIdentity(
                ensured.Stream.Vhost,
                ensured.Stream.App,
                ensured.Stream.Stream);
            return new CatalogOperationResult<FormalStreamEnsureResult>(
                true,
                new FormalStreamEnsureResult(
                    request.DeviceId,
                    request.ChannelId,
                    request.StreamType,
                    identity,
                    runtime.RuntimeState),
                StatusCodes.Status200OK,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(request, UnavailableCode, stage, exception.GetType().Name);
            return Failure<FormalStreamEnsureResult>(
                StatusCodes.Status503ServiceUnavailable,
                UnavailableCode,
                UnavailableMessage);
        }
    }

    private void LogFailure(
        FormalStreamEnsureRequest request,
        string failureCode,
        string stage,
        string exceptionType)
    {
        logger.LogError(
            "Formal stream ensure failed safely. Operation={Operation} FailureCode={FailureCode} Stage={Stage} DeviceId={DeviceId} ChannelId={ChannelId} StreamType={StreamType} ExceptionType={ExceptionType}",
            "FormalStreamEnsure.Ensure",
            failureCode,
            stage,
            request.DeviceId,
            request.ChannelId,
            request.StreamType,
            exceptionType);
    }

    private static CatalogOperationResult<FormalStreamEnsureResult> MapEnsureFailure(
        string? failureCode) =>
        string.Equals(failureCode, ConflictCode, StringComparison.Ordinal)
            ? Failure<FormalStreamEnsureResult>(
                StatusCodes.Status409Conflict,
                ConflictCode,
                ConflictMessage)
            : Failure<FormalStreamEnsureResult>(
                StatusCodes.Status503ServiceUnavailable,
                UnavailableCode,
                UnavailableMessage);

    private static CatalogOperationResult<T> Failure<T>(
        int statusCode,
        string code,
        string message) =>
        new(
            false,
            default,
            statusCode,
            new CatalogErrorDto(code, message));
}
