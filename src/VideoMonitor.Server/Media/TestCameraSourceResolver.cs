using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Infrastructure.Hikvision;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Media;

public sealed class TestCameraSourceResolver : ITestCameraSourceResolver
{
    private readonly ICameraMediaCredentialReader credentialReader;
    private readonly ICentralCatalogRepository catalogRepository;

    public TestCameraSourceResolver(
        ICameraMediaCredentialReader credentialReader,
        ICentralCatalogRepository catalogRepository)
    {
        this.credentialReader = credentialReader
            ?? throw new ArgumentNullException(nameof(credentialReader));
        this.catalogRepository = catalogRepository
            ?? throw new ArgumentNullException(nameof(catalogRepository));
    }

    public async Task<ResolvedTestCameraSource> ResolveAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Draft);

        var draft = request.Draft;
        ValidateDraft(draft);
        if (request.ExistingDeviceId is null != request.ExistingChannelId is null)
        {
            throw new TestStreamOperationException(
                TestStreamErrorCode.InvalidDraft,
                "测试视频请求无效。");
        }

        var password = draft.Password ?? string.Empty;
        if (request.ExistingDeviceId is { } deviceId
            && request.ExistingChannelId is { } channelId)
        {
            var catalogDevice = await catalogRepository
                .GetDeviceAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            if (catalogDevice is null
                || catalogDevice.Id != deviceId
                || catalogDevice.Channels.All(channel =>
                    channel.Id != channelId || channel.DeviceId != deviceId))
            {
                throw new TestStreamOperationException(
                    TestStreamErrorCode.CatalogUnavailable,
                    "设备媒体身份无效。");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                var credential = await credentialReader
                    .ReadAsync(deviceId, channelId, cancellationToken)
                    .ConfigureAwait(false);
                if (credential.DeviceId != deviceId || credential.ChannelId != channelId)
                {
                    throw new TestStreamOperationException(
                        TestStreamErrorCode.CatalogUnavailable,
                        "设备媒体身份无效。");
                }

                password = credential.Password;
            }
        }

        var device = new CameraDevice
        {
            Id = request.ExistingDeviceId ?? Guid.Empty,
            IpAddress = draft.IpAddress,
            RtspPort = draft.RtspPort,
            Username = draft.Username,
            Password = password,
            TransportMode = draft.TransportMode
        };
        var channel = new CameraChannel
        {
            Id = request.ExistingChannelId ?? Guid.Empty,
            DeviceId = request.ExistingDeviceId ?? Guid.Empty,
            ChannelNo = draft.ChannelNo,
            StreamType = draft.StreamType
        };

        return new ResolvedTestCameraSource(
            HikvisionRtspUrlBuilder.Build(device, channel),
            request.ExistingDeviceId,
            request.ExistingChannelId,
            draft.ChannelNo,
            draft.StreamType);
    }

    private static void ValidateDraft(CameraDeviceDraftDto draft)
    {
        if (string.IsNullOrWhiteSpace(draft.IpAddress)
            || draft.RtspPort is < 1 or > 65535
            || string.IsNullOrWhiteSpace(draft.Username)
            || draft.ChannelNo < 1
            || !Enum.IsDefined(draft.StreamType)
            || !Enum.IsDefined(draft.TransportMode))
        {
            throw new TestStreamOperationException(
                TestStreamErrorCode.InvalidDraft,
                "测试视频请求无效。");
        }
    }
}
