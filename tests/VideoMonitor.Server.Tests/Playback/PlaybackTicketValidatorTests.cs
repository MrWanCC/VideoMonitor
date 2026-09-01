using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Playback;

public sealed class PlaybackTicketValidatorTests
{
    [Fact]
    public async Task RejectsMissingMalformedBadSignatureExpiredAndMismatchedClaims()
    {
        var now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var issuer = new PlaybackTicketIssuer(
            new FixedSigningKeyProvider(key),
            () => now);
        var ticket = await issuer.IssueAsync(
            new PlaybackMediaIdentity("vhost", "app", "stream"));
        var expired = await new PlaybackTicketIssuer(
                new FixedSigningKeyProvider(key),
                () => now.AddSeconds(-61))
            .IssueAsync(new PlaybackMediaIdentity("vhost", "app", "stream"));
        var validator = new PlaybackTicketValidator(
            new FixedSigningKeyProvider(key));

        var results = new[]
        {
            validator.Validate(null, "vhost", "app", "stream", now),
            validator.Validate("not-a-ticket", "vhost", "app", "stream", now),
            validator.Validate(
                ReplaceSignature(ticket.Value),
                "vhost",
                "app",
                "stream",
                now),
            validator.Validate(expired.Value, "vhost", "app", "stream", now),
            validator.Validate(ticket.Value, "other-vhost", "app", "stream", now),
            validator.Validate(ticket.Value, "vhost", "other-app", "stream", now),
            validator.Validate(ticket.Value, "vhost", "app", "other-stream", now)
        };

        Assert.All(results, result => Assert.False(result.IsValid));
        Assert.All(results, result =>
        {
            Assert.False(string.IsNullOrWhiteSpace(result.FailureCode));
            Assert.DoesNotContain(ticket.Value, result.FailureCode, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task FormalTicketCannotAuthorizeTest()
    {
        var ticket = await CreateTicketAsync("videomonitor", "formal-stream");
        var result = new PlaybackTicketValidator(
            new FixedSigningKeyProvider(GetKey())).Validate(
            ticket.Value,
            "__defaultVhost__",
            "videomonitor-test",
            "formal-stream",
            DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task TestTicketCannotAuthorizeFormal()
    {
        var ticket = await CreateTicketAsync("videomonitor-test", "test_stream");
        var result = new PlaybackTicketValidator(
            new FixedSigningKeyProvider(GetKey())).Validate(
            ticket.Value,
            "__defaultVhost__",
            "videomonitor",
            "test_stream",
            DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
    }

    private static async Task<PlaybackTicket> CreateTicketAsync(
        string app,
        string stream)
    {
        var now = DateTimeOffset.UtcNow;
        return await new PlaybackTicketIssuer(
                new FixedSigningKeyProvider(GetKey()),
                () => now)
            .IssueAsync(new PlaybackMediaIdentity("__defaultVhost__", app, stream));
    }

    private static string ReplaceSignature(string value)
    {
        var separator = value.LastIndexOf('.');
        var replacement = value[separator + 1] == 'A' ? 'B' : 'A';
        return value[..(separator + 1)] + replacement + value[(separator + 2)..];
    }

    private static byte[] GetKey() =>
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    private sealed class FixedSigningKeyProvider : IPlaybackSigningKeyProvider
    {
        private readonly byte[] key;

        public FixedSigningKeyProvider(byte[] key) => this.key = key;

        public Task<byte[]> GetOrCreateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult((byte[])key.Clone());
    }
}
