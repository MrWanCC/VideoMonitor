using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqliteMediaRuntimeSettingsProviderTests
{
    [Fact]
    public async Task GetDecryptsSavedCredentialOnlyAtRuntimeBoundary()
    {
        await using var fixture = await MediaSettingsFixture.CreateAsync();
        var protector = new RecordingSecretProtector();
        var repository = fixture.CreateRepository(protector);
        await repository.UpdateAsync(new UpdateMediaSettingsRequest(
            "http://127.0.0.1:8080",
            "rtsp://media.example.test:554",
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            "Runtime-Only-Secret",
            45,
            1));
        protector.Reset();
        var provider = new SqliteMediaRuntimeSettingsProvider(repository, protector);

        var runtime = await provider.GetAsync();

        Assert.Equal("Runtime-Only-Secret", runtime.ZlmSecret);
        Assert.Equal("http://127.0.0.1:8080", runtime.ZlmApiBaseUrl);
        Assert.Equal("rtsp://media.example.test:554", runtime.PlaybackBaseUrl);
        Assert.Equal(45, runtime.NoReaderGraceSeconds);
        Assert.Equal(2, runtime.Revision);
        Assert.Equal(1, protector.UnprotectCalls);
        Assert.Equal("media-settings:zlm-secret", protector.LastUnprotectPurpose);
    }

    [Fact]
    public async Task EmptyInitialRowProjectsUnconfiguredWithoutDecrypt()
    {
        await using var fixture = await MediaSettingsFixture.CreateAsync();
        var protector = new RecordingSecretProtector();
        var repository = fixture.CreateRepository(protector);
        var provider = new SqliteMediaRuntimeSettingsProvider(repository, protector);

        var runtime = await provider.GetAsync();

        Assert.Equal(string.Empty, runtime.ZlmApiBaseUrl);
        Assert.Equal(string.Empty, runtime.PlaybackBaseUrl);
        Assert.Equal(string.Empty, runtime.ZlmSecret);
        Assert.Equal("videomonitor", runtime.FormalApp);
        Assert.Equal("videomonitor-test", runtime.TestApp);
        Assert.Equal(30, runtime.NoReaderGraceSeconds);
        Assert.Equal(1, runtime.Revision);
        Assert.Equal(0, protector.UnprotectCalls);
    }
}
