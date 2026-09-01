using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public sealed class TestStreamOrphanReconcileContributor : IMediaReconcileContributor
{
    private readonly IZlmMediaGateway gateway;
    private readonly IMediaRuntimeSettingsProvider settingsProvider;
    private readonly TestSessionRegistry sessionRegistry;
    private readonly Func<DateTimeOffset> utcNow;

    public TestStreamOrphanReconcileContributor(
        IZlmMediaGateway gateway,
        IMediaRuntimeSettingsProvider settingsProvider,
        TestSessionRegistry sessionRegistry,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.settingsProvider = settingsProvider
            ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.sessionRegistry = sessionRegistry
            ?? throw new ArgumentNullException(nameof(sessionRegistry));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.Vhost)
            || string.IsNullOrWhiteSpace(settings.TestApp))
        {
            return;
        }

        foreach (var expired in sessionRegistry.GetExpired())
        {
            var cleanup = await gateway.DeleteStreamProxyAsync(
                    expired.Handle.ProxyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!cleanup.IsSuccess || cleanup.Data?.Flag != true)
            {
                throw new InvalidOperationException("测试视频会话清理失败。");
            }

            sessionRegistry.RemoveAfterSuccessfulCleanup(
                expired.Dto.SessionId,
                expired);
        }

        var response = await gateway.GetMediaListAsync(
                settings.Vhost,
                settings.TestApp,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            throw new InvalidOperationException("媒体服务当前不可用。");
        }

        foreach (var evidence in response.Data)
        {
            if (!IsSafeOrphan(evidence, settings.Vhost, settings.TestApp)
                || sessionRegistry.ContainsIdentity(
                    evidence.Vhost,
                    evidence.App,
                    evidence.Stream))
            {
                continue;
            }

            await gateway.CloseExactStreamAsync(
                    "rtsp",
                    settings.Vhost,
                    settings.TestApp,
                    evidence.Stream,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private bool IsSafeOrphan(
        ZlmMediaEvidence evidence,
        string configuredVhost,
        string configuredTestApp)
    {
        if (!string.Equals(evidence.Schema, "rtsp", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.Vhost, configuredVhost, StringComparison.Ordinal)
            || !string.Equals(evidence.App, configuredTestApp, StringComparison.Ordinal)
            || !IsTestStreamId(evidence.Stream)
            || !IsPullOrProxyCompatible(evidence)
            || !IsOlderThanSessionTtl(evidence))
        {
            return false;
        }

        return true;
    }

    private bool IsOlderThanSessionTtl(ZlmMediaEvidence evidence)
    {
        if (evidence.AliveSecond is { } aliveSecond)
        {
            return aliveSecond > (long)TestSessionRegistry.SessionLifetime.TotalSeconds;
        }

        if (evidence.CreateStamp is not { } createStamp || createStamp <= 0)
        {
            return false;
        }

        try
        {
            var created = createStamp >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(createStamp)
                : DateTimeOffset.FromUnixTimeSeconds(createStamp);
            return utcNow() - created > TestSessionRegistry.SessionLifetime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsPullOrProxyCompatible(ZlmMediaEvidence evidence)
    {
        if (evidence.OriginType is { } originType)
        {
            return originType is 4 or 7;
        }

        return !string.IsNullOrWhiteSpace(evidence.OriginTypeStr)
            && (evidence.OriginTypeStr.Contains("pull", StringComparison.OrdinalIgnoreCase)
                || evidence.OriginTypeStr.Contains("proxy", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestStreamId(string stream) =>
        stream.StartsWith("test_", StringComparison.Ordinal)
        && Guid.TryParseExact(stream[5..], "N", out _);
}
