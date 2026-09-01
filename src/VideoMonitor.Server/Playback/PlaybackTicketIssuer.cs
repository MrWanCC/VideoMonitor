using System.Security.Cryptography;
using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Playback;

public interface IPlaybackTicketIssuer
{
    Task<PlaybackTicket> IssueAsync(
        PlaybackMediaIdentity media,
        CancellationToken cancellationToken = default);
}

public sealed class PlaybackTicketIssuer : IPlaybackTicketIssuer
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);
    private readonly IPlaybackSigningKeyProvider signingKeyProvider;
    private readonly Func<DateTimeOffset> utcNow;

    public PlaybackTicketIssuer(
        IPlaybackSigningKeyProvider signingKeyProvider,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.signingKeyProvider = signingKeyProvider
            ?? throw new ArgumentNullException(nameof(signingKeyProvider));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<PlaybackTicket> IssueAsync(
        PlaybackMediaIdentity media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (string.IsNullOrWhiteSpace(media.Vhost)
            || string.IsNullOrWhiteSpace(media.App)
            || string.IsNullOrWhiteSpace(media.Stream))
        {
            throw new ArgumentException(
                "媒体 identity 不能为空。",
                nameof(media));
        }

        var now = utcNow();
        var expiresUtc = now.Add(TicketLifetime);
        var payload = new PlaybackTicketPayload(
            media.Vhost,
            media.App,
            media.Stream,
            expiresUtc.ToUnixTimeMilliseconds(),
            PlaybackTicketCodec.Encode(RandomNumberGenerator.GetBytes(16)));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadValue = PlaybackTicketCodec.Encode(payloadBytes);
        var signingKey = await signingKeyProvider
            .GetOrCreateAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var signature = HMACSHA256.HashData(signingKey, payloadBytes);
            var value = payloadValue + "." + PlaybackTicketCodec.Encode(signature);
            CryptographicOperations.ZeroMemory(signature);
            return new PlaybackTicket(
                value,
                media.Vhost,
                media.App,
                media.Stream,
                expiresUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingKey);
        }
    }
}

internal static class PlaybackTicketCodec
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_')))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
