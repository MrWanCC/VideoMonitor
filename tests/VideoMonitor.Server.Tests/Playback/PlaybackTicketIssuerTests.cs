using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Playback;

public sealed class PlaybackTicketIssuerTests
{
    [Fact]
    public async Task FormalIdentityCanIssue()
    {
        var issuer = CreateIssuer();

        var ticket = await issuer.IssueAsync(
            new PlaybackMediaIdentity("__defaultVhost__", "videomonitor", "vm_formal"));

        Assert.False(string.IsNullOrWhiteSpace(ticket.Value));
        Assert.Equal("__defaultVhost__", ticket.Vhost);
        Assert.Equal("videomonitor", ticket.App);
        Assert.Equal("vm_formal", ticket.Stream);
    }

    [Fact]
    public async Task TestIdentityCanIssue()
    {
        var issuer = CreateIssuer();

        var ticket = await issuer.IssueAsync(
            new PlaybackMediaIdentity("__defaultVhost__", "videomonitor-test", "test_" + Guid.NewGuid().ToString("N")));

        Assert.False(string.IsNullOrWhiteSpace(ticket.Value));
        Assert.Equal("videomonitor-test", ticket.App);
    }

    [Fact]
    public async Task IssueBindsAllClaimsAndUsesSixtySecondWindow()
    {
        var now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var issuer = new PlaybackTicketIssuer(
            new FixedSigningKeyProvider(),
            () => now);

        var first = await issuer.IssueAsync(
            new PlaybackMediaIdentity("vhost-a", "app-a", "stream-a"));
        var second = await issuer.IssueAsync(
            new PlaybackMediaIdentity("vhost-b", "app-b", "stream-b"));

        Assert.Equal(now.AddSeconds(60), first.ExpiresUtc);
        Assert.Equal(now.AddSeconds(60), second.ExpiresUtc);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal("vhost-a", first.Vhost);
        Assert.Equal("app-a", first.App);
        Assert.Equal("stream-a", first.Stream);
    }

    private static PlaybackTicketIssuer CreateIssuer() =>
        new(new FixedSigningKeyProvider());

    private sealed class FixedSigningKeyProvider : IPlaybackSigningKeyProvider
    {
        public Task<byte[]> GetOrCreateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
    }
}
