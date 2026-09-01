using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed record TestSessionRegistration(
    TestSessionDto Dto,
    TestStreamProxyHandle Handle,
    DateTimeOffset CreatedAtUtc);

public sealed class TestSessionRegistry
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(2);

    private readonly object sync = new();
    private readonly Dictionary<Guid, TestSessionRegistration> sessions = [];
    private readonly Func<DateTimeOffset> utcNow;

    public TestSessionRegistry(Func<DateTimeOffset>? utcNow = null)
    {
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public TestSessionDto Add(
        TestStreamProxyHandle handle,
        Guid? deviceId,
        Guid? channelId,
        Uri playbackUrl)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(playbackUrl);

        var now = utcNow();
        var dto = new TestSessionDto(
            Guid.NewGuid(),
            deviceId,
            channelId,
            handle.App,
            handle.StreamId,
            playbackUrl,
            now.Add(SessionLifetime));
        var registration = new TestSessionRegistration(dto, handle, now);
        lock (sync)
        {
            sessions.Add(dto.SessionId, registration);
        }

        return dto;
    }

    public bool TryGet(
        Guid sessionId,
        out TestSessionRegistration? registration)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out registration))
            {
                registration = null;
                return false;
            }

            return true;
        }
    }

    public bool RemoveAfterSuccessfulCleanup(
        Guid sessionId,
        TestSessionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (sync)
        {
            return sessions.TryGetValue(sessionId, out var current)
                && ReferenceEquals(current, registration)
                && sessions.Remove(sessionId);
        }
    }

    public IReadOnlyList<TestSessionRegistration> GetExpired()
    {
        var now = utcNow();
        lock (sync)
        {
            var expired = sessions.Values
                .Where(item => item.Dto.ExpiresUtc <= now)
                .ToArray();
            return expired;
        }
    }

    public bool ContainsHandle(TestStreamProxyHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        lock (sync)
        {
            return sessions.Values.Any(item =>
                string.Equals(item.Handle.Vhost, handle.Vhost, StringComparison.Ordinal)
                && string.Equals(item.Handle.App, handle.App, StringComparison.Ordinal)
                && string.Equals(item.Handle.StreamId, handle.StreamId, StringComparison.Ordinal)
                && string.Equals(item.Handle.ProxyKey, handle.ProxyKey, StringComparison.Ordinal));
        }
    }

    public bool ContainsIdentity(string vhost, string app, string stream)
    {
        lock (sync)
        {
            return sessions.Values.Any(item =>
                string.Equals(item.Handle.Vhost, vhost, StringComparison.Ordinal)
                && string.Equals(item.Handle.App, app, StringComparison.Ordinal)
                && string.Equals(item.Handle.StreamId, stream, StringComparison.Ordinal));
        }
    }
}
