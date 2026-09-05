using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MediaDiagnosticsStreamRowViewModel : ObservableObject
{
    private string deviceName = "未知设备";
    private string channelName = "未知通道";
    private int channelNo;
    private StreamRuntimeState runtimeState;
    private int viewerCount;
    private StreamOwnership ownership;
    private SourceObservation sourceObservation;
    private DateTimeOffset? startedAtUtc;
    private DateTimeOffset? observedAtUtc;
    private DateTimeOffset? lastSuccessUtc;
    private string? safeLastErrorCode;
    private string? safeLastErrorMessage;
    private bool isStale;

    public MediaDiagnosticsStreamRowViewModel(MediaStreamKey key)
    {
        Key = key;
    }

    public MediaStreamKey Key { get; }

    public Guid DeviceId => Key.DeviceId;

    public Guid ChannelId => Key.ChannelId;

    public StreamType StreamType => Key.StreamType;

    public string StreamTypeText => StreamType switch
    {
        StreamType.Main => "主码流",
        StreamType.Sub => "子码流",
        _ => StreamType.ToString()
    };

    public string DeviceName
    {
        get => deviceName;
        private set => SetProperty(ref deviceName, value);
    }

    public string ChannelName
    {
        get => channelName;
        private set => SetProperty(ref channelName, value);
    }

    public int ChannelNo
    {
        get => channelNo;
        private set => SetProperty(ref channelNo, value);
    }

    public StreamRuntimeState RuntimeState
    {
        get => runtimeState;
        private set
        {
            if (SetProperty(ref runtimeState, value))
            {
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    public int ViewerCount
    {
        get => viewerCount;
        private set => SetProperty(ref viewerCount, value);
    }

    public StreamOwnership Ownership
    {
        get => ownership;
        private set => SetProperty(ref ownership, value);
    }

    public SourceObservation SourceObservation
    {
        get => sourceObservation;
        private set => SetProperty(ref sourceObservation, value);
    }

    public DateTimeOffset? StartedAtUtc
    {
        get => startedAtUtc;
        private set => SetProperty(ref startedAtUtc, value);
    }

    public DateTimeOffset? ObservedAtUtc
    {
        get => observedAtUtc;
        private set => SetProperty(ref observedAtUtc, value);
    }

    public DateTimeOffset? LastSuccessUtc
    {
        get => lastSuccessUtc;
        private set => SetProperty(ref lastSuccessUtc, value);
    }

    public string? SafeLastErrorCode
    {
        get => safeLastErrorCode;
        private set => SetProperty(ref safeLastErrorCode, value);
    }

    public string? SafeLastErrorMessage
    {
        get => safeLastErrorMessage;
        private set => SetProperty(ref safeLastErrorMessage, value);
    }

    public bool IsStale
    {
        get => isStale;
        private set => SetProperty(ref isStale, value);
    }

    public bool CanRetry => RuntimeState == StreamRuntimeState.Faulted;

    internal void Apply(
        MediaStreamDiagnosticsDto snapshot,
        string nextDeviceName,
        string nextChannelName,
        int nextChannelNo)
    {
        DeviceName = nextDeviceName;
        ChannelName = nextChannelName;
        ChannelNo = nextChannelNo;
        RuntimeState = snapshot.RuntimeState;
        ViewerCount = snapshot.ViewerCount;
        Ownership = snapshot.Ownership;
        SourceObservation = snapshot.SourceObservation;
        StartedAtUtc = snapshot.StartedAtUtc;
        ObservedAtUtc = snapshot.ObservedAtUtc;
        LastSuccessUtc = snapshot.LastSuccessUtc;
        SafeLastErrorCode = snapshot.SafeLastErrorCode;
        SafeLastErrorMessage = snapshot.SafeLastErrorMessage;
        IsStale = snapshot.IsStale;
    }
}
