namespace VideoMonitor.Server.Playback;

public sealed record PlaybackTicket(
    string Value,
    string Vhost,
    string App,
    string Stream,
    DateTimeOffset ExpiresUtc);

public sealed record PlaybackTicketValidationResult(
    bool IsValid,
    string? FailureCode);

internal sealed record PlaybackTicketPayload(
    string Vhost,
    string App,
    string Stream,
    long ExpiresUnixMilliseconds,
    string Nonce);
