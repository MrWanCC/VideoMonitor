using System.Reflection;
using VideoMonitor.Wpf.Playback;

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

    [Fact]
    public void VlcPlaybackService_UsesOnlyApprovedExperimentOptionsForDiagnostics()
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
        Assert.Contains("\"--rtsp-tcp\"", source);
        Assert.Contains("\"--stats\"", source);
        Assert.Contains("\"--clock-synchro=0\"", source);
        Assert.DoesNotContain("--network-caching", source);
        Assert.DoesNotContain("--live-caching", source);
        Assert.DoesNotContain("--clock-jitter", source);
        Assert.DoesNotContain("--drop-late-frames", source);
        Assert.DoesNotContain("--skip-frames", source);
        Assert.DoesNotContain("--avcodec-hw", source);
    }

    [Fact]
    public void VlcPlaybackService_UsesClockSynchroZeroWithoutClockJitter()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var playbackSourcePath = Path.Combine(
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
        var playbackSource = File.ReadAllText(playbackSourcePath);
        var diagnosticsSource = File.ReadAllText(diagnosticsSourcePath);

        Assert.Contains("\"--clock-synchro=0\"", playbackSource);
        Assert.DoesNotContain("--clock-jitter=0", playbackSource);

        var optionsField = typeof(VlcPlaybackService).GetField(
            "LibVlcOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(optionsField);
        var actualOptions = Assert.IsType<string[]>(optionsField!.GetValue(null));
        Assert.Equal(
            [
                "--no-video-title-show",
                "--rtsp-tcp",
                "--stats",
                "--clock-synchro=0"
            ],
            actualOptions);

        Assert.Contains(
            "options=--no-video-title-show,--rtsp-tcp,--stats,--clock-synchro=0",
            diagnosticsSource);
        Assert.DoesNotContain(
            "options=--no-video-title-show,--rtsp-tcp,--stats,--clock-jitter=0",
            diagnosticsSource);
    }
}
