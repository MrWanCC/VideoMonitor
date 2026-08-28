namespace VideoMonitor.Core.Tests.Playback;

public sealed class LocalZlmPlaybackSourceProviderStructureTests
{
    [Fact]
    public void Provider_UsesCatalogInsteadOfRetainingLocalDeviceConfiguration()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var providerPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Playback",
            "LocalZlmPlaybackSourceProvider.cs");
        var source = File.ReadAllText(providerPath);

        Assert.Contains("IDeviceCatalog", source);
        Assert.DoesNotContain("LocalDeviceOptions", source);
        Assert.DoesNotContain("local-device.json", source);
    }
}
