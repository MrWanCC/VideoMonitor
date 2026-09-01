using System.Net;
using System.Text;
using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaSettingsProbeTests
{
    [Fact]
    public async Task ValidCandidateCallsGetServerConfigWithoutPersisting()
    {
        var repository = new FakeMediaSettingsRepository();
        var protector = new FakeSecretProtector();
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"code\":0,\"msg\":\"ok\",\"data\":{}}"
        };
        var probe = new MediaSettingsProbe(repository, protector, () => handler);
        var request = CreateRequest("Candidate-Secret");

        var result = await probe.TestAsync(request);

        Assert.True(result.IsReachable);
        Assert.Null(result.FailureCode);
        var zlmRequest = Assert.Single(handler.Requests);
        Assert.Contains("/index/api/getServerConfig", zlmRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("addStreamProxy", zlmRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal(0, repository.UpdateCalls);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Candidate-Secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(request.ZlmApiBaseUrl, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSecretFailsSafely()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"code\":401,\"msg\":\"bad secret\",\"data\":{}}"
        };
        var probe = new MediaSettingsProbe(
            new FakeMediaSettingsRepository(),
            new FakeSecretProtector(),
            () => handler);

        var result = await probe.TestAsync(CreateRequest("Wrong-Secret"));

        Assert.False(result.IsReachable);
        Assert.Equal("AuthFailed", result.FailureCode);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Wrong-Secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("bad secret", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankCandidateSecretUsesSavedSecretWhenPresent()
    {
        var repository = new FakeMediaSettingsRepository
        {
            Storage = new MediaSettingsStorageRecord(
                "http://stored-zlm",
                "rtsp://stored-playback",
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                "test-protected:Saved-Secret",
                30,
                2)
        };
        var protector = new FakeSecretProtector();
        var handler = new RecordingHandler
        {
            ResponseBody = "{\"code\":0,\"msg\":\"ok\",\"data\":{}}"
        };
        var probe = new MediaSettingsProbe(repository, protector, () => handler);

        var result = await probe.TestAsync(CreateRequest("   "));

        Assert.True(result.IsReachable);
        Assert.Equal(1, protector.UnprotectCalls);
        Assert.Equal("media-settings:zlm-secret", protector.LastPurpose);
        Assert.Contains("secret=Saved-Secret", Assert.Single(handler.Requests).PathAndQuery);
    }

    [Fact]
    public async Task BlankCandidateSecretWithoutSavedSecretFailsSafely()
    {
        var repository = new FakeMediaSettingsRepository();
        var protector = new FakeSecretProtector();
        var handler = new RecordingHandler();
        var probe = new MediaSettingsProbe(repository, protector, () => handler);

        var result = await probe.TestAsync(CreateRequest(null));

        Assert.False(result.IsReachable);
        Assert.Equal("ZLM_SECRET_REQUIRED", result.FailureCode);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task InvalidPlaybackBaseUrlFailsBeforeZlmCall()
    {
        var handler = new RecordingHandler();
        var probe = new MediaSettingsProbe(
            new FakeMediaSettingsRepository(),
            new FakeSecretProtector(),
            () => handler);

        var result = await probe.TestAsync(CreateRequest(
            "Candidate-Secret",
            playbackBaseUrl: "rtsp://user:pass@media.example.test:554"));

        Assert.False(result.IsReachable);
        Assert.Equal("INVALID_PLAYBACK_BASE_URL", result.FailureCode);
        Assert.Empty(handler.Requests);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("user:pass", serialized, StringComparison.Ordinal);
    }

    private static TestMediaSettingsRequest CreateRequest(
        string? secret,
        string playbackBaseUrl = "rtsp://media.example.test:554") =>
        new(
            "http://127.0.0.1:8080",
            playbackBaseUrl,
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            secret,
            30);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string ResponseBody { get; init; } = "{\"code\":0,\"msg\":\"ok\",\"data\":{}}";
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}

internal sealed class FakeMediaSettingsRepository : IMediaSettingsRepository
{
    public int UpdateCalls { get; private set; }

    public MediaSettingsStorageRecord Storage { get; set; } = new(
        string.Empty,
        string.Empty,
        "__defaultVhost__",
        "videomonitor",
        "videomonitor-test",
        string.Empty,
        30,
        1);

    public Task<MediaSettingsDto> GetAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MediaSettingsDto(
            Storage.ZlmApiBaseUrl,
            Storage.PlaybackBaseUrl,
            Storage.Vhost,
            Storage.FormalApp,
            Storage.TestApp,
            !string.IsNullOrEmpty(Storage.ZlmSecretCiphertext),
            Storage.NoReaderGraceSeconds,
            Storage.Revision));

    public Task<MediaSettingsStorageRecord> ReadStorageAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Storage);

    public Task<CatalogRepositoryResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;
        throw new InvalidOperationException("Probe must not persist settings.");
    }
}

internal sealed class FakeSecretProtector : ISecretProtector
{
    public int UnprotectCalls { get; private set; }
    public string? LastPurpose { get; private set; }

    public Task<string> ProtectAsync(
        string plaintext,
        string purpose,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"test-protected:{plaintext}");

    public Task<string> UnprotectAsync(
        string protectedValue,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        UnprotectCalls++;
        LastPurpose = purpose;
        return Task.FromResult(protectedValue["test-protected:".Length..]);
    }
}
