using System.Security.Cryptography;
using System.Text.Json;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Playback;

public interface IPlaybackTicketValidator
{
    Task<PlaybackTicketValidationResult> ValidateAsync(
        string? encodedTicket,
        string actualVhost,
        string actualApp,
        string actualStream,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed class PlaybackTicketValidator :
    IPlaybackTicketValidator
{
    private const string InvalidCode = "PLAYBACK_TICKET_INVALID";
    private readonly IPlaybackSigningKeyProvider signingKeyProvider;

    public PlaybackTicketValidator(IPlaybackSigningKeyProvider signingKeyProvider)
    {
        this.signingKeyProvider = signingKeyProvider
            ?? throw new ArgumentNullException(nameof(signingKeyProvider));
    }

    public async Task<PlaybackTicketValidationResult> ValidateAsync(
        string? encodedTicket,
        string actualVhost,
        string actualApp,
        string actualStream,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(encodedTicket))
        {
            return Invalid(InvalidCode);
        }

        byte[] key;
        try
        {
            key = await signingKeyProvider
                .GetOrCreateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Invalid(InvalidCode);
        }

        try
        {
            return ValidateWithKey(
                encodedTicket,
                actualVhost,
                actualApp,
                actualStream,
                now,
                key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static PlaybackTicketValidationResult ValidateWithKey(
        string? encodedTicket,
        string actualVhost,
        string actualApp,
        string actualStream,
        DateTimeOffset now,
        ReadOnlySpan<byte> signingKey)
    {
        if (string.IsNullOrWhiteSpace(encodedTicket)
            || string.IsNullOrWhiteSpace(actualVhost)
            || string.IsNullOrWhiteSpace(actualApp)
            || string.IsNullOrWhiteSpace(actualStream))
        {
            return Invalid(InvalidCode);
        }

        var parts = encodedTicket.Split('.', StringSplitOptions.None);
        if (parts.Length != 2
            || !PlaybackTicketCodec.TryDecode(parts[0], out var payloadBytes)
            || !PlaybackTicketCodec.TryDecode(parts[1], out var providedSignature)
            || providedSignature.Length != SHA256.HashSizeInBytes)
        {
            return Invalid(InvalidCode);
        }

        var expectedSignature = HMACSHA256.HashData(signingKey, payloadBytes);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    providedSignature,
                    expectedSignature))
            {
                return Invalid(InvalidCode);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedSignature);
            CryptographicOperations.ZeroMemory(providedSignature);
        }

        PlaybackTicketPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PlaybackTicketPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return Invalid(InvalidCode);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Vhost)
            || string.IsNullOrWhiteSpace(payload.App)
            || string.IsNullOrWhiteSpace(payload.Stream)
            || string.IsNullOrWhiteSpace(payload.Nonce))
        {
            return Invalid(InvalidCode);
        }

        if (!string.Equals(payload.Vhost, actualVhost, StringComparison.Ordinal)
            || !string.Equals(payload.App, actualApp, StringComparison.Ordinal)
            || !string.Equals(payload.Stream, actualStream, StringComparison.Ordinal))
        {
            return Invalid(InvalidCode);
        }

        DateTimeOffset expiresUtc;
        try
        {
            expiresUtc = DateTimeOffset.FromUnixTimeMilliseconds(
                payload.ExpiresUnixMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Invalid(InvalidCode);
        }

        return now >= expiresUtc
            ? Invalid(InvalidCode)
            : new PlaybackTicketValidationResult(true, null);
    }

    private static PlaybackTicketValidationResult Invalid(string code) =>
        new(false, code);
}
