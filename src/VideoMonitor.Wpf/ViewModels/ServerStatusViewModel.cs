using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class ServerStatusViewModel : ObservableObject, IDisposable
{
    private readonly ServerConnectionCoordinator coordinator;
    private ServerConnectionState state;
    private string stateText = string.Empty;
    private string lastSuccessfulSyncText = "--";
    private bool isStale;
    private Uri? baseUri;
    private bool disposed;

    public ServerStatusViewModel(ServerConnectionCoordinator coordinator)
    {
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        coordinator.StatusChanged += OnStatusChanged;
        ApplyStatus(coordinator.Status);
    }

    public ServerConnectionState State
    {
        get => state;
        private set => SetProperty(ref state, value);
    }

    public string StateText
    {
        get => stateText;
        private set => SetProperty(ref stateText, value);
    }

    public string LastSuccessfulSyncText
    {
        get => lastSuccessfulSyncText;
        private set => SetProperty(ref lastSuccessfulSyncText, value);
    }

    public bool IsStale
    {
        get => isStale;
        private set => SetProperty(ref isStale, value);
    }

    public Uri? BaseUri
    {
        get => baseUri;
        private set => SetProperty(ref baseUri, value);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        coordinator.StatusChanged -= OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        ApplyStatus(coordinator.Status);
    }

    private void ApplyStatus(ServerConnectionStatus status)
    {
        State = status.State;
        StateText = GetStateText(status.State);
        LastSuccessfulSyncText = status.LastSuccessfulSyncUtc is { } timestamp
            ? timestamp.ToLocalTime().ToString(
                "HH:mm:ss",
                CultureInfo.InvariantCulture)
            : "--";
        IsStale = status.IsStale;
        BaseUri = status.BaseUri;
    }

    private static string GetStateText(ServerConnectionState value) =>
        value switch
        {
            ServerConnectionState.Unconfigured => "未配置",
            ServerConnectionState.Connecting => "连接中",
            ServerConnectionState.Connected => "已连接",
            ServerConnectionState.Unavailable => "连接失败",
            _ => "连接失败"
        };
}
