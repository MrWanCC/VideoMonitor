using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LibVLCSharp.Shared;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.Services;
using VideoMonitor.Wpf.ViewModels;
using VideoMonitor.Wpf.Views;
using WpfMessageBox = System.Windows.MessageBox;

namespace VideoMonitor.Wpf;

public partial class App
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LocalPlaybackConfiguration playbackConfiguration;
        try
        {
            playbackConfiguration = LocalConfigurationLoader.Load(
                AppContext.BaseDirectory);
        }
        catch (InvalidOperationException exception)
        {
            WpfMessageBox.Show(
                exception.Message,
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ApplicationCatalogComposition composition;
        try
        {
            composition = await ApplicationCatalogComposition
                .CreateAsync(
                    playbackConfiguration,
                    new ApplicationCatalogComposition.Dependencies())
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception.GetType().Name);
            WpfMessageBox.Show(
                GetCatalogStartupError(exception),
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var singleCameraEnabled = !composition.IsFormalCentralMode;
        var groups = composition.LocalCatalog is { } localCatalog
            ? MonitorCatalogProjection.CreateGroups(localCatalog)
            : MonitorCatalogProjection.CreateGroups(composition.ReadModel);
        var switchService = new MonitorSwitchService(groups);
        var monitorViewModel = composition.LocalCatalog is { } localCatalogForView
            ? new MonitorViewModel(switchService, groups, localCatalogForView)
            : new MonitorViewModel(switchService, composition.ReadModel);
        var deviceManagementViewModel = composition.LocalCatalog is { } localCatalogForManagement
            ? new DeviceManagementViewModel(localCatalogForManagement)
            : new DeviceManagementViewModel(
                composition.ReadModel,
                composition.CommandService,
                composition.TestPreview);
        var secondaryViewModel = composition.LocalCatalog is { } localCatalogForSecondary
            ? new SecondaryMonitorViewModel(
                switchService,
                groups,
                localCatalogForSecondary)
            : new SecondaryMonitorViewModel(
                switchService,
                composition.ReadModel);
        var screenService = new ScreenService();
        var mainViewModel = composition.IsFormalCentralMode
            ? new MainViewModel(
                monitorViewModel,
                deviceManagementViewModel,
                composition.ServerStatus!,
                () => new ServerSettingsViewModel(
                    composition.Coordinator!,
                    composition.ClientSettingsStore!,
                    () => deviceManagementViewModel.HasUnsavedDraft),
                new MediaSettingsViewModel(
                    composition.MediaSettingsApiClient!,
                    () => composition.ServerStatus!.BaseUri),
                screenService.HasSecondaryScreen)
            : new MainViewModel(
                monitorViewModel,
                deviceManagementViewModel,
                screenService.HasSecondaryScreen);
        var mainWindow = new MainWindow(mainViewModel);
        var secondaryWindow = new SecondaryMonitorWindow(secondaryViewModel);
        var playbackCancellation = new CancellationTokenSource();
        SingleCameraPlaybackCoordinator? playbackCoordinator = null;
        VlcPlaybackService? vlcPlaybackService = null;
        SingleCameraPlaybackSelection? playbackSelection = null;
        string? playbackConfigurationError = null;
        Task? playbackStartTask = null;

        if (singleCameraEnabled)
        {
            try
            {
                playbackSelection = SingleCameraPlaybackComposition.SelectDevice(
                    composition.LocalCatalog!,
                    playbackConfiguration.SingleCameraTest.DeviceId,
                    playbackConfiguration.SingleCameraTest.ChannelId);
                vlcPlaybackService = new VlcPlaybackService();
                playbackCoordinator = new SingleCameraPlaybackCoordinator(
                    composition.LocalPlaybackSource!,
                    vlcPlaybackService);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or PlaybackEngineException
                    or VLCException
                    or DllNotFoundException)
            {
                Debug.WriteLine(exception.GetType().Name);
                playbackConfigurationError = "LibVLC初始化失败。";
                vlcPlaybackService?.Dispose();
                vlcPlaybackService = null;
            }
        }

        mainWindow.SourceInitialized += (_, _) =>
            screenService.PlaceMainWindow(mainWindow);

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
        secondaryWindow.HiddenByUser += (_, _) =>
            mainViewModel.IsSecondaryScreenVisible = false;
        var allowMainWindowClose = false;
        var shutdownInProgress = false;
        var finalCloseApplied = false;

        void OnPersistenceFailed(object? sender, EventArgs args)
        {
            if (shutdownInProgress)
            {
                return;
            }

            _ = mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!shutdownInProgress)
                {
                    WpfMessageBox.Show(
                        mainWindow,
                        "设备配置保存失败。当前修改可能无法在重启后保留，请检查磁盘空间或数据目录权限。",
                        "保存提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }));
        }

        if (composition.PersistenceCoordinator is { } persistenceCoordinator)
        {
            persistenceCoordinator.PersistenceFailed += OnPersistenceFailed;
        }

        void ApplyFinalWindowClose()
        {
            if (finalCloseApplied)
            {
                return;
            }

            finalCloseApplied = true;
            mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            if (composition.PersistenceCoordinator is { } persistenceCoordinator)
            {
                persistenceCoordinator.PersistenceFailed -= OnPersistenceFailed;
            }

            secondaryWindow.AllowSecondaryWindowClose = true;
            secondaryWindow.Close();
            playbackCancellation.Dispose();
        }

        async Task StopPlaybackResourcesAsync()
        {
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
        }

        async Task StopApplicationResourcesAsync()
        {
            try
            {
                await ShutdownCleanupCoordinator.ExecuteAsync(
                    StopPlaybackResourcesAsync,
                    composition.DisposeAsync);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.GetType().Name);
                var message = composition.IsFormalCentralMode
                    ? "关闭时中央连接清理失败。"
                    : "关闭时资源清理失败，设备目录最后一次修改可能未保存。";
                WpfMessageBox.Show(
                    message,
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
            await StopApplicationResourcesAsync();
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
            catch (OperationCanceledException)
                when (playbackCancellation.IsCancellationRequested)
            {
            }
        }

        void OnMainWindowContentRendered(object? sender, EventArgs args)
        {
            mainWindow.ContentRendered -= OnMainWindowContentRendered;
            playbackStartTask = StartPlaybackAsync();
        }

        if (singleCameraEnabled)
        {
            mainWindow.ContentRendered += OnMainWindowContentRendered;
        }

        MainWindow = mainWindow;
        mainWindow.Show();
        ApplySecondaryScreenVisibility();

        if (composition.IsFormalCentralMode)
        {
            composition.StartCentralCoordinator();
        }
        else
        {
            ShowLocalCatalogNotices(mainWindow, composition);
        }
    }

    private static string GetCatalogStartupError(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            "设备目录目录权限不足，请确认安装器已授予应用目录所需的修改权限。",
        IOException => "设备目录文件无法访问，应用将退出。",
        InvalidDataException => exception.Message,
        NotSupportedException => exception.Message,
        _ => "设备目录加载失败，应用将退出。"
    };

    private static void ShowLocalCatalogNotices(
        MainWindow mainWindow,
        ApplicationCatalogComposition composition)
    {
        if (composition.CatalogMigrationWarning is not null)
        {
            WpfMessageBox.Show(
                mainWindow,
                composition.CatalogMigrationWarning,
                "迁移提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (composition.CatalogRecoveryOccurred)
        {
            WpfMessageBox.Show(
                mainWindow,
                "设备配置文件损坏，系统已从最近的有效备份恢复。\n原损坏文件已保留用于排查。",
                "设备目录恢复提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
