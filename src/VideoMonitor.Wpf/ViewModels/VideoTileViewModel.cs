using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class VideoTileViewModel : ObservableObject
{
    private string cameraName = "未选择摄像头";
    private string groupName = "--";
    private int channelNumber;
    private CameraStatus status = CameraStatus.Offline;
    private string ipAddress = "--";
    private string bitrate = "-- Mbps";
    private string streamType = "--";
    private string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private PlaybackState playbackState = PlaybackState.Placeholder;
    private PlaybackSession? playbackSession;
    private string playbackErrorTitle = string.Empty;
    private string playbackErrorDetail = string.Empty;

    public string IpAddress
    {
        get => ipAddress;
        private set => SetProperty(ref ipAddress, value);
    }

    public string Resolution => "1920×1080";

    public string CameraName
    {
        get => cameraName;
        private set => SetProperty(ref cameraName, value);
    }

    public string GroupName
    {
        get => groupName;
        private set => SetProperty(ref groupName, value);
    }

    public int ChannelNumber
    {
        get => channelNumber;
        private set => SetProperty(ref channelNumber, value);
    }

    public CameraStatus Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string Bitrate
    {
        get => bitrate;
        private set => SetProperty(ref bitrate, value);
    }

    public string StreamType
    {
        get => streamType;
        private set => SetProperty(ref streamType, value);
    }

    public string Timestamp
    {
        get => timestamp;
        private set => SetProperty(ref timestamp, value);
    }

    public PlaybackState PlaybackState
    {
        get => playbackState;
        private set => SetProperty(ref playbackState, value);
    }

    public PlaybackSession? PlaybackSession
    {
        get => playbackSession;
        private set => SetProperty(ref playbackSession, value);
    }

    public string PlaybackErrorTitle
    {
        get => playbackErrorTitle;
        private set => SetProperty(ref playbackErrorTitle, value);
    }

    public string PlaybackErrorDetail
    {
        get => playbackErrorDetail;
        private set => SetProperty(ref playbackErrorDetail, value);
    }

    public void Update(
        CameraInfo camera,
        CameraDevice? device,
        CameraChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(camera);
        CameraName = device?.Name ?? camera.Name;
        GroupName = camera.GroupName;
        ChannelNumber = camera.ChannelNumber;
        Status = device?.Status ?? camera.Status;
        IpAddress = device?.IpAddress ?? "--";
        Bitrate = camera.Bitrate;
        StreamType = channel is null
            ? camera.StreamType
            : channel.StreamType == VideoMonitor.Core.Models.StreamType.Main ? "主码流" : "辅码流";
        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public void ShowLoading()
    {
        PlaybackErrorTitle = string.Empty;
        PlaybackErrorDetail = string.Empty;
        PlaybackState = PlaybackState.Loading;
    }

    public void ShowPlaying(PlaybackSession session)
    {
        PlaybackSession = session ?? throw new ArgumentNullException(nameof(session));
        PlaybackErrorTitle = string.Empty;
        PlaybackErrorDetail = string.Empty;
        PlaybackState = PlaybackState.Playing;
    }

    public void ShowError(string title, string detail)
    {
        PlaybackSession = null;
        PlaybackErrorTitle = title;
        PlaybackErrorDetail = detail;
        PlaybackState = PlaybackState.Error;
    }

    public void ShowPlaceholder()
    {
        PlaybackSession = null;
        PlaybackErrorTitle = string.Empty;
        PlaybackErrorDetail = string.Empty;
        PlaybackState = PlaybackState.Placeholder;
    }
}
