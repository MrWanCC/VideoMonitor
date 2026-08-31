using System.Globalization;
using System.Windows.Data;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Converters;

public sealed class StreamTypeToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StreamType.Main => "主码流",
        StreamType.Sub => "辅码流",
        _ => "--"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TransportModeToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        TransportMode.Auto => "Auto",
        TransportMode.Tcp => "TCP",
        TransportMode.Udp => "UDP",
        _ => "--"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FirstChannelNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CameraDevice device when device.Channels.FirstOrDefault() is { } channel =>
            channel.ChannelNo.ToString(culture),
        CameraDeviceDto device when device.Channels.FirstOrDefault() is { } channel =>
            channel.ChannelNo.ToString(culture),
        _ => "--"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FirstChannelStreamConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CameraDevice device when device.Channels.FirstOrDefault() is { } channel =>
            StreamTypeToText(channel.StreamType),
        CameraDeviceDto device when device.Channels.FirstOrDefault() is { } channel =>
            StreamTypeToText(channel.StreamType),
        _ => "--"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string StreamTypeToText(StreamType streamType) => streamType switch
    {
        StreamType.Main => "主码流",
        StreamType.Sub => "辅码流",
        _ => "--"
    };
}

public sealed class DeviceCatalogStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        DeviceCatalogStatusResolver.Resolve(value) switch
        {
            CameraStatus.Online => "在线",
            CameraStatus.Warning => "异常",
            CameraStatus.Unknown => "未探测",
            _ => "离线"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

public sealed class DeviceCatalogStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var resourceKey = DeviceCatalogStatusResolver.Resolve(value) switch
        {
            CameraStatus.Online => "OnlineGreenBrush",
            CameraStatus.Warning => "WarningOrangeBrush",
            _ => "OfflineGrayBrush"
        };

        return System.Windows.Application.Current?.TryFindResource(resourceKey)
                as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

internal static class DeviceCatalogStatusResolver
{
    public static CameraStatus Resolve(object value) => value switch
    {
        CameraDeviceDto => CameraStatus.Unknown,
        CameraDevice device => device.Status,
        CameraStatus status => status,
        _ => CameraStatus.Unknown
    };
}
