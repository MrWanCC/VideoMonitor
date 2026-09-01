using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public static class MediaHookEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapMediaHookEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/media/hooks/on-stream-changed",
            (HttpContext context, IZlmHookTrustPolicy trust, MediaEventProcessor processor) =>
                HandleAsync(context, "on-stream-changed", trust, processor));
        endpoints.MapPost(
            "/api/v1/media/hooks/on-stream-none-reader",
            (HttpContext context, IZlmHookTrustPolicy trust, MediaEventProcessor processor) =>
                HandleAsync(context, "on-stream-none-reader", trust, processor));
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        string routeKind,
        IZlmHookTrustPolicy trust,
        MediaEventProcessor processor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(processor);

        if (!trust.IsTrusted(context.Connection.RemoteIpAddress))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        HookPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<HookPayload>(
                    context.Request.Body,
                    SerializerOptions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Schema)
            || string.IsNullOrWhiteSpace(payload.Vhost)
            || string.IsNullOrWhiteSpace(payload.App)
            || string.IsNullOrWhiteSpace(payload.Stream))
        {
            return Results.BadRequest();
        }

        var key = MediaStreamIdGenerator.TryParseFormal(payload.Stream, out var parsedKey)
            ? parsedKey
            : (MediaStreamKey?)null;
        var kind = string.Equals(routeKind, "on-stream-none-reader", StringComparison.Ordinal)
            ? MediaHookKind.NoneReader
            : MediaHookKind.StreamChanged;
        var accepted = processor.TryEnqueue(new MediaHookEvent(
            kind,
            payload.Schema,
            payload.Vhost,
            payload.App,
            payload.Stream,
            key));
        return accepted
            ? Results.Accepted()
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record HookPayload(
        string? Schema,
        string? Vhost,
        string? App,
        string? Stream);
}
