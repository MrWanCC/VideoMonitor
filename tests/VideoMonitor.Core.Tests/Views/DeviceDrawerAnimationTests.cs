using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf;
using VideoMonitor.Wpf.ViewModels;
using VideoMonitor.Wpf.Views.Pages;

namespace VideoMonitor.Core.Tests.Views;

[Collection("Wpf")]
public sealed class DeviceDrawerAnimationTests
{
    [Fact]
    public void Drawer_OverlaysDeviceListAndFinishesCloseBeforeHiding()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                var data = MockDeviceData.Create();
                var viewModel = new DeviceManagementViewModel(
                    new InMemoryDeviceCatalog(data.Groups, data.Devices));
                var view = new DeviceView { DataContext = viewModel };
                var host = new Window
                {
                    Width = 1920,
                    Height = 1032,
                    Opacity = 0,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = view,
                };
                host.Show();

                viewModel.AddDeviceCommand.Execute(null);
                var drawer = Assert.IsType<Border>(view.FindName("EditorDrawer"));
                var shade = Assert.IsType<Border>(view.FindName("DrawerShade"));
                var editorScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("EditorScrollViewer"));
                var translation = Assert.IsType<TranslateTransform>(drawer.RenderTransform);
                PumpDispatcherUntil(
                    () => translation.X <= 0.5 && shade.Opacity >= 0.12,
                    TimeSpan.FromSeconds(2));

                Assert.Equal(520, drawer.Width);
                Assert.Equal(1, Grid.GetColumn(drawer));
                Assert.Equal(1, Grid.GetColumnSpan(drawer));
                Assert.Equal(Visibility.Visible, drawer.Visibility);
                Assert.True(drawer.IsHitTestVisible);
                Assert.InRange(translation.X, -0.5, 0.5);
                Assert.InRange(shade.Opacity, 0.12, 0.18);
                Assert.True(shade.IsHitTestVisible);
                Assert.Equal(Visibility.Collapsed, editorScrollViewer.ComputedVerticalScrollBarVisibility);

                viewModel.CancelEditCommand.Execute(null);
                Assert.Equal(Visibility.Visible, drawer.Visibility);
                Assert.True(drawer.IsHitTestVisible);

                PumpDispatcherUntil(
                    () => drawer.Visibility == Visibility.Collapsed,
                    TimeSpan.FromSeconds(2));

                Assert.Equal(Visibility.Collapsed, drawer.Visibility);
                Assert.False(drawer.IsHitTestVisible);
                Assert.InRange(translation.X, 519.5, 520.5);
                Assert.Equal(Visibility.Collapsed, shade.Visibility);
                Assert.False(shade.IsHitTestVisible);
                host.Close();
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
