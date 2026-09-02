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

    [Fact]
    public void FormalPrepareDoesNotStartLibVlcBeforeCoordinatorAttachesTheView()
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
        var prepareStart = source.IndexOf(
            "public PlaybackSession Prepare(",
            StringComparison.Ordinal);
        var playStart = source.IndexOf(
            "public void Play(",
            prepareStart,
            StringComparison.Ordinal);

        Assert.True(prepareStart >= 0 && playStart > prepareStart);
        Assert.DoesNotContain("mediaPlayer.Play()", source[prepareStart..playStart]);
        Assert.Contains("session.MediaPlayer.Play()", source[playStart..]);
    }
}
