using VideoMonitor.Client.Controls;
using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Tests.Controls;

public sealed class VideoTileControlTests
{
    [Fact]
    public void SetCamera_UpdatesDisplayedMetadata()
    {
        using var tile = new VideoTileControl();

        tile.SetCamera(new CameraInfo("西401溜井-通道2", "西401溜井", 2));

        Assert.Equal("西401溜井-通道2", tile.CameraNameText);
        Assert.Equal("西401溜井", tile.GroupNameText);
        Assert.Equal("通道 2", tile.ChannelText);
        Assert.Equal("在线", tile.StatusText);
    }

    [Fact]
    public void ShowError_DisplaysMessageAndErrorStatus()
    {
        using var tile = new VideoTileControl();

        tile.ShowError("信号异常");

        Assert.Equal("异常", tile.StatusText);
        Assert.Equal("信号异常", tile.PlaceholderText);
    }
}
