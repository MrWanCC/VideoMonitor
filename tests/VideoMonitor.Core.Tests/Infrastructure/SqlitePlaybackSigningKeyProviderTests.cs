using Microsoft.Data.Sqlite;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class SqlitePlaybackSigningKeyProviderTests
{
    [Fact]
    public async Task FirstUseCreatesDurableProtectedKey()
    {
        using var context = PlaybackSigningKeyTestContext.Create();
        await context.CreateInitializer().InitializeAsync();
        var protector = new RecordingSecretProtector();
        var provider = CreateProvider(context, protector);

        var key = await provider.GetOrCreateAsync();
        var storedValue = await ReadStoredValueAsync(context);

        Assert.Equal(32, key.Length);
        Assert.StartsWith("test-protected:", storedValue, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Convert.ToBase64String(key),
            storedValue,
            StringComparison.Ordinal);
        Assert.Equal(1, protector.ProtectCalls);
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public async Task ConcurrentGetOrCreateReturnsOneKey()
    {
        using var context = PlaybackSigningKeyTestContext.Create();
        await context.CreateInitializer().InitializeAsync();
        var protector = new RecordingSecretProtector();
        var provider = CreateProvider(context, protector);

        var keys = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => provider.GetOrCreateAsync()));

        Assert.All(keys, key => Assert.Equal(32, key.Length));
        Assert.All(keys.Skip(1), key => Assert.Equal(keys[0], key));
        Assert.Equal(1, protector.ProtectCalls);
    }

    [Fact]
    public async Task ReloadReturnsSameKey()
    {
        using var context = PlaybackSigningKeyTestContext.Create();
        await context.CreateInitializer().InitializeAsync();
        var firstProtector = new RecordingSecretProtector();
        var firstProvider = CreateProvider(context, firstProtector);
        var firstKey = await firstProvider.GetOrCreateAsync();

        var secondProtector = new RecordingSecretProtector();
        var secondProvider = CreateProvider(context, secondProtector);
        var reloadedKey = await secondProvider.GetOrCreateAsync();

        Assert.Equal(firstKey, reloadedKey);
        Assert.Equal(0, secondProtector.ProtectCalls);
        Assert.Equal(1, secondProtector.UnprotectCalls);
    }

    private static SqlitePlaybackSigningKeyProvider CreateProvider(
        PlaybackSigningKeyTestContext context,
        ISecretProtector protector) =>
        new(new SqliteConnectionFactory(context.Provider), protector);

    private static async Task<string> ReadStoredValueAsync(
        PlaybackSigningKeyTestContext context)
    {
        await using var connection = new SqliteConnectionFactory(context.Provider)
            .CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM server_settings WHERE key = 'playback.signing-key.v1';";
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private sealed class PlaybackSigningKeyTestContext : IDisposable
    {
        private PlaybackSigningKeyTestContext(string root)
        {
            Provider = new DefaultAppPathProvider(
                new ServerStorageOptions { RootPath = root });
            new ServerStorageLayout(Provider).EnsureCreated();
        }

        public DefaultAppPathProvider Provider { get; }

        public static PlaybackSigningKeyTestContext Create() =>
            new(Path.Combine(
                Path.GetTempPath(),
                "VideoMonitorPlaybackSigningKeyTests",
                Guid.NewGuid().ToString("N")));

        public SqliteDatabaseInitializer CreateInitializer() =>
            new(new SqliteConnectionFactory(Provider));

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Provider.RootDirectory))
            {
                try
                {
                    Directory.Delete(Provider.RootDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
