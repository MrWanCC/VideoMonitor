using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class VideoTileViewModel : ObservableObject
{
    private string cameraName = "未选择摄像头";
    private string groupName = "--";
    private int channelNumber;
    private CameraStatus status = CameraStatus.Offline;
    private string bitrate = "-- Mbps";
    private string streamType = "--";

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

    public void Update(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        CameraName = camera.Name;
        GroupName = camera.GroupName;
        ChannelNumber = camera.ChannelNumber;
        Status = camera.Status;
        Bitrate = camera.Bitrate;
        StreamType = camera.StreamType;
    }
}
