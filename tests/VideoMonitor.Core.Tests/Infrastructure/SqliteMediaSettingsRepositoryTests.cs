using Microsoft.Data.Sqlite;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteMediaSettingsRepositoryTests
{
    [Fact]
    public async Task DefaultsAreCreatedWithExpectedNamespaceAndRevision()
    {
        await using var fixture = await MediaSettingsFixture.CreateAsync();
        var repository = fixture.CreateRepository();

        var dto = await repository.GetAsync();

        Assert.Equal("videomonitor", dto.FormalApp);
        Assert.Equal("videomonitor-test", dto.TestApp);
        Assert.Equal(30, dto.NoReaderGraceSeconds);
        Assert.Equal(1, dto.Revision);
        Assert.False(dto.HasSecret);
    }

    [Fact]
    public async Task UpdateProtectsSecretAndGetNeverReturnsCiphertext()
    {
        await using var fixture = await MediaSettingsFixture.CreateAsync();
        var protector = new RecordingSecretProtector();
        var repository = fixture.CreateRepository(protector);

        var result = await repository.UpdateAsync(CreateUpdate("Plain-ZLM-Secret"));
        var dto = await repository.GetAsync();
        var storedCiphertext = await fixture.ReadCiphertextAsync();

        Assert.Equal(CatalogRepositoryStatus.Success, result.Status);
        Assert.True(result.Value!.HasSecret);
        Assert.True(dto.HasSecret);
        Assert.Equal(2, dto.Revision);
        Assert.StartsWith("test-protected:", storedCiphertext, StringComparison.Ordinal);
        Assert.DoesNotContain("Plain-ZLM-Secret", storedCiphertext, StringComparison.Ordinal);
        Assert.Equal("media-settings:zlm-secret", protector.LastProtectPurpose);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task NullOrBlankSecretPreservesExistingProtectedValue()
    {
        await using var fixture = await MediaSettingsFixture.CreateAsync();
        var protector = new RecordingSecretProtector();
        var repository = fixture.CreateRepository(protector);
        await repository.UpdateAsync(CreateUpdate("Initial-Secret"));
        var originalCiphertext = await fixture.ReadCiphertextAsync();

        var nullSecret = await repository.UpdateAsync(new UpdateMediaSettingsRequest(
            "http://127.0.0.1:8081",
            "rtsp://media.example.test:554",
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            null,
            35,
            2));
        var afterNullSecret = await fixture.ReadCiphertextAsync();
        var blankSecret = await repository.UpdateAsync(new UpdateMediaSettingsRequest(
            "http://127.0.0.1:8082",
            "rtsp://media.example.test:8554",
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            "   ",
            40,
            3));

        Assert.Equal(CatalogRepositoryStatus.Success, nullSecret.Status);
        Assert.Equal(CatalogRepositoryStatus.Success, blankSecret.Status);
        Assert.Equal(originalCiphertext, afterNullSecret);
        Assert.Equal(originalCiphertext, await fixture.ReadCiphertextAsync());
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
        var dto = await repository.GetAsync();
        Assert.True(dto.HasSecret);
        Assert.Equal("http://127.0.0.1:8082", dto.ZlmApiBaseUrl);
        Assert.Equal("rtsp://media.example.test:8554", dto.PlaybackBaseUrl);
        Assert.Equal(4, dto.Revision);
    }

    [Fact]
    public async Task StaleRevisionDoesNotChangeSettings()
    {
        await using var fixture = await MediaSettingsFixture.CreateAsync();
        var protector = new RecordingSecretProtector();
        var repository = fixture.CreateRepository(protector);
        await repository.UpdateAsync(CreateUpdate("Initial-Secret"));
        var before = await repository.ReadStorageAsync();

        var stale = await repository.UpdateAsync(new UpdateMediaSettingsRequest(
            "http://must-not-persist",
            "rtsp://must-not-persist",
            "changed-vhost",
            "changed-formal",
            "changed-test",
            "Changed-Secret",
            99,
            1));

        Assert.Equal(CatalogRepositoryStatus.RevisionConflict, stale.Status);
        Assert.Equal(2, stale.CurrentRevision);
        Assert.Equal(before, await repository.ReadStorageAsync());
        Assert.Equal(2, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    private static UpdateMediaSettingsRequest CreateUpdate(
        string? secret,
        long expectedRevision = 1) =>
        new(
            "http://127.0.0.1:8080",
            "rtsp://media.example.test:554",
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            secret,
            30,
            expectedRevision);
}

internal sealed class MediaSettingsFixture : IAsyncDisposable
{
    private MediaSettingsFixture(string root)
    {
        Provider = new DefaultAppPathProvider(new ServerStorageOptions { RootPath = root });
        new ServerStorageLayout(Provider).EnsureCreated();
        Factory = new SqliteConnectionFactory(Provider);
        Initializer = new SqliteDatabaseInitializer(Factory);
    }

    public DefaultAppPathProvider Provider { get; }
    public SqliteConnectionFactory Factory { get; }
    public SqliteDatabaseInitializer Initializer { get; }

    public static async Task<MediaSettingsFixture> CreateAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "VideoMonitorMediaSettingsTests",
            Guid.NewGuid().ToString("N"));
        var fixture = new MediaSettingsFixture(root);
        await fixture.Initializer.InitializeAsync();
        return fixture;
    }

    public SqliteMediaSettingsRepository CreateRepository(
        ISecretProtector? protector = null) =>
        new(Factory, protector ?? new RecordingSecretProtector());

    public async Task<string> ReadCiphertextAsync()
    {
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT zlm_secret_ciphertext FROM media_settings WHERE id = 1;";
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.Yield();
        try
        {
            if (Directory.Exists(Provider.RootDirectory))
            {
                Directory.Delete(Provider.RootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class RecordingSecretProtector : ISecretProtector
{
    public int ProtectCalls { get; private set; }
    public int UnprotectCalls { get; private set; }
    public string? LastProtectPurpose { get; private set; }
    public string? LastUnprotectPurpose { get; private set; }

    public Task<string> ProtectAsync(
        string plaintext,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        ProtectCalls++;
        LastProtectPurpose = purpose;
        return Task.FromResult(
            $"test-protected:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext))}");
    }

    public Task<string> UnprotectAsync(
        string protectedValue,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        UnprotectCalls++;
        LastUnprotectPurpose = purpose;
        if (!protectedValue.StartsWith("test-protected:", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid protected test value.");
        }

        var encoded = protectedValue["test-protected:".Length..];
        return Task.FromResult(System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(encoded)));
    }

    public void Reset()
    {
        ProtectCalls = 0;
        UnprotectCalls = 0;
        LastProtectPurpose = null;
        LastUnprotectPurpose = null;
    }
}
