using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class StreamIdGeneratorTests
{
    [Fact]
    public void Generate_IsStableAndIndependentOfMutableFields()
    {
        var device = CreateDevice(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "原名称",
            "192.168.0.2");
        var channel = CreateChannel(device.Id, 1);

        var before = StreamIdGenerator.Generate(device, channel);
        device.Name = "新名称";
        device.IpAddress = "10.0.0.20";
        device.Username = "other";
        var after = StreamIdGenerator.Generate(device, channel);

        Assert.Equal("device_50000000000000000000000000000001_channel_1_main", before);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Generate_DistinguishesMainAndSubStreams()
    {
        var device = CreateDevice(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "Camera 01",
            "192.168.0.2");

        var main = StreamIdGenerator.Generate(
            device,
            CreateChannel(device.Id, 1, StreamType.Main));
        var sub = StreamIdGenerator.Generate(
            device,
            CreateChannel(device.Id, 1, StreamType.Sub));

        Assert.Equal("device_50000000000000000000000000000001_channel_1_main", main);
        Assert.Equal("device_50000000000000000000000000000001_channel_1_sub", sub);
    }

    [Fact]
    public void Generate_ContainsOnlyAsciiLettersDigitsAndUnderscores()
    {
        var device = CreateDevice(Guid.NewGuid(), "西401溜井", "192.168.0.2");

        var streamId = StreamIdGenerator.Generate(device, CreateChannel(device.Id, 1));

        Assert.Matches("^[a-z0-9_]+$", streamId);
    }

    private static CameraDevice CreateDevice(Guid id, string name, string ipAddress) => new()
    {
        Id = id,
        Name = name,
        IpAddress = ipAddress,
        Username = "admin"
    };

    private static CameraChannel CreateChannel(
        Guid deviceId,
        int channelNo,
        StreamType streamType = StreamType.Main) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        ChannelNo = channelNo,
        StreamType = streamType
    };
}
