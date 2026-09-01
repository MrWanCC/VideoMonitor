using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class MediaStreamKeyTests
{
    [Fact]
    public void FormalIdIsStableForSameIdentityAndIgnoresNames()
    {
        var deviceId = Guid.Parse("51000000-0000-0000-0000-000000000001");
        var channelId = Guid.Parse("61000000-0000-0000-0000-000000000001");
        var key = new MediaStreamKey(deviceId, channelId, StreamType.Main);

        var formalId = MediaStreamIdGenerator.GenerateFormal(key);
        var sameId = new MediaStreamKey(deviceId, channelId, StreamType.Main)
            .ToFormalStreamId();
        var differentChannelId = MediaStreamIdGenerator.GenerateFormal(
            new MediaStreamKey(
                deviceId,
                Guid.Parse("61000000-0000-0000-0000-000000000002"),
                StreamType.Main));
        var differentStreamType = MediaStreamIdGenerator.GenerateFormal(
            new MediaStreamKey(deviceId, channelId, StreamType.Sub));

        Assert.Equal("vm_51000000000000000000000000000001_61000000000000000000000000000001_main", formalId);
        Assert.Equal(formalId, sameId);
        Assert.NotEqual(formalId, differentChannelId);
        Assert.NotEqual(formalId, differentStreamType);
        Assert.True(MediaStreamIdGenerator.TryParseFormal(formalId, out var parsed));
        Assert.Equal(key, parsed);

        Assert.False(MediaStreamIdGenerator.TryParseFormal("vm_device_name_main", out _));
        Assert.False(MediaStreamIdGenerator.TryParseFormal(
            "vm_51000000000000000000000000000001_61000000000000000000000000000001_Main_extra",
            out _));
        Assert.False(MediaStreamIdGenerator.TryParseFormal(
            "vm_51000000000000000000000000000001_61000000000000000000000000000001_unknown",
            out _));
    }
}
