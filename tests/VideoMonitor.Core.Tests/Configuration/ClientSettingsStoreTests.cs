using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Core.Tests.Configuration;

public sealed class ClientSettingsStoreTests
{
    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEmpty()
    {
        var root = CreateTemporaryRoot();

        var store = new JsonClientSettingsStore(root);

        var settings = store.Load();

        Assert.Equal(ClientSettings.Empty, settings);
        Assert.Null(settings.Server.BaseUrl);
    }

    [Fact]
    public async Task FirstSave_RoundTripsBaseUrl()
    {
        var root = CreateTemporaryRoot();
        var store = new JsonClientSettingsStore(root);
        var expected = new ClientSettings(
            new ClientServerSettings("http://127.0.0.1:5080"));

        await store.SaveAsync(expected);

        Assert.True(File.Exists(Path.Combine(root, "client-settings.json")));
        Assert.Equal(expected, store.Load());
    }

    [Fact]
    public async Task ExistingSave_AtomicallyReplacesSettings()
    {
        var root = CreateTemporaryRoot();
        var store = new JsonClientSettingsStore(root);
        var first = new ClientSettings(
            new ClientServerSettings("https://server-a"));
        var second = new ClientSettings(
            new ClientServerSettings("https://server-b"));

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        Assert.Equal(second, store.Load());
        Assert.False(File.Exists(Path.Combine(root, "client-settings.tmp")));
    }

    [Fact]
    public void MalformedJson_ThrowsSafeInvalidDataException()
    {
        var root = CreateTemporaryRoot();
        var path = Path.Combine(root, "client-settings.json");
        const string malformedJson = "{ broken json";
        Directory.CreateDirectory(root);
        File.WriteAllText(path, malformedJson);

        var exception = Assert.Throws<InvalidDataException>(() =>
            new JsonClientSettingsStore(root).Load());

        Assert.DoesNotContain(malformedJson, exception.Message);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(malformedJson, File.ReadAllText(path));
    }

    [Fact]
    public async Task ReplaceFailure_PreservesOldSettingsAndCleansTemporaryFile()
    {
        var root = CreateTemporaryRoot();
        var store = new JsonClientSettingsStore(root);
        var first = new ClientSettings(
            new ClientServerSettings("https://server-a"));
        var second = new ClientSettings(
            new ClientServerSettings("https://server-b"));
        await store.SaveAsync(first);

        var targetPath = Path.Combine(root, "client-settings.json");
        await using (var targetLock = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(second));
        }

        Assert.Equal(first, store.Load());
        Assert.False(File.Exists(Path.Combine(root, "client-settings.tmp")));
    }

    [Fact]
    public void InjectedRoot_ReturnsClientSettingsJsonInsideRoot()
    {
        var root = CreateTemporaryRoot();

        var path = ClientSettingsPathProvider.GetPath(root);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(root), "client-settings.json"),
            path);
    }

    [Fact]
    public void DefaultPath_UsesCommonApplicationDataVideoMonitorClient()
    {
        var path = ClientSettingsPathProvider.GetPath();

        var commonApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        var expectedRoot = Path.Combine(
            commonApplicationData,
            "VideoMonitor",
            "Client");

        Assert.Equal(
            Path.Combine(expectedRoot, "client-settings.json"),
            path);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "VideoMonitor.ClientSettings.Tests",
            Guid.NewGuid().ToString("N"));
}
