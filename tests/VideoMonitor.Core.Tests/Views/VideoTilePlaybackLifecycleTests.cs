using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.WPF;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Controls;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;
using VideoMonitor.Wpf.Views;
using VideoMonitor.Wpf.Views.Pages;

namespace VideoMonitor.Core.Tests.Views;

[Collection("Wpf")]
public sealed class VideoTilePlaybackLifecycleTests
{
    private static readonly SemaphoreSlim WpfGate = new(1, 1);

    [Fact]
    public async Task PlaceholderDoesNotExposeNativeWhiteVideoHost()
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
            viewModel.ShowPlaceholder();
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var placeholder = FindVisualChildren<TextBlock>(tile)
                .FirstOrDefault(text => text.Text == "模拟视频画面");
            var contentControl = FindVisualChild<ContentControl>(tile);
            Assert.NotNull(contentControl);
            Assert.Equal(Visibility.Collapsed, contentControl!.Visibility);
            Assert.Empty(FindVisualChildren<HwndHost>(tile));
            Assert.NotNull(placeholder);
            Assert.True(placeholder!.IsVisible);

            host.Close();
        });
    }

    [Fact]
    public async Task LoadingWithoutPreparedSessionDoesNotExposeNativeHost()
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

            var contentControl = FindVisualChild<ContentControl>(tile);
            Assert.NotNull(contentControl);
            Assert.Equal(Visibility.Collapsed, contentControl!.Visibility);
            Assert.Empty(FindVisualChildren<HwndHost>(tile));

            host.Close();
        });
    }

    [Fact]
    public async Task PreparedSessionLoadsVideoViewBeforePlay()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = new VideoTileViewModel();
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);

            host.Show();
            viewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            Assert.Equal(Visibility.Visible, videoView!.Visibility);
            Assert.True(videoView.IsLoaded);
            Assert.NotNull(videoView.GetBindingExpression(VideoView.MediaPlayerProperty));
            Assert.Equal(PlaybackState.Loading, viewModel.PlaybackState);

            host.Close();
        });
    }

    [Fact]
    public async Task ClearingSessionReturnsToPureWpfPlaceholder()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = new VideoTileViewModel();
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);

            host.Show();
            viewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            viewModel.ShowPlaceholder();
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var contentControl = FindVisualChild<ContentControl>(tile);
            Assert.NotNull(contentControl);
            Assert.Equal(Visibility.Collapsed, contentControl!.Visibility);
            Assert.Empty(FindVisualChildren<HwndHost>(tile));
            Assert.NotEmpty(FindVisualChildren<TextBlock>(tile)
                .Where(text => text.Text == "模拟视频画面" && text.IsVisible));

            host.Close();
        });
    }

    [Fact]
    public async Task FormalPlayingKeepsVideoViewLoaded()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = new VideoTileViewModel();
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);
            var session = CreateSession();

            host.Show();
            viewModel.AttachPreparedSession(session);
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            viewModel.ShowPlaying(session);
            host.UpdateLayout();

            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            Assert.Equal(Visibility.Visible, videoView!.Visibility);
            Assert.True(videoView.IsLoaded);
            Assert.Same(session, viewModel.PlaybackSession);

            host.Close();
        });
    }

    [Fact]
    public async Task RuntimeRecoveryDoesNotPrematurelyDestroyAttachedHost()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = new VideoTileViewModel();
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);

            host.Show();
            viewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            Assert.True(videoView!.IsLoaded);

            viewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            Assert.Same(videoView, FindVisualChild<VideoView>(tile));
            Assert.True(videoView.IsLoaded);

            host.Close();
        });
    }

    [Fact]
    public async Task FormalCoordinatorPlaysOnlyAfterVideoHostIsLoaded()
    {
        await RunOnStaAsync(async () =>
        {
            var deviceId = Guid.NewGuid();
            var channelId = Guid.NewGuid();
            var viewModel = new VideoTileViewModel();
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);
            var session = CreateSession();
            var playObserved = 0;

            host.Show();
            var coordinator = new FormalPlaybackCoordinator(
                new SingleSourceProvider(deviceId, channelId),
                (_, _) =>
                {
                    return session;
                },
                _ => { },
                viewModel,
                new WpfUiDispatcher(Dispatcher.CurrentDispatcher),
                playPlayback: playedSession =>
                {
                    Assert.True(viewModel.IsVideoHostReady);
                    Assert.Same(session, playedSession);
                    Volatile.Write(ref playObserved, 1);
                });

            await coordinator.StartAsync(deviceId, channelId, StreamType.Main);

            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            Assert.True(videoView!.IsLoaded);
            Assert.True(videoView.IsVisible);
            Assert.Same(session, viewModel.PlaybackSession);
            Assert.Equal(PlaybackState.Loading, viewModel.PlaybackState);
            Assert.Equal(1, Volatile.Read(ref playObserved));
            await coordinator.DisposeAsync();
            viewModel.ShowPlaceholder();
            host.Close();
        });
    }

    [Fact]
    public async Task VideoOverlayReceivesTileViewModelWithoutVisualAncestorLookup()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = CreateTileViewModel(channelNumber: 7);
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);

            host.Show();
            viewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            var overlay = FindForegroundOverlay();
            Assert.NotNull(overlay);
            Assert.Equal("VideoViewContent", overlay!.Name);
            Assert.Same(viewModel, overlay!.DataContext);

            var bindingDataItems = FindVisualChildren<TextBlock>(overlay)
                .Select(textBlock => textBlock.GetBindingExpression(TextBlock.TextProperty))
                .Where(binding => binding is not null)
                .Select(binding => binding!.DataItem)
                .ToArray();
            Assert.Contains(viewModel, bindingDataItems);
            Assert.Contains(
                FindVisualChildren<TextBlock>(overlay),
                textBlock => textBlock.Inlines
                    .OfType<Run>()
                    .Any(run => run.GetBindingExpression(Run.TextProperty)
                        ?.ParentBinding.Path.Path == nameof(VideoTileViewModel.ChannelNumber)
                        && run.GetBindingExpression(Run.TextProperty)?.DataItem
                            is VideoTileViewModel));
            Assert.Contains(
                FindVisualChildren<TextBlock>(overlay),
                textBlock => textBlock.GetBindingExpression(TextBlock.TextProperty)
                    ?.ParentBinding.Path.Path == nameof(VideoTileViewModel.PlaybackErrorTitle));
            Assert.Contains(
                FindVisualChildren<TextBlock>(overlay),
                textBlock => textBlock.GetBindingExpression(TextBlock.TextProperty)
                    ?.ParentBinding.Path.Path == nameof(VideoTileViewModel.PlaybackErrorDetail));
            Assert.NotNull(FindVisualChildByName(overlay, "VideoInteractionSurface"));

            host.Close();
        });
    }

    [Fact]
    public async Task VideoOverlayTracksChangedTileDataContext()
    {
        await RunOnStaAsync(async () =>
        {
            var firstViewModel = CreateTileViewModel(channelNumber: 1);
            var secondViewModel = CreateTileViewModel(channelNumber: 2);
            var tile = new VideoTile { DataContext = firstViewModel };
            var host = CreateHiddenHost(tile);

            host.Show();
            firstViewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            secondViewModel.AttachPreparedSession(CreateSession());
            tile.DataContext = secondViewModel;
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var videoView = FindVisualChild<VideoView>(tile);
            Assert.NotNull(videoView);
            var overlay = FindForegroundOverlay();
            Assert.NotNull(overlay);
            Assert.Equal("VideoViewContent", overlay!.Name);
            Assert.Same(secondViewModel, overlay!.DataContext);
            Assert.Contains(
                FindVisualChildren<TextBlock>(overlay),
                textBlock => textBlock.Inlines
                    .OfType<Run>()
                    .Any(run => run.GetBindingExpression(Run.TextProperty)
                        ?.ParentBinding.Path.Path == nameof(VideoTileViewModel.ChannelNumber)
                        && ReferenceEquals(
                            run.GetBindingExpression(Run.TextProperty)?.DataItem,
                            secondViewModel)));

            host.Close();
        });
    }

    [Fact]
    public async Task VideoInteractionSurfaceRemainsAvailable()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = CreateTileViewModel(channelNumber: 3);
            var tile = new VideoTile { DataContext = viewModel };
            var host = CreateHiddenHost(tile);

            host.Show();
            viewModel.AttachPreparedSession(CreateSession());
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var overlay = FindForegroundOverlay();
            Assert.NotNull(overlay);
            Assert.NotNull(FindVisualChildByName(overlay!, "VideoInteractionSurface"));

            host.Close();
        });
    }

    [Fact]
    public async Task PageReentryDoesNotExposeNativeHostForEmptyTiles()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = CreateConfiguredMonitor();
            var view = new MonitorView { DataContext = viewModel };
            var host = CreateHiddenHost(view);

            host.Show();
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();
            var emptyTiles = FindVisualChildren<VideoTile>(view)
                .Where(tile => tile.DataContext is VideoTileViewModel slot
                    && viewModel.MainTiles.Skip(1).Contains(slot))
                .ToArray();
            Assert.Equal(3, emptyTiles.Length);
            AssertEmptyTilesDoNotExposeNativeHost(emptyTiles);

            view.Visibility = Visibility.Collapsed;
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            view.Visibility = Visibility.Visible;
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            AssertEmptyTilesDoNotExposeNativeHost(emptyTiles);

            host.Close();
        });
    }

    [Fact]
    public async Task MainFourTilePlaceholderRegression()
    {
        await RunOnStaAsync(async () =>
        {
            var view = new MonitorView { DataContext = new MonitorViewModel(
                new MonitorSwitchService(Array.Empty<MonitorGroup>()),
                Array.Empty<MonitorGroup>(),
                new InMemoryDeviceCatalog([], [])) };
            var host = CreateHiddenHost(view);

            host.Show();
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            host.UpdateLayout();

            var tiles = FindVisualChildren<VideoTile>(view).ToArray();
            Assert.Equal(4, tiles.Length);
            AssertEmptyTilesDoNotExposeNativeHost(tiles);

            host.Close();
        });
    }

    [Fact]
    public async Task SecondaryThreeTilePlaceholderRegression()
    {
        await RunOnStaAsync(async () =>
        {
            var viewModel = new SecondaryMonitorViewModel(
                new MonitorSwitchService(Array.Empty<MonitorGroup>()),
                Array.Empty<MonitorGroup>(),
                new InMemoryDeviceCatalog([], []));
            var window = new SecondaryMonitorWindow(viewModel)
            {
                Opacity = 0,
                ShowInTaskbar = false,
            };

            window.Show();
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            window.UpdateLayout();

            var tiles = FindVisualChildren<VideoTile>(window).ToArray();
            Assert.Equal(3, tiles.Length);
            AssertEmptyTilesDoNotExposeNativeHost(tiles);

            window.AllowSecondaryWindowClose = true;
            window.Close();
        });
    }

    private static Window CreateHiddenHost(FrameworkElement content) => new()
    {
        Width = 800,
        Height = 600,
        Opacity = 0,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        Content = content,
    };

    private static PlaybackSession CreateSession() => new(
        Guid.NewGuid(),
        "test-stream",
        media: null,
        mediaPlayer: null);

    private static VideoTileViewModel CreateTileViewModel(int channelNumber)
    {
        var viewModel = new VideoTileViewModel();
        viewModel.Update(
            new CameraInfo("Camera", "Group", channelNumber),
            device: null,
            channel: null,
            CameraStatus.Unknown);
        return viewModel;
    }

    private static void AssertEmptyTilesDoNotExposeNativeHost(
        IEnumerable<VideoTile> tiles)
    {
        foreach (var tile in tiles)
        {
            var contentControl = FindVisualChild<ContentControl>(tile);
            Assert.NotNull(contentControl);
            Assert.Equal(Visibility.Collapsed, contentControl!.Visibility);
            Assert.Empty(FindVisualChildren<HwndHost>(tile));
            Assert.NotEmpty(FindVisualChildren<TextBlock>(tile)
                .Where(text => text.Text == "模拟视频画面" && text.IsVisible));
        }
    }

    private static MonitorViewModel CreateConfiguredMonitor()
    {
        var rootId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var root = new DeviceGroup
        {
            Id = rootId,
            Name = "Root",
            Kind = MonitorGroupType.Chute,
        };
        var child = new DeviceGroup
        {
            Id = groupId,
            Name = "Child",
            ParentId = rootId,
        };
        var device = new CameraDevice
        {
            Id = deviceId,
            Name = "Camera",
            GroupId = groupId,
        };
        device.Channels.Add(new CameraChannel
        {
            Id = channelId,
            DeviceId = deviceId,
            ChannelName = "Main",
        });
        var catalog = new InMemoryDeviceCatalog([root, child], [device]);
        var group = new MonitorGroup(
            "Child",
            MonitorGroupType.Chute,
            [new CameraInfo("Camera", "Child", 1)
            {
                DeviceId = deviceId,
                ChannelId = channelId,
            }])
        {
            GroupId = groupId,
            RootGroupId = rootId,
            RootName = "Root",
        };
        return new MonitorViewModel(new MonitorSwitchService([group]), [group], catalog);
    }

    private sealed class SingleSourceProvider : IFormalPlaybackSourceProvider
    {
        private readonly Guid deviceId;
        private readonly Guid channelId;

        public SingleSourceProvider(Guid deviceId, Guid channelId)
        {
            this.deviceId = deviceId;
            this.channelId = channelId;
        }

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid requestedDeviceId,
            Guid requestedChannelId,
            StreamType streamType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FormalPlaybackSource(
                deviceId,
                channelId,
                "test-stream",
                new Uri("https://server-b/live/test-stream"),
                DateTimeOffset.UtcNow.AddMinutes(1)));

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

    private static FrameworkElement? FindVisualChildByName(
        DependencyObject root,
        string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element
                && element.Name == name)
            {
                return element;
            }

            var descendant = FindVisualChildByName(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static FrameworkElement? FindForegroundOverlay()
    {
        return Application.Current.Windows
            .Cast<Window>()
            .Select(window => FindVisualChildByName(window, "VideoViewContent"))
            .FirstOrDefault(element => element is not null);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
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
