using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.Configuration;

public static class LocalDeviceCatalogOverride
{
    public static void Apply(
        IDeviceCatalog catalog,
        LocalDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);

        var device = catalog.GetDevice(options.DeviceId)
            ?? throw new InvalidOperationException("本地设备覆盖对应的设备不存在。");
        var channel = device.Channels
            .SingleOrDefault(item => item.Id == options.ChannelId)
            ?? throw new InvalidOperationException("本地设备覆盖对应的通道不存在。");

        device.IpAddress = options.IpAddress;
        device.RtspPort = options.RtspPort;
        device.Username = options.Username;
        device.Password = options.Password;
        channel.ChannelNo = options.ChannelNo;
        channel.StreamType = options.StreamType;
        catalog.UpdateDevice(device);
    }
}
