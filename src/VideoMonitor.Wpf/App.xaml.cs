using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using LibVLCSharp.Shared;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
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
        var groups = MockMonitorData.CreateGroups();
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name ==
                (singleCameraEnabled ? "西401溜井" : "备用1")),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitorViewModel = new MonitorViewModel(switchService, groups);
        var deviceData = MockDeviceData.Create();
        var deviceManagementViewModel = new DeviceManagementViewModel(deviceData.Groups, deviceData.Devices);
        var screenService = new ScreenService();
        var mainViewModel = new MainViewModel(
            monitorViewModel,
            deviceManagementViewModel,
            screenService.HasSecondaryScreen);
        var secondaryViewModel = new SecondaryMonitorViewModel(switchService, groups);
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
            && playbackConfiguration?.Device is { } localDevice)
        {
            try
            {
                playbackSelection = SingleCameraPlaybackComposition.SelectDevice(
                    deviceData,
                    localDevice);
                zlmHttpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(7)
                };
                var zlmClient = new ZlmClient(zlmHttpClient, playbackConfiguration.Zlm);
                var provider = new LocalZlmPlaybackSourceProvider(
                    zlmClient,
                    playbackConfiguration.Zlm,
                    localDevice,
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
        }

        async void OnMainWindowClosing(object? sender, CancelEventArgs args)
        {
            if (allowMainWindowClose || playbackCoordinator is null)
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
