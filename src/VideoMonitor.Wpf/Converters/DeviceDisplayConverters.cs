using System.Globalization;
using System.Windows.Data;
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
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CameraDevice device && device.Channels.FirstOrDefault() is { } channel
            ? channel.ChannelNo.ToString(culture)
            : "--";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FirstChannelStreamConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CameraDevice device && device.Channels.FirstOrDefault() is { } channel
            ? channel.StreamType == StreamType.Main ? "主码流" : "辅码流"
            : "--";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
