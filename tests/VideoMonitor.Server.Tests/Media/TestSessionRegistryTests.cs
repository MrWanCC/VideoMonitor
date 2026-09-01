using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestSessionRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddUsesTwoMinuteSessionLifetimeAndSafeDto()
    {
        var registry = new TestSessionRegistry(() => Now);
        var handle = Handle("test_0123456789abcdef0123456789abcdef", "proxy-1");

        var session = registry.Add(
            handle,
            Guid.Parse("91000000-0000-0000-0000-000000000001"),
            Guid.Parse("92000000-0000-0000-0000-000000000001"),
            new Uri("http://server/live"));

        Assert.Equal(Now.AddMinutes(2), session.ExpiresUtc);
        Assert.Equal(handle.App, session.App);
        Assert.Equal(handle.StreamId, session.StreamId);
        Assert.DoesNotContain("ProxyKey", JsonSerializer.Serialize(session));
        Assert.DoesNotContain("SourceUri", JsonSerializer.Serialize(session));
    }

    [Fact]
    public void TryGetUsesExactSessionIdAndRemovesOnlyAfterSuccessfulCleanup()
    {
        var registry = new TestSessionRegistry(() => Now);
        var session = registry.Add(
            Handle("test_0123456789abcdef0123456789abcdef", "proxy-1"),
            null,
            null,
            new Uri("http://server/live"));

        Assert.False(registry.TryGet(Guid.NewGuid(), out _));
        Assert.True(registry.TryGet(session.SessionId, out var taken));
        Assert.Equal(session.SessionId, taken!.Dto.SessionId);
        Assert.True(registry.RemoveAfterSuccessfulCleanup(session.SessionId, taken));
        Assert.False(registry.TryGet(session.SessionId, out _));
    }

    [Fact]
    public void GetExpiredReturnsOnlySessionsPastTtlUntilCleanupSucceeds()
    {
        var time = Now;
        var registry = new TestSessionRegistry(() => time);
        var session = registry.Add(
            Handle("test_0123456789abcdef0123456789abcdef", "proxy-1"),
            null,
            null,
            new Uri("http://server/live"));

        time = Now.AddMinutes(2).AddTicks(1);

        var expired = registry.GetExpired();

        var item = Assert.Single(expired);
        Assert.Equal(session.SessionId, item.Dto.SessionId);
        Assert.True(registry.TryGet(session.SessionId, out var retained));
        Assert.True(registry.RemoveAfterSuccessfulCleanup(session.SessionId, retained!));
        Assert.False(registry.TryGet(session.SessionId, out _));
    }

    private static TestStreamProxyHandle Handle(string stream, string proxyKey) => new(
        "configured-vhost",
        "videomonitor-test",
        stream,
        proxyKey,
        Now);
}
