namespace VideoMonitor.Core.Tests.Playback;

public sealed class VlcPlaybackDisplayStructureTests
{
    [Fact]
    public void VlcPlaybackService_UsesDefaultRtspTransportWithoutTuning()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var serviceSourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Playback",
            "VlcPlaybackService.cs");
        var diagnosticsSourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Playback",
            "PlaybackDiagnostics.cs");
        var serviceSource = File.ReadAllText(serviceSourcePath);
        var diagnosticsSource = File.ReadAllText(diagnosticsSourcePath);

        Assert.Contains("\"--no-video-title-show\"", serviceSource);
        Assert.Contains("\"--stats\"", serviceSource);
        Assert.DoesNotContain("--rtsp-tcp", serviceSource);
        Assert.DoesNotContain("--clock-jitter", serviceSource);
        Assert.DoesNotContain("--clock-synchro", serviceSource);
        Assert.DoesNotContain("--network-caching", serviceSource);
        Assert.DoesNotContain("--live-caching", serviceSource);
        Assert.DoesNotContain("--file-caching", serviceSource);
        Assert.DoesNotContain("--drop-late-frames", serviceSource);
        Assert.DoesNotContain("--skip-frames", serviceSource);
        Assert.DoesNotContain("--avcodec-hw", serviceSource);

        var optionsField = typeof(VideoMonitor.Wpf.Playback.VlcPlaybackService).GetField(
            "LibVlcOptions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(optionsField);
        var options = Assert.IsType<string[]>(optionsField.GetValue(null));
        Assert.Equal(
            new[] { "--no-video-title-show", "--stats" },
            options);

        Assert.Contains(
            "options=--no-video-title-show,--stats",
            diagnosticsSource);
        Assert.DoesNotContain(
            "options=--no-video-title-show,--rtsp-tcp,--stats",
            diagnosticsSource);
    }

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

    [Fact]
    public void VlcPlaybackService_EnablesOnlyStatsForDiagnostics()
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

        Assert.Contains("\"--no-video-title-show\"", source);
        Assert.DoesNotContain("--rtsp-tcp", source);
        Assert.Contains("\"--stats\"", source);
        Assert.DoesNotContain("--network-caching", source);
        Assert.DoesNotContain("--live-caching", source);
        Assert.DoesNotContain("--clock-synchro", source);
        Assert.DoesNotContain("--drop-late-frames", source);
        Assert.DoesNotContain("--skip-frames", source);
        Assert.DoesNotContain("--avcodec-hw", source);
    }
}
