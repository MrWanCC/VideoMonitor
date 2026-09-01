using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaSettingsApiTests
{
    [Fact]
    public async Task GetNeverReturnsSecretOrCiphertext()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var secret = "Never-Return-ZLM-Secret";
        var put = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate(secret, 1));
        put.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/media/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<MediaSettingsDto>();
        Assert.NotNull(dto);
        Assert.True(dto.HasSecret);
        await AssertSafeBodyAsync(response, secret);
    }

    [Fact]
    public async Task PutUsesExpectedRevisionAndReturns409OnConflict()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var first = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate("Initial-Secret", 1));
        first.EnsureSuccessStatusCode();

        var stale = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate("Stale-Secret", 1));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var error = await stale.Content.ReadFromJsonAsync<CatalogErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("MEDIA_SETTINGS_REVISION_CONFLICT", error.Code);
        Assert.Equal(2, error.CurrentRevision);
        await AssertSafeBodyAsync(stale, "Stale-Secret");
    }

    [Fact]
    public async Task PostTestDoesNotChangeRevisionOrCiphertext()
    {
        var probe = new FakeMediaSettingsProbe();
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediaSettingsProbe>();
                services.AddSingleton<IMediaSettingsProbe>(probe);
            }));
        using var client = factory.CreateClient();
        var put = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate("Saved-Secret", 1));
        put.EnsureSuccessStatusCode();
        var before = await ReadRawSettingsAsync(baseFactory.DatabasePath);

        var response = await client.PostAsJsonAsync(
            "/api/v1/media/settings/test",
            CreateTest("Candidate-Only-Secret"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaSettingsTestResult>();
        Assert.NotNull(result);
        Assert.True(result.IsReachable);
        Assert.Equal(before, await ReadRawSettingsAsync(baseFactory.DatabasePath));
        Assert.Equal(1, probe.CallCount);
        await AssertSafeBodyAsync(response, "Candidate-Only-Secret");
    }

    [Fact]
    public async Task BlankEditSecretPreservesExistingSecret()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var put = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate("Saved-Secret", 1));
        put.EnsureSuccessStatusCode();
        var before = await ReadRawSettingsAsync(factory.DatabasePath);

        var blank = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate("   ", 2) with
            {
                ZlmApiBaseUrl = "http://127.0.0.1:8081"
            });

        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        var after = await ReadRawSettingsAsync(factory.DatabasePath);
        Assert.Equal(before.Ciphertext, after.Ciphertext);
        Assert.Equal(3, after.Revision);
        Assert.Equal("http://127.0.0.1:8081", after.ZlmApiBaseUrl);
    }

    [Theory]
    [InlineData("not-an-absolute-url", "rtsp://media.example.test:554")]
    [InlineData("http://127.0.0.1:8080", "rtsp://user:pass@media.example.test:554")]
    public async Task PutRejectsInvalidMediaUrlsWithoutPersisting(
        string zlmApiBaseUrl,
        string playbackBaseUrl)
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();
        var before = await ReadRawSettingsAsync(factory.DatabasePath);

        var response = await client.PutAsJsonAsync(
            "/api/v1/media/settings",
            CreateUpdate(null, 1) with
            {
                ZlmApiBaseUrl = zlmApiBaseUrl,
                PlaybackBaseUrl = playbackBaseUrl
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("MEDIA_SETTINGS_VALIDATION_FAILED", error.Code);
        Assert.Equal(before, await ReadRawSettingsAsync(factory.DatabasePath));
    }

    private static UpdateMediaSettingsRequest CreateUpdate(
        string? secret,
        long expectedRevision) =>
        new(
            "http://127.0.0.1:8080",
            "rtsp://media.example.test:554",
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            secret,
            30,
            expectedRevision);

    private static TestMediaSettingsRequest CreateTest(string? secret) =>
        new(
            "http://127.0.0.1:8080",
            "rtsp://media.example.test:554",
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            secret,
            30);

    private static async Task<RawSettings> ReadRawSettingsAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT zlm_api_base_url, zlm_secret_ciphertext, revision
            FROM media_settings
            WHERE id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RawSettings(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2));
    }

    private static async Task AssertSafeBodyAsync(
        HttpResponseMessage response,
        string secret)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("zlmSecretCiphertext", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"zlmSecret\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProblemDetails", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("traceId", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RawSettings(
        string ZlmApiBaseUrl,
        string Ciphertext,
        long Revision);

    private sealed class FakeMediaSettingsProbe : IMediaSettingsProbe
    {
        public int CallCount { get; private set; }

        public Task<MediaSettingsTestResult> TestAsync(
            TestMediaSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new MediaSettingsTestResult(true, null));
        }
    }
}
