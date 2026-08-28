using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Hikvision;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Wpf.Playback;

/// <summary>
/// Direct ZLM control is intentionally limited to the single-machine validation phase.
/// Production clients will replace this provider with a Server-backed implementation and
/// will not hold camera credentials or the ZLM API secret.
/// </summary>
public sealed class LocalZlmPlaybackSourceProvider : IPlaybackSourceProvider
{
    private readonly ZlmClient zlmClient;
    private readonly ZlmOptions zlmOptions;
    private readonly LocalDeviceOptions localDevice;
    private readonly TimeSpan registrationTimeout;
    private readonly TimeSpan pollInterval;

    public LocalZlmPlaybackSourceProvider(
        ZlmClient zlmClient,
        ZlmOptions zlmOptions,
        LocalDeviceOptions localDevice,
        TimeSpan registrationTimeout,
        TimeSpan pollInterval)
    {
        this.zlmClient = zlmClient ?? throw new ArgumentNullException(nameof(zlmClient));
        this.zlmOptions = zlmOptions ?? throw new ArgumentNullException(nameof(zlmOptions));
        this.localDevice = localDevice ?? throw new ArgumentNullException(nameof(localDevice));
        this.registrationTimeout = registrationTimeout >= TimeSpan.Zero
            ? registrationTimeout
            : throw new ArgumentOutOfRangeException(nameof(registrationTimeout));
        this.pollInterval = pollInterval >= TimeSpan.Zero
            ? pollInterval
            : throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public async Task<PlaybackSource> PrepareAsync(
        CameraDevice device,
        CameraChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(channel);

        var runtimeDevice = CreateRuntimeDevice(device);
        var runtimeChannel = CreateRuntimeChannel(channel, runtimeDevice.Id);
        var streamId = StreamIdGenerator.Generate(runtimeDevice, runtimeChannel);
        var cameraUri = HikvisionRtspUrlBuilder.Build(runtimeDevice, runtimeChannel);

        var server = await zlmClient.CheckServerAsync(cancellationToken).ConfigureAwait(false);
        if (!server.IsSuccess)
        {
            throw new PlaybackSourceException(
                PlaybackFailureStage.ZlmUnavailable,
                "ZLMediaKit不可连接",
                server.Message);
        }

        var existing = await zlmClient
            .GetMediaListAsync(streamId, cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            throw new PlaybackSourceException(
                PlaybackFailureStage.ZlmUnavailable,
                "ZLMediaKit不可连接",
                existing.Message);
        }

        if (ContainsTargetStream(existing.Data, streamId))
        {
            return CreateSource(channel.Id, streamId, null, ownsProxy: false);
        }

        var add = await zlmClient
            .AddStreamProxyAsync(streamId, cameraUri, cancellationToken)
            .ConfigureAwait(false);
        if (!add.IsSuccess || string.IsNullOrWhiteSpace(add.Data?.Key))
        {
            throw new PlaybackSourceException(
                PlaybackFailureStage.ZlmProxyRegistrationFailed,
                "ZLMediaKit拉流失败",
                add.Message);
        }

        var proxyKey = add.Data.Key;
        try
        {
            if (await WaitForStreamAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                return CreateSource(channel.Id, streamId, proxyKey, ownsProxy: true);
            }
        }
        catch (PlaybackSourceException)
        {
            await TryDeleteOwnedProxyAsync(proxyKey, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await TryDeleteOwnedProxyAsync(proxyKey, cancellationToken).ConfigureAwait(false);
        throw new PlaybackSourceException(
            PlaybackFailureStage.ZlmStreamRegistrationTimeout,
            "拉流失败",
            "摄像头RTSP连接超时");
    }

    public async Task ReleaseAsync(
        PlaybackSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.OwnsProxy || string.IsNullOrWhiteSpace(source.ProxyKey))
        {
            return;
        }

        var result = await zlmClient
            .DeleteStreamProxyAsync(source.ProxyKey, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data?.Flag != true)
        {
            throw new PlaybackSourceException(
                PlaybackFailureStage.ZlmProxyReleaseFailed,
                "ZLMediaKit代理释放失败",
                result.Message);
        }
    }

    private async Task<bool> WaitForStreamAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + registrationTimeout;
        do
        {
            var media = await zlmClient
                .GetMediaListAsync(streamId, cancellationToken)
                .ConfigureAwait(false);
            if (!media.IsSuccess)
            {
                throw new PlaybackSourceException(
                    PlaybackFailureStage.ZlmProxyRegistrationFailed,
                    "ZLMediaKit注册流失败",
                    media.Message);
            }

            if (media.IsSuccess && ContainsTargetStream(media.Data, streamId))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
        while (true);
    }

    private async Task TryDeleteOwnedProxyAsync(
        string proxyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await zlmClient
                .DeleteStreamProxyAsync(proxyKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The original registration timeout remains the user-facing failure.
        }
    }

    private CameraDevice CreateRuntimeDevice(CameraDevice source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        GroupId = source.GroupId,
        IpAddress = localDevice.IpAddress,
        SdkPort = source.SdkPort,
        RtspPort = localDevice.RtspPort,
        Username = localDevice.Username,
        Password = localDevice.Password,
        Manufacturer = source.Manufacturer,
        Model = source.Model,
        Status = source.Status,
        Enabled = source.Enabled,
        Remark = source.Remark
    };

    private CameraChannel CreateRuntimeChannel(CameraChannel source, Guid deviceId) => new()
    {
        Id = source.Id,
        DeviceId = deviceId,
        ChannelNo = localDevice.ChannelNo,
        ChannelName = source.ChannelName,
        StreamType = localDevice.StreamType,
        Enabled = source.Enabled
    };

    private PlaybackSource CreateSource(
        Guid channelId,
        string streamId,
        string? proxyKey,
        bool ownsProxy) => new(
        channelId,
        streamId,
        new UriBuilder(
            "rtsp",
            zlmOptions.RtspHost,
            zlmOptions.RtspPort,
            $"{zlmOptions.App.Trim('/')}/{streamId}").Uri,
        proxyKey,
        ownsProxy);

    private bool ContainsTargetStream(
        IReadOnlyList<ZlmStreamInfo>? streams,
        string streamId) =>
        streams?.Any(stream =>
            string.Equals(stream.Schema, "rtsp", StringComparison.OrdinalIgnoreCase)
            && string.Equals(stream.Vhost, zlmOptions.Vhost, StringComparison.Ordinal)
            && string.Equals(stream.App, zlmOptions.App, StringComparison.Ordinal)
            && string.Equals(stream.Stream, streamId, StringComparison.Ordinal)) == true;

}
