using System.Windows;
using System.Windows.Media;
using FormsScreen = System.Windows.Forms.Screen;
using WpfWindow = System.Windows.Window;

namespace VideoMonitor.Wpf.Services;

public sealed class ScreenService
{
    public bool HasSecondaryScreen => FormsScreen.AllScreens.Length >= 2;

    public void PlaceMainWindow(WpfWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var primary = FormsScreen.PrimaryScreen?.WorkingArea ?? FormsScreen.AllScreens[0].WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = primary.Left / dpi.DpiScaleX + 32;
        window.Top = primary.Top / dpi.DpiScaleY + 24;
        window.Width = Math.Min(1600, primary.Width / dpi.DpiScaleX - 64);
        window.Height = Math.Min(920, primary.Height / dpi.DpiScaleY - 48);
    }

    public void PlaceSecondaryWindow(WpfWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var screens = FormsScreen.AllScreens;
        var target = screens.Length >= 2
            ? screens[1].WorkingArea
            : (FormsScreen.PrimaryScreen?.WorkingArea ?? screens[0].WorkingArea);
        var dpi = VisualTreeHelper.GetDpi(window);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Height = 540;
        window.MinHeight = 540;
        window.MaxHeight = 540;

        if (screens.Length >= 2)
        {
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.Left = target.Left / dpi.DpiScaleX;
            window.Top = target.Top / dpi.DpiScaleY;
            window.Width = target.Width / dpi.DpiScaleX;
        }
        else
        {
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
            window.Left = target.Left / dpi.DpiScaleX + 80;
            window.Top = target.Top / dpi.DpiScaleY + 80;
            window.Width = Math.Min(1440, target.Width / dpi.DpiScaleX - 160);
        }
    }
}
