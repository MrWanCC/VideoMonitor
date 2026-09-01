using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Hikvision;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Media;

public sealed class CameraSourceResolver : ICameraSourceResolver
{
    private readonly ICameraMediaCredentialReader credentialReader;

    public CameraSourceResolver(ICameraMediaCredentialReader credentialReader)
    {
        this.credentialReader = credentialReader
            ?? throw new ArgumentNullException(nameof(credentialReader));
    }

    public async Task<ResolvedCameraSource> ResolveAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default)
    {
        var credential = await credentialReader
            .ReadAsync(key.DeviceId, key.ChannelId, cancellationToken)
            .ConfigureAwait(false);

        if (credential.DeviceId != key.DeviceId
            || credential.ChannelId != key.ChannelId
            || credential.StreamType != key.StreamType)
        {
            throw new InvalidDataException("设备媒体身份无效。");
        }

        var device = new CameraDevice
        {
            Id = credential.DeviceId,
            IpAddress = credential.IpAddress,
            RtspPort = credential.RtspPort,
            Username = credential.Username,
            Password = credential.Password,
            TransportMode = credential.TransportMode
        };
        var channel = new CameraChannel
        {
            Id = credential.ChannelId,
            DeviceId = credential.DeviceId,
            ChannelNo = credential.ChannelNo,
            StreamType = credential.StreamType
        };
        var sourceUri = HikvisionRtspUrlBuilder.Build(device, channel);
        return new ResolvedCameraSource(
            key,
            sourceUri,
            SourceBindingVerifier.Fingerprint(sourceUri));
    }
}
