using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Converters;

public sealed class CameraStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is CameraStatus status
            ? status switch
            {
                CameraStatus.Online => "在线",
                CameraStatus.Warning => "异常",
                CameraStatus.Unknown => "未探测",
                _ => "离线"
            }
            : "离线";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

public sealed class CameraStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var resourceKey = value is CameraStatus status
            ? status switch
            {
                CameraStatus.Online => "OnlineGreenBrush",
                CameraStatus.Warning => "WarningOrangeBrush",
                CameraStatus.Unknown => "OfflineGrayBrush",
                _ => "OfflineGrayBrush"
            }
            : "OfflineGrayBrush";

        return System.Windows.Application.Current.TryFindResource(resourceKey) as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
