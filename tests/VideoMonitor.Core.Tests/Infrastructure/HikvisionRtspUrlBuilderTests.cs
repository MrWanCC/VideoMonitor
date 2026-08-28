using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Hikvision;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class HikvisionRtspUrlBuilderTests
{
    [Theory]
    [InlineData(StreamType.Main, 1, "/Streaming/Channels/101")]
    [InlineData(StreamType.Sub, 1, "/Streaming/Channels/102")]
    [InlineData(StreamType.Main, 2, "/Streaming/Channels/201")]
    [InlineData(StreamType.Sub, 2, "/Streaming/Channels/202")]
    public void Build_UsesHikvisionChannelEncoding(
        StreamType streamType,
        int channelNo,
        string expectedPath)
    {
        var (device, channel) = CreateDevice(streamType, channelNo);

        var uri = HikvisionRtspUrlBuilder.Build(device, channel);

        Assert.Equal(expectedPath, uri.AbsolutePath);
        Assert.Equal("192.168.0.2", uri.Host);
        Assert.Equal(554, uri.Port);
    }

    [Fact]
    public void Redact_DoesNotExposePassword()
    {
        var (device, channel) = CreateDevice(StreamType.Main, 1);

        var redacted = HikvisionRtspUrlBuilder.Redact(
            HikvisionRtspUrlBuilder.Build(device, channel));

        Assert.Contains("admin:******@", redacted);
        Assert.DoesNotContain(device.Password, redacted);
    }

    private static (CameraDevice Device, CameraChannel Channel) CreateDevice(
        StreamType streamType,
        int channelNo)
    {
        var device = new CameraDevice
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Name = "Camera01",
            IpAddress = "192.168.0.2",
            RtspPort = 554,
            Username = "admin",
            Password = "secret-password"
        };
        var channel = new CameraChannel
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
            DeviceId = device.Id,
            ChannelNo = channelNo,
            StreamType = streamType
        };
        return (device, channel);
    }
}
