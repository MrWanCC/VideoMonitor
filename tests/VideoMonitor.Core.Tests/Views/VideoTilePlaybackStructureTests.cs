namespace VideoMonitor.Core.Tests.Views;

public sealed class VideoTilePlaybackStructureTests
{
    [Fact]
    public void VideoTile_ContainsOnePersistentVideoViewAndFourPlaybackStates()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var xamlPath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Controls",
            "VideoTile.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Equal(1, CountOccurrences(xaml, "<vlc:VideoView "));
        Assert.Contains("PlaybackSession.MediaPlayer", xaml);
        Assert.Contains("PlaybackState.Placeholder", xaml);
        Assert.Contains("PlaybackState.Loading", xaml);
        Assert.Contains("PlaybackState.Error", xaml);
        Assert.Contains("正在连接视频", xaml);
        Assert.Contains("PlaybackErrorTitle", xaml);
        Assert.Contains("PlaybackErrorDetail", xaml);
        Assert.DoesNotContain("VideoOverlayTemplate", xaml);
        Assert.DoesNotContain("Stretch=\"Fill\"", xaml);
        Assert.Contains("MouseLeftButtonDown=\"OnVideoSurfaceMouseLeftButtonDown\"", xaml);
        Assert.DoesNotContain("MouseDoubleClick=\"OnVideoSurfaceDoubleClick\"", xaml);
        Assert.Contains("x:Name=\"VideoInteractionSurface\"", xaml);

        var codeBehindPath = Path.ChangeExtension(xamlPath, ".xaml.cs");
        var codeBehind = File.ReadAllText(codeBehindPath);
        Assert.Contains("IsVisibleChanged += OnVideoTileIsVisibleChanged", codeBehind);
        Assert.Contains("Window.GetWindow(VideoInteractionSurface)", codeBehind);
        Assert.Contains("overlayWindow.Hide()", codeBehind);
        Assert.Contains("overlayWindow.Show()", codeBehind);

        var videoViewStart = xaml.IndexOf("<vlc:VideoView ", StringComparison.Ordinal);
        var videoViewEnd = xaml.IndexOf("</vlc:VideoView>", videoViewStart, StringComparison.Ordinal);
        Assert.True(videoViewStart >= 0 && videoViewEnd > videoViewStart);
        var videoViewContent = xaml[videoViewStart..videoViewEnd];
        Assert.Contains("Visibility=\"Visible\"", videoViewContent);
        Assert.Contains("x:Name=\"VideoViewContent\"", videoViewContent);
        Assert.Contains("PlaybackState.Placeholder", videoViewContent);
        Assert.Contains("PlaybackState.Loading", videoViewContent);
        Assert.Contains("PlaybackState.Error", videoViewContent);
        Assert.Contains("Background=\"#02000000\"", videoViewContent);
        Assert.DoesNotContain("Background=\"Transparent\"", videoViewContent);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var position = 0;
        while ((position = source.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }
}
