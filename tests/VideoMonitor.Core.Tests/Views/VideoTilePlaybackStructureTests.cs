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
        Assert.Contains("PlaybackState.Playing", xaml);
        Assert.Contains("PlaybackState.Error", xaml);
        Assert.Contains("正在连接视频", xaml);
        Assert.Contains("PlaybackErrorTitle", xaml);
        Assert.Contains("PlaybackErrorDetail", xaml);
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
