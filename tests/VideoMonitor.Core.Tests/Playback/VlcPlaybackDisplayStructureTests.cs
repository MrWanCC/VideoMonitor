namespace VideoMonitor.Core.Tests.Playback;

public sealed class VlcPlaybackDisplayStructureTests
{
    [Fact]
    public void VlcPlaybackService_UsesMildNonCroppingAspectRatio()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var sourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Playback",
            "VlcPlaybackService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("mediaPlayer.AspectRatio = \"19:10\"", source);
        Assert.DoesNotContain("CropGeometry", source);
        Assert.DoesNotContain("mediaPlayer.Scale", source);
    }
}
