using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.WPF;
using VideoMonitor.Wpf.Controls;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Views;

[Collection("Wpf")]
public sealed class VideoTilePlaybackLifecycleTests
{
    private static readonly SemaphoreSlim WpfGate = new(1, 1);

    [Fact]
    public async Task VideoTileKeepsVideoViewLoadedWhilePlaybackIsLoading()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = new VideoTileViewModel();
            var tile = new VideoTile
            {
                DataContext = viewModel,
            };
            var host = new Window
            {
                Width = 800,
                Height = 600,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = tile,
            };

            host.Show();
            viewModel.ShowLoading();
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            Assert.Equal(Visibility.Visible, videoView!.Visibility);
            Assert.True(videoView.IsLoaded);

            host.Close();
        });
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static Application CreateTestApplication()
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        foreach (var resourceName in new[]
        {
            "Colors.xaml",
            "Icons.xaml",
            "Typography.xaml",
            "Buttons.xaml",
            "Controls.xaml",
        })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/VideoMonitor.Wpf;component/Themes/{resourceName}",
                    UriKind.Absolute),
            });
        }

        return application;
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        await WpfGate.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                Exception? failure = null;
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(dispatcher));
                    ResetApplicationState();
                    _ = CreateTestApplication();
                    _ = RunAsync();
                    Dispatcher.Run();
                    ResetApplicationState();

                    if (failure is null)
                    {
                        completion.SetResult(null);
                    }
                    else
                    {
                        completion.SetException(failure);
                    }

                    async Task RunAsync()
                    {
                        try
                        {
                            await action();
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                        finally
                        {
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                        }
                    }
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            })
            {
                IsBackground = true,
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task;
        }
        finally
        {
            WpfGate.Release();
        }
    }

    private static void ResetApplicationState()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic;
        typeof(Application).GetField("_appInstance", flags)!.SetValue(null, null);
        typeof(Application).GetField("_appCreatedInThisAppDomain", flags)!
            .SetValue(null, false);
        typeof(Application).GetField("_isShuttingDown", flags)!
            .SetValue(null, false);
    }
}
