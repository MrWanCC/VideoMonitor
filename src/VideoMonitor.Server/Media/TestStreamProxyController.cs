using System.Security.Cryptography;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public sealed class TestStreamProxyController : ITestStreamProxyController
{
    private readonly IZlmMediaGateway gateway;
    private readonly IMediaRuntimeSettingsProvider settingsProvider;
    private readonly int maxAttempts;
    private readonly int maxRegistrationPolls;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Func<string> streamIdFactory;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TestSessionRegistry sessionRegistry;
    private readonly TimeSpan cleanupTimeout;

    public TestStreamProxyController(
        IZlmMediaGateway gateway,
        IMediaRuntimeSettingsProvider settingsProvider,
        int maxAttempts = 5,
        int maxRegistrationPolls = 10,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<string>? streamIdFactory = null,
        Func<DateTimeOffset>? utcNow = null)
        : this(
            gateway,
            settingsProvider,
            new TestSessionRegistry(),
            maxAttempts,
            maxRegistrationPolls,
            delayAsync,
            streamIdFactory,
            utcNow)
    {
    }

    public TestStreamProxyController(
        IZlmMediaGateway gateway,
        IMediaRuntimeSettingsProvider settingsProvider,
        TestSessionRegistry sessionRegistry,
        int maxAttempts = 5,
        int maxRegistrationPolls = 10,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<string>? streamIdFactory = null,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? cleanupTimeout = null)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.settingsProvider = settingsProvider
            ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.sessionRegistry = sessionRegistry
            ?? throw new ArgumentNullException(nameof(sessionRegistry));
        this.maxAttempts = Math.Max(1, maxAttempts);
        this.maxRegistrationPolls = Math.Max(1, maxRegistrationPolls);
        this.delayAsync = delayAsync ?? Task.Delay;
        this.streamIdFactory = streamIdFactory ?? NewStreamId;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(5);
        if (this.cleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupTimeout));
        }
    }

    public async Task<TestStreamProxyHandle> StartAsync(
        ResolvedTestCameraSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var settings = await settingsProvider.GetAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.Vhost)
            || string.IsNullOrWhiteSpace(settings.TestApp))
        {
            throw new TestStreamOperationException(
                TestStreamErrorCode.MediaServerUnavailable,
                "媒体服务当前不可用。");
        }

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = streamIdFactory();
            if (!IsTestStreamId(stream))
            {
                throw new TestStreamOperationException(
                    TestStreamErrorCode.IdentityConflict,
                    "测试视频 identity 无效。");
            }

            var existing = await gateway.GetMediaListAsync(
                    settings.Vhost,
                    settings.TestApp,
                    stream,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureMediaListSuccess(existing);
            if (ContainsIdentity(existing.Data, settings.Vhost, settings.TestApp, stream))
            {
                if (attempt + 1 < maxAttempts)
                {
                    continue;
                }

                throw new TestStreamOperationException(
                    TestStreamErrorCode.IdentityConflict,
                    "测试视频 identity 冲突。");
            }

            var added = await gateway.AddStreamProxyAsync(
                    settings.Vhost,
                    settings.TestApp,
                    stream,
                    source.SourceUri,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!added.IsSuccess || added.Data is null
                || string.IsNullOrWhiteSpace(added.Data.Key))
            {
                throw new TestStreamOperationException(
                    ClassifyApiFailure(added.Code),
                    "媒体服务当前不可用。");
            }

            var proxyKey = added.Data.Key;
            var handle = new TestStreamProxyHandle(
                settings.Vhost,
                settings.TestApp,
                stream,
                proxyKey,
                utcNow());
            var ownershipTransferred = false;
            try
            {
                for (var poll = 0; poll < maxRegistrationPolls; poll++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var observed = await gateway.GetMediaListAsync(
                            settings.Vhost,
                            settings.TestApp,
                            stream,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (observed.IsSuccess
                        && ContainsExact(observed.Data, settings.Vhost, settings.TestApp, stream))
                    {
                        ownershipTransferred = true;
                        return handle;
                    }

                    if (poll + 1 < maxRegistrationPolls)
                    {
                        await delayAsync(TimeSpan.FromMilliseconds(100), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                throw new TestStreamOperationException(
                    TestStreamErrorCode.MediaRegistrationTimeout,
                    "媒体服务注册测试视频超时。");
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    var cleaned = await TryCleanupProxyAsync(proxyKey).ConfigureAwait(false);
                    if (!cleaned)
                    {
                        sessionRegistry.RegisterPendingCleanup(handle);
                    }
                }
            }
        }

        throw new TestStreamOperationException(
            TestStreamErrorCode.IdentityConflict,
            "测试视频 identity 冲突。");
    }

    public async Task StopAsync(
        TestStreamProxyHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (string.IsNullOrWhiteSpace(handle.ProxyKey))
        {
            throw new TestStreamOperationException(
                TestStreamErrorCode.InvalidDraft,
                "测试视频会话无效。");
        }

        await CleanupProxyAsync(handle.ProxyKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupProxyAsync(
        string proxyKey,
        CancellationToken cancellationToken)
    {
        var result = await gateway.DeleteStreamProxyAsync(proxyKey, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data?.Flag != true)
        {
            throw new TestStreamOperationException(
                ClassifyApiFailure(result.Code),
                "测试视频清理失败。");
        }
    }

    private async Task<bool> TryCleanupProxyAsync(string proxyKey)
    {
        using var cleanupCancellation = new CancellationTokenSource(cleanupTimeout);
        try
        {
            await CleanupProxyAsync(proxyKey, cleanupCancellation.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureMediaListSuccess(
        ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>> response)
    {
        if (!response.IsSuccess)
        {
            throw new TestStreamOperationException(
                ClassifyApiFailure(response.Code),
                "媒体服务当前不可用。");
        }
    }

    private static bool ContainsExact(
        IReadOnlyList<ZlmMediaEvidence>? evidence,
        string vhost,
        string app,
        string stream) =>
        evidence?.Any(item =>
            string.Equals(item.Schema, "rtsp", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Vhost, vhost, StringComparison.Ordinal)
            && string.Equals(item.App, app, StringComparison.Ordinal)
            && string.Equals(item.Stream, stream, StringComparison.Ordinal)) == true;

    private static bool ContainsIdentity(
        IReadOnlyList<ZlmMediaEvidence>? evidence,
        string vhost,
        string app,
        string stream) =>
        evidence?.Any(item =>
            string.Equals(item.Vhost, vhost, StringComparison.Ordinal)
            && string.Equals(item.App, app, StringComparison.Ordinal)
            && string.Equals(item.Stream, stream, StringComparison.Ordinal)) == true;

    private static TestStreamErrorCode ClassifyApiFailure(int code) =>
        code is -100 or 401 or 403 or -401 or -403
            ? TestStreamErrorCode.AuthFailed
            : TestStreamErrorCode.MediaServerUnavailable;

    private static bool IsTestStreamId(string value) =>
        value.StartsWith("test_", StringComparison.Ordinal)
        && Guid.TryParseExact(value[5..], "N", out _);

    private static string NewStreamId() =>
        "test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
