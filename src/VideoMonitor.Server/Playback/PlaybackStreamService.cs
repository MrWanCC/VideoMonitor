using Microsoft.AspNetCore.Http;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Playback;

public sealed class PlaybackStreamService : IPlaybackStreamService
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
    private readonly IPlaybackTicketIssuer ticketIssuer;
    private readonly IPlaybackUrlBuilder urlBuilder;

    public PlaybackStreamService(
        ICentralCatalogRepository catalogRepository,
        ICameraSourceResolver sourceResolver,
        IMediaRuntimeSettingsProvider settingsProvider,
        IStreamManager streamManager,
        IPlaybackTicketIssuer ticketIssuer,
        IPlaybackUrlBuilder urlBuilder)
    {
        this.catalogRepository = catalogRepository
            ?? throw new ArgumentNullException(nameof(catalogRepository));
        this.sourceResolver = sourceResolver
            ?? throw new ArgumentNullException(nameof(sourceResolver));
        this.settingsProvider = settingsProvider
            ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.streamManager = streamManager
            ?? throw new ArgumentNullException(nameof(streamManager));
        this.ticketIssuer = ticketIssuer
            ?? throw new ArgumentNullException(nameof(ticketIssuer));
        this.urlBuilder = urlBuilder
            ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public async Task<CatalogOperationResult<EnsurePlaybackStreamResponse>> EnsureAsync(
        EnsurePlaybackStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DeviceId == Guid.Empty
            || request.ChannelId == Guid.Empty
            || !Enum.IsDefined(request.StreamType))
        {
            return Failure<EnsurePlaybackStreamResponse>(
                StatusCodes.Status400BadRequest,
                ValidationCode,
                ValidationMessage);
        }

        try
        {
            var device = await catalogRepository
                .GetDeviceAsync(request.DeviceId, cancellationToken)
                .ConfigureAwait(false);
            if (device is null)
            {
                return Failure<EnsurePlaybackStreamResponse>(
                    StatusCodes.Status404NotFound,
                    "PLAYBACK_DEVICE_NOT_FOUND",
                    NotFoundMessage);
            }

            var channel = device.Channels.FirstOrDefault(
                candidate => candidate.Id == request.ChannelId);
            if (channel is null)
            {
                return Failure<EnsurePlaybackStreamResponse>(
                    StatusCodes.Status404NotFound,
                    "PLAYBACK_CHANNEL_NOT_FOUND",
                    NotFoundMessage);
            }

            if (channel.DeviceId != device.Id
                || channel.StreamType != request.StreamType
                || !device.Enabled
                || !channel.Enabled)
            {
                return Failure<EnsurePlaybackStreamResponse>(
                    StatusCodes.Status400BadRequest,
                    ValidationCode,
                    ValidationMessage);
            }

            var key = new MediaStreamKey(
                device.Id,
                channel.Id,
                channel.StreamType);
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
            var ensured = await streamManager
                .EnsureStreamAsync(mediaRequest, cancellationToken)
                .ConfigureAwait(false);
            if (!ensured.IsSuccess || ensured.Stream is null)
            {
                return MapEnsureFailure(ensured.FailureCode);
            }

            var runtime = streamManager.GetSnapshot().Streams.FirstOrDefault(
                candidate => candidate.Key == key);
            if (runtime is null || runtime.RuntimeState != StreamRuntimeState.Ready)
            {
                return Failure<EnsurePlaybackStreamResponse>(
                    StatusCodes.Status503ServiceUnavailable,
                    UnavailableCode,
                    UnavailableMessage);
            }

            var identity = new PlaybackMediaIdentity(
                ensured.Stream.Vhost,
                ensured.Stream.App,
                ensured.Stream.Stream);
            var ticket = await ticketIssuer
                .IssueAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            var playbackUrl = await urlBuilder
                .BuildAsync(identity, ticket, cancellationToken)
                .ConfigureAwait(false);
            return new CatalogOperationResult<EnsurePlaybackStreamResponse>(
                true,
                new EnsurePlaybackStreamResponse(
                    ensured.Stream.Stream,
                    playbackUrl,
                    ticket.ExpiresUtc,
                    runtime.RuntimeState),
                StatusCodes.Status200OK,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure<EnsurePlaybackStreamResponse>(
                StatusCodes.Status503ServiceUnavailable,
                UnavailableCode,
                UnavailableMessage);
        }
    }

    private static CatalogOperationResult<EnsurePlaybackStreamResponse> MapEnsureFailure(
        string? failureCode) =>
        string.Equals(failureCode, ConflictCode, StringComparison.Ordinal)
            ? Failure<EnsurePlaybackStreamResponse>(
                StatusCodes.Status409Conflict,
                ConflictCode,
                ConflictMessage)
            : Failure<EnsurePlaybackStreamResponse>(
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
