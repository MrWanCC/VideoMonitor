using CommunityToolkit.Mvvm.ComponentModel;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.ViewModels;

public sealed partial class DeviceEditDraftViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private Guid? groupId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RtspPreview))]
    private string ipAddress = string.Empty;

    [ObservableProperty]
    private string sdkPort = "8000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RtspPreview))]
    private string rtspPort = "554";

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string manufacturer = string.Empty;

    [ObservableProperty]
    private string model = string.Empty;

    [ObservableProperty]
    private string remark = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RtspPreview))]
    private string channelNo = "1";

    [ObservableProperty]
    private string channelName = "通道1";

    [ObservableProperty]
    private StreamType streamType = StreamType.Main;

    [ObservableProperty]
    private TransportMode transportMode = TransportMode.Auto;

    public string RtspPreview =>
        $"rtsp://***@{(string.IsNullOrWhiteSpace(IpAddress) ? "--" : IpAddress)}:{RtspPort}/Streaming/Channels/{ChannelNo}01";

    public void ResetForAdd(Guid selectedGroupId)
    {
        Name = string.Empty;
        GroupId = selectedGroupId;
        IpAddress = string.Empty;
        SdkPort = "8000";
        RtspPort = "554";
        Username = string.Empty;
        Password = string.Empty;
        Manufacturer = string.Empty;
        Model = string.Empty;
        Remark = string.Empty;
        ChannelNo = "1";
        ChannelName = "通道1";
        StreamType = StreamType.Main;
        TransportMode = TransportMode.Auto;
    }

    public void Load(CameraDevice device)
    {
        var channel = device.Channels.FirstOrDefault();
        Name = device.Name;
        GroupId = device.GroupId;
        IpAddress = device.IpAddress;
        SdkPort = device.SdkPort.ToString();
        RtspPort = device.RtspPort.ToString();
        Username = device.Username;
        Password = device.Password;
        Manufacturer = device.Manufacturer;
        Model = device.Model;
        Remark = device.Remark;
        ChannelNo = (channel?.ChannelNo ?? 1).ToString();
        ChannelName = channel?.ChannelName ?? "通道1";
        StreamType = channel?.StreamType ?? StreamType.Main;
        TransportMode = device.TransportMode;
    }
}
