using VideoMonitor.Core.Media;
using Microsoft.AspNetCore.Http;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Playback;
using VideoMonitor.Server.Catalog;

namespace VideoMonitor.Server.Media;

public interface ITestStreamService
{
    Task<VideoMonitor.Server.Catalog.CatalogOperationResult<TestSessionDto>> StartAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default);

    Task<VideoMonitor.Server.Catalog.CatalogOperationResult<object?>> StopAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class TestStreamOperationException : Exception
{
    public TestStreamOperationException(TestStreamErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public TestStreamErrorCode Code { get; }
}

public sealed class TestStreamService : ITestStreamService
{
    private readonly ITestCameraSourceResolver sourceResolver;
    private readonly ITestStreamProxyController proxyController;
    private readonly IPlaybackTicketIssuer ticketIssuer;
    private readonly IPlaybackUrlBuilder urlBuilder;
    private readonly TestSessionRegistry sessionRegistry;
    private readonly IMediaObservationRecorder observationRecorder;
    private readonly Func<DateTimeOffset> utcNow;

    public TestStreamService(
        ITestCameraSourceResolver sourceResolver,
        ITestStreamProxyController proxyController,
        IPlaybackTicketIssuer ticketIssuer,
        IPlaybackUrlBuilder urlBuilder,
        TestSessionRegistry sessionRegistry,
        IMediaObservationRecorder observationRecorder,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.sourceResolver = sourceResolver
            ?? throw new ArgumentNullException(nameof(sourceResolver));
        this.proxyController = proxyController
            ?? throw new ArgumentNullException(nameof(proxyController));
        this.ticketIssuer = ticketIssuer
            ?? throw new ArgumentNullException(nameof(ticketIssuer));
        this.urlBuilder = urlBuilder
            ?? throw new ArgumentNullException(nameof(urlBuilder));
        this.sessionRegistry = sessionRegistry
            ?? throw new ArgumentNullException(nameof(sessionRegistry));
        this.observationRecorder = observationRecorder
            ?? throw new ArgumentNullException(nameof(observationRecorder));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CatalogOperationResult<TestSessionDto>> StartAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Draft is null)
        {
            return Failure<TestSessionDto>(TestStreamErrorCode.InvalidDraft);
        }

        ResolvedTestCameraSource? source = null;
        TestStreamProxyHandle? handle = null;
        try
        {
            source = await sourceResolver.ResolveAsync(request, cancellationToken)
                .ConfigureAwait(false);
            handle = await proxyController.StartAsync(source, cancellationToken)
                .ConfigureAwait(false);

            var identity = new PlaybackMediaIdentity(
                handle.Vhost,
                handle.App,
                handle.StreamId);
            var ticket = await ticketIssuer.IssueAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            Uri playbackUrl;
            try
            {
                playbackUrl = await urlBuilder
                    .BuildAsync(identity, ticket, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await CleanupAfterPreparationFailureAsync(handle).ConfigureAwait(false);
                RecordFailure(request, source, TestStreamErrorCode.PlaybackPreparationFailed);
                return Failure<TestSessionDto>(TestStreamErrorCode.PlaybackPreparationFailed);
            }

            var session = sessionRegistry.Add(
                handle,
                source.ExistingDeviceId,
                source.ExistingChannelId,
                playbackUrl);
            RecordSuccess(source);
            return new CatalogOperationResult<TestSessionDto>(
                true,
                session,
                StatusCodes.Status200OK,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (handle is not null)
            {
                await CleanupAfterPreparationFailureAsync(handle).ConfigureAwait(false);
            }

            throw;
        }
        catch (TestStreamOperationException exception)
        {
            if (handle is not null)
            {
                await CleanupAfterPreparationFailureAsync(handle).ConfigureAwait(false);
            }

            RecordFailure(request, source, exception.Code);
            return Failure<TestSessionDto>(exception.Code);
        }
        catch
        {
            if (handle is not null)
            {
                await CleanupAfterPreparationFailureAsync(handle).ConfigureAwait(false);
            }

            var code = source is null
                ? TestStreamErrorCode.ConnectFailed
                : TestStreamErrorCode.PlaybackPreparationFailed;
            RecordFailure(request, source, code);
            return Failure<TestSessionDto>(code);
        }
    }

    public async Task<CatalogOperationResult<object?>> StopAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!sessionRegistry.TryGet(sessionId, out var registration))
        {
            return Failure<object?>(TestStreamErrorCode.SessionNotFound);
        }

        try
        {
            await proxyController.StopAsync(registration!.Handle, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure<object?>(TestStreamErrorCode.MediaServerUnavailable);
        }

        if (!sessionRegistry.RemoveAfterSuccessfulCleanup(
                sessionId,
                registration!))
        {
            return Failure<object?>(TestStreamErrorCode.MediaServerUnavailable);
        }

        return new CatalogOperationResult<object?>(
            true,
            null,
            StatusCodes.Status200OK,
            null);
    }

    private async Task CleanupAfterPreparationFailureAsync(
        TestStreamProxyHandle handle)
    {
        try
        {
            await proxyController.StopAsync(handle, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void RecordSuccess(ResolvedTestCameraSource source)
    {
        if (source.ExistingDeviceId is not { } deviceId
            || source.ExistingChannelId is not { } channelId)
        {
            return;
        }

        observationRecorder.Record(
            new MediaStreamKey(deviceId, channelId, source.StreamType),
            SourceObservation.Reachable,
            utcNow(),
            null,
            null);
    }

    private void RecordFailure(
        TestStreamStartRequest request,
        ResolvedTestCameraSource? source,
        TestStreamErrorCode errorCode)
    {
        var deviceId = source?.ExistingDeviceId;
        var channelId = source?.ExistingChannelId;
        if (deviceId is not { } existingDeviceId
            || channelId is not { } existingChannelId)
        {
            return;
        }

        var observation = errorCode == TestStreamErrorCode.AuthFailed
            ? SourceObservation.AuthFailed
            : SourceObservation.ConnectFailed;
        observationRecorder.Record(
            new MediaStreamKey(
                existingDeviceId,
                existingChannelId,
                source?.StreamType ?? request.Draft.StreamType),
            observation,
            utcNow(),
            errorCode.ToString(),
            "测试视频操作失败。");
    }

    private static CatalogOperationResult<T> Failure<T>(
        TestStreamErrorCode code)
    {
        var statusCode = code switch
        {
            TestStreamErrorCode.InvalidDraft => StatusCodes.Status400BadRequest,
            TestStreamErrorCode.IdentityConflict => StatusCodes.Status409Conflict,
            TestStreamErrorCode.SessionNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return new CatalogOperationResult<T>(
            false,
            default,
            statusCode,
            new VideoMonitor.Core.Catalog.CatalogErrorDto(
                code.ToString(),
                "测试视频操作失败。"));
    }
}
