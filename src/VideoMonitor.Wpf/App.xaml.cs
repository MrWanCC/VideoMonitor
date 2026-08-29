using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using LibVLCSharp.Shared;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.Services;
using VideoMonitor.Wpf.ViewModels;
using VideoMonitor.Wpf.Views;

namespace VideoMonitor.Wpf;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalPlaybackConfiguration? playbackConfiguration = null;
        string? playbackConfigurationError = null;
        try
        {
            playbackConfiguration = LocalConfigurationLoader.Load(AppContext.BaseDirectory);
        }
        catch (InvalidOperationException exception)
        {
            playbackConfigurationError = exception.Message;
        }

        var singleCameraEnabled = playbackConfiguration?.SingleCameraTest.Enabled == true;
        var deviceCatalogStore = new JsonDeviceCatalogStore();
        InMemoryDeviceCatalog deviceCatalog;
        try
        {
            deviceCatalog = new DeviceCatalogBootstrapper(
                deviceCatalogStore).InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception.GetType().Name);
            var startupError = exception switch
            {
                UnauthorizedAccessException =>
                    "设备目录目录权限不足，请确认安装器已授予应用目录所需的修改权限。",
                IOException => "设备目录文件无法访问，应用将退出。",
                _ => "设备目录加载失败，应用将退出。"
            };
            System.Windows.MessageBox.Show(
                startupError,
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var groups = MonitorCatalogProjection.CreateGroups(deviceCatalog);
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name ==
                (singleCameraEnabled ? "西401溜井" : "备用1")),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitorViewModel = new MonitorViewModel(switchService, groups, deviceCatalog);
        var deviceManagementViewModel = new DeviceManagementViewModel(deviceCatalog);
        var screenService = new ScreenService();
        var mainViewModel = new MainViewModel(
            monitorViewModel,
            deviceManagementViewModel,
            screenService.HasSecondaryScreen);
        var secondaryViewModel = new SecondaryMonitorViewModel(switchService, groups, deviceCatalog);
        var mainWindow = new MainWindow(mainViewModel);
        var secondaryWindow = new SecondaryMonitorWindow(secondaryViewModel);
        var playbackCancellation = new CancellationTokenSource();
        SingleCameraPlaybackCoordinator? playbackCoordinator = null;
        VlcPlaybackService? vlcPlaybackService = null;
        HttpClient? zlmHttpClient = null;
        SingleCameraPlaybackSelection? playbackSelection = null;
        Task? playbackStartTask = null;
        var playbackStopped = false;

        if (singleCameraEnabled
            && playbackConfigurationError is null
            && playbackConfiguration is { } configuredPlayback)
        {
            try
            {
                playbackSelection = SingleCameraPlaybackComposition.SelectDevice(
                    deviceCatalog,
                    configuredPlayback.SingleCameraTest.DeviceId,
                    configuredPlayback.SingleCameraTest.ChannelId);
                zlmHttpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(7)
                };
                var zlmClient = new ZlmClient(zlmHttpClient, playbackConfiguration.Zlm);
                var provider = new LocalZlmPlaybackSourceProvider(
                    deviceCatalog,
                    zlmClient,
                    playbackConfiguration.Zlm,
                    TimeSpan.FromSeconds(6),
                    TimeSpan.FromMilliseconds(250));
                vlcPlaybackService = new VlcPlaybackService();
                playbackCoordinator = new SingleCameraPlaybackCoordinator(
                    provider,
                    vlcPlaybackService);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or PlaybackEngineException
                    or VLCException
                    or DllNotFoundException)
            {
                playbackConfigurationError = "LibVLC初始化失败。";
                Debug.WriteLine(exception.GetType().Name);
                vlcPlaybackService?.Dispose();
                zlmHttpClient?.Dispose();
                vlcPlaybackService = null;
                zlmHttpClient = null;
                playbackCoordinator = null;
            }
        }

        var persistenceCoordinator = new DeviceCatalogPersistenceCoordinator(
            deviceCatalog,
            deviceCatalogStore);

        mainWindow.SourceInitialized += (_, _) => screenService.PlaceMainWindow(mainWindow);

        void ApplySecondaryScreenVisibility()
        {
            if (!mainViewModel.IsSecondaryScreenVisible)
            {
                secondaryWindow.Hide();
                return;
            }

            screenService.PlaceSecondaryWindow(secondaryWindow);
            secondaryWindow.Show();
            secondaryWindow.Activate();
        }

        void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainViewModel.IsSecondaryScreenVisible))
            {
                ApplySecondaryScreenVisibility();
            }
        }

        mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        secondaryWindow.HiddenByUser += (_, _) => mainViewModel.IsSecondaryScreenVisible = false;
        var allowMainWindowClose = false;
        var shutdownInProgress = false;
        var finalCloseApplied = false;

        void ApplyFinalWindowClose()
        {
            if (finalCloseApplied)
            {
                return;
            }

            finalCloseApplied = true;
            mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            secondaryWindow.AllowSecondaryWindowClose = true;
            secondaryWindow.Close();
            playbackCancellation.Dispose();
        }

        async Task StopPlaybackAsync()
        {
            if (playbackStopped)
            {
                return;
            }

            playbackStopped = true;
            if (playbackStartTask is not null)
            {
                await playbackStartTask;
            }

            if (playbackCoordinator is not null)
            {
                try
                {
                    await playbackCoordinator.DisposeAsync();
                }
                catch (PlaybackSourceException exception)
                {
                    Debug.WriteLine($"{exception.Stage}: {exception.Title}");
                }
            }

            vlcPlaybackService?.Dispose();
            zlmHttpClient?.Dispose();

            try
            {
                await persistenceCoordinator.DisposeAsync();
            }
            catch (InvalidOperationException exception)
            {
                Debug.WriteLine(exception.GetType().Name);
                System.Windows.MessageBox.Show(
                    "设备目录保存失败，最后一次修改可能未保存。",
                    "关闭提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        async void OnMainWindowClosing(object? sender, CancelEventArgs args)
        {
            if (allowMainWindowClose)
            {
                ApplyFinalWindowClose();
                return;
            }

            args.Cancel = true;
            if (shutdownInProgress)
            {
                return;
            }

            shutdownInProgress = true;
            playbackCancellation.Cancel();
            await StopPlaybackAsync();
            allowMainWindowClose = true;
            mainWindow.Close();
        }

        mainWindow.Closing += OnMainWindowClosing;

        async Task StartPlaybackAsync()
        {
            if (playbackConfigurationError is not null)
            {
                monitorViewModel.MainTiles[0].ShowError(
                    "配置错误",
                    playbackConfigurationError);
                return;
            }

            if (playbackCoordinator is null || playbackSelection is null)
            {
                return;
            }

            try
            {
                await playbackCoordinator.StartAsync(
                    playbackSelection.Device,
                    playbackSelection.Channel,
                    monitorViewModel.MainTiles[0],
                    playbackCancellation.Token);
            }
            catch (OperationCanceledException) when (playbackCancellation.IsCancellationRequested)
            {
            }
        }

        void OnMainWindowContentRendered(object? sender, EventArgs args)
        {
            mainWindow.ContentRendered -= OnMainWindowContentRendered;
            playbackStartTask = StartPlaybackAsync();
        }

        if (singleCameraEnabled || playbackConfigurationError is not null)
        {
            mainWindow.ContentRendered += OnMainWindowContentRendered;
        }

        MainWindow = mainWindow;
        mainWindow.Show();
        ApplySecondaryScreenVisibility();
    }
}
