using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Catalog;

namespace VideoMonitor.Server.Playback;

public sealed class PlaybackStreamService : IPlaybackStreamService
{
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string ValidationMessage = "Playback request validation failed.";
    private const string UnavailableCode = "MEDIA_UNAVAILABLE";
    private const string UnavailableMessage = "Playback media is unavailable.";
    private const string RetryUnavailableCode = "MEDIA_DIAGNOSTICS_RETRY_FAILED";

    private readonly IFormalStreamEnsureService formalEnsureService;
    private readonly IPlaybackTicketIssuer ticketIssuer;
    private readonly IPlaybackUrlBuilder urlBuilder;
    private readonly ILogger<PlaybackStreamService> logger;

    public PlaybackStreamService(
        IFormalStreamEnsureService formalEnsureService,
        IPlaybackTicketIssuer ticketIssuer,
        IPlaybackUrlBuilder urlBuilder,
        ILogger<PlaybackStreamService>? logger = null)
    {
        this.formalEnsureService = formalEnsureService
            ?? throw new ArgumentNullException(nameof(formalEnsureService));
        this.ticketIssuer = ticketIssuer
            ?? throw new ArgumentNullException(nameof(ticketIssuer));
        this.urlBuilder = urlBuilder
            ?? throw new ArgumentNullException(nameof(urlBuilder));
        this.logger = logger ?? NullLogger<PlaybackStreamService>.Instance;
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
            var ensured = await formalEnsureService
                .EnsureAsync(
                    new FormalStreamEnsureRequest(
                        request.DeviceId,
                        request.ChannelId,
                        request.StreamType),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!ensured.IsSuccess || ensured.Value is null)
            {
                return MapEnsureFailure(ensured);
            }

            var identity = ensured.Value.MediaIdentity;
            var ticket = await ticketIssuer
                .IssueAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            var playbackUrl = await urlBuilder
                .BuildAsync(identity, ticket, cancellationToken)
                .ConfigureAwait(false);
            return new CatalogOperationResult<EnsurePlaybackStreamResponse>(
                true,
                new EnsurePlaybackStreamResponse(
                    identity.Stream,
                    playbackUrl,
                    ticket.ExpiresUtc,
                    ensured.Value.RuntimeState),
                StatusCodes.Status200OK,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(request, UnavailableCode, "IssueTicket", exception.GetType().Name);
            return Failure<EnsurePlaybackStreamResponse>(
                StatusCodes.Status503ServiceUnavailable,
                UnavailableCode,
                UnavailableMessage);
        }
    }

    private void LogFailure(
        EnsurePlaybackStreamRequest request,
        string failureCode,
        string stage,
        string exceptionType)
    {
        logger.LogError(
            "Playback stream failed safely. Operation={Operation} FailureCode={FailureCode} Stage={Stage} DeviceId={DeviceId} ChannelId={ChannelId} StreamType={StreamType} ExceptionType={ExceptionType}",
            "PlaybackStream.Ensure",
            failureCode,
            stage,
            request.DeviceId,
            request.ChannelId,
            request.StreamType,
            exceptionType);
    }

    private static CatalogOperationResult<EnsurePlaybackStreamResponse> MapEnsureFailure(
        CatalogOperationResult<FormalStreamEnsureResult> result)
    {
        if (result.Error is not null)
        {
            return Failure<EnsurePlaybackStreamResponse>(
                result.StatusCode,
                result.Error.Code,
                result.Error.Message);
        }

        return Failure<EnsurePlaybackStreamResponse>(
            StatusCodes.Status503ServiceUnavailable,
            RetryUnavailableCode,
            "Formal playback ensure is unavailable.");
    }

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
