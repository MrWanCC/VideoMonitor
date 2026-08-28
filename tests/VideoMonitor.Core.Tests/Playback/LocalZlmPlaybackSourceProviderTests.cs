using System.Net;
using System.Text.Json;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Tests.Infrastructure;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class LocalZlmPlaybackSourceProviderTests
{
    private static readonly Guid DeviceId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");
    private const string TargetStreamId =
        "device_50000000000000000000000000000001_channel_1_main";

    [Fact]
    public async Task Prepare_WhenStreamAlreadyExists_ReusesWithoutOwnership()
    {
        var handler = Responses(ServerOk(), MediaListWith(TargetStreamId));
        var provider = CreateProvider(handler);

        var source = await provider.PrepareAsync(
            Device(),
            Channel(),
            CancellationToken.None);

        Assert.False(source.OwnsProxy);
        Assert.Null(source.ProxyKey);
        Assert.Equal(ChannelId, source.CameraChannelId);
        Assert.Equal(TargetStreamId, source.StreamId);
        Assert.Equal(
            $"rtsp://192.0.2.10:554/live/{TargetStreamId}",
            source.PlaybackUrl.ToString().TrimEnd('/'));

        await provider.ReleaseAsync(source, CancellationToken.None);

        Assert.DoesNotContain(
            handler.Requests,
            request => request.AbsolutePath.EndsWith("/delStreamProxy"));
    }

    [Fact]
    public async Task Prepare_WhenStreamMissing_OwnsReturnedProxyKeyAndReleasesIt()
    {
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            AddProxy("owned-key"),
            MediaListWith(TargetStreamId),
            DeleteSucceeded());
        var provider = CreateProvider(handler);

        var source = await provider.PrepareAsync(
            Device(),
            Channel(),
            CancellationToken.None);

        Assert.True(source.OwnsProxy);
        Assert.Equal("owned-key", source.ProxyKey);

        await provider.ReleaseAsync(source, CancellationToken.None);

        Assert.Contains(
            handler.Requests,
            request => request.AbsolutePath.EndsWith("/delStreamProxy")
                && request.Query.Contains("key=owned-key"));
    }

    [Fact]
    public async Task Prepare_WhenZlmUnavailable_ReportsSpecificStageWithoutPassword()
    {
        var handler = new StubHttpMessageHandler(
            (HttpStatusCode.ServiceUnavailable, "unavailable"));
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<PlaybackSourceException>(() =>
            provider.PrepareAsync(Device(), Channel(), CancellationToken.None));

        Assert.Equal(PlaybackFailureStage.ZlmUnavailable, exception.Stage);
        Assert.Equal("ZLMediaKit不可连接", exception.Title);
        Assert.DoesNotContain("camera-password", exception.ToString());
    }

    [Fact]
    public async Task Prepare_WhenAddProxyFails_ReportsRegistrationFailure()
    {
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            """{"code":-1,"msg":"proxy failed"}""");
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<PlaybackSourceException>(() =>
            provider.PrepareAsync(Device(), Channel(), CancellationToken.None));

        Assert.Equal(PlaybackFailureStage.ZlmProxyRegistrationFailed, exception.Stage);
        Assert.Equal("ZLMediaKit拉流失败", exception.Title);
        Assert.Contains("proxy failed", exception.Detail);
        Assert.DoesNotContain("camera-password", exception.ToString());
    }

    [Fact]
    public async Task Prepare_WhenCameraRtspTimesOut_ReportsCameraTimeout()
    {
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            """{"code":-1,"msg":"play rtsp timeout"}""");
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<PlaybackSourceException>(() =>
            provider.PrepareAsync(Device(), Channel(), CancellationToken.None));

        Assert.Equal(PlaybackFailureStage.ZlmStreamRegistrationTimeout, exception.Stage);
        Assert.Equal("拉流失败", exception.Title);
        Assert.Equal("摄像头RTSP连接超时", exception.Detail);
    }

    [Fact]
    public async Task Prepare_WhenProxyAlreadyExists_WaitsAndReusesWithoutOwnership()
    {
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            """{"code":-1,"msg":"This stream already exists"}""",
            MediaListWith(TargetStreamId));
        var provider = CreateProvider(handler);

        var source = await provider.PrepareAsync(
            Device(),
            Channel(),
            CancellationToken.None);

        Assert.False(source.OwnsProxy);
        Assert.Null(source.ProxyKey);

        await provider.ReleaseAsync(source, CancellationToken.None);

        Assert.DoesNotContain(
            handler.Requests,
            request => request.AbsolutePath.EndsWith("/delStreamProxy"));
    }

    [Fact]
    public async Task Prepare_WhenMediaNeverRegisters_TimesOutAndCleansOwnedProxy()
    {
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            AddProxy("owned-key"),
            EmptyMediaList(),
            DeleteSucceeded());
        var provider = CreateProvider(
            handler,
            registrationTimeout: TimeSpan.Zero,
            pollInterval: TimeSpan.Zero);

        var exception = await Assert.ThrowsAsync<PlaybackSourceException>(() =>
            provider.PrepareAsync(Device(), Channel(), CancellationToken.None));

        Assert.Equal(PlaybackFailureStage.ZlmStreamRegistrationTimeout, exception.Stage);
        Assert.Equal("拉流失败", exception.Title);
        Assert.Equal("摄像头RTSP连接超时", exception.Detail);
        Assert.Contains(
            handler.Requests,
            request => request.AbsolutePath.EndsWith("/delStreamProxy")
                && request.Query.Contains("key=owned-key"));
    }

    [Fact]
    public async Task Prepare_WhenMediaListFailsAfterAdd_ReportsApiFailureAndCleansOwnedProxy()
    {
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            AddProxy("owned-key"),
            """{"code":-1,"msg":"media list failed"}""",
            DeleteSucceeded());
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<PlaybackSourceException>(() =>
            provider.PrepareAsync(Device(), Channel(), CancellationToken.None));

        Assert.Equal(PlaybackFailureStage.ZlmProxyRegistrationFailed, exception.Stage);
        Assert.Equal("ZLMediaKit注册流失败", exception.Title);
        Assert.Contains("media list failed", exception.Detail);
        Assert.Contains(
            handler.Requests,
            request => request.AbsolutePath.EndsWith("/delStreamProxy")
                && request.Query.Contains("key=owned-key"));
    }

    [Fact]
    public async Task Prepare_WhenCancelledAfterAdd_CleansOwnedProxy()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Responses(
            ServerOk(),
            EmptyMediaList(),
            AddProxy("owned-key"),
            EmptyMediaList(),
            DeleteSucceeded());
        var mediaListRequests = 0;
        handler.RequestObserved = request =>
        {
            if (request.AbsolutePath.EndsWith("/getMediaList")
                && ++mediaListRequests == 2)
            {
                cancellation.Cancel();
            }
        };
        var provider = CreateProvider(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.PrepareAsync(Device(), Channel(), cancellation.Token));

        Assert.Contains(
            handler.Requests,
            request => request.AbsolutePath.EndsWith("/delStreamProxy")
                && request.Query.Contains("key=owned-key"));
    }

    private static LocalZlmPlaybackSourceProvider CreateProvider(
        StubHttpMessageHandler handler,
        TimeSpan? registrationTimeout = null,
        TimeSpan? pollInterval = null)
    {
        var options = new ZlmOptions
        {
            BaseUrl = "http://192.0.2.10",
            Secret = "fake-secret",
            Vhost = "__defaultVhost__",
            App = "live",
            RtspHost = "192.0.2.10",
            RtspPort = 554
        };
        return new LocalZlmPlaybackSourceProvider(
            new ZlmClient(new HttpClient(handler), options),
            options,
            new LocalDeviceOptions
            {
                LocalIdentifier = "camera001",
                IpAddress = "192.0.2.20",
                RtspPort = 554,
                Username = "admin",
                Password = "camera-password",
                ChannelNo = 1,
                StreamType = StreamType.Main
            },
            registrationTimeout ?? TimeSpan.FromSeconds(1),
            pollInterval ?? TimeSpan.Zero);
    }

    private static CameraDevice Device() => new()
    {
        Id = DeviceId,
        Name = "西401溜井 · 通道1",
        IpAddress = "198.51.100.20",
        RtspPort = 554,
        Username = "mock",
        Password = "mock-password"
    };

    private static CameraChannel Channel() => new()
    {
        Id = ChannelId,
        DeviceId = DeviceId,
        ChannelNo = 1,
        StreamType = StreamType.Main
    };

    private static StubHttpMessageHandler Responses(params string[] bodies) => new(bodies);

    private static string ServerOk() => """{"code":0,"data":[]}""";

    private static string EmptyMediaList() => """{"code":0,"data":[]}""";

    private static string AddProxy(string key) =>
        JsonSerializer.Serialize(new { code = 0, data = new { key } });

    private static string DeleteSucceeded() =>
        """{"code":0,"data":{"flag":true}}""";

    private static string MediaListWith(string streamId) =>
        JsonSerializer.Serialize(new
        {
            code = 0,
            data = new[]
            {
                new
                {
                    schema = "rtsp",
                    vhost = "__defaultVhost__",
                    app = "live",
                    stream = streamId
                }
            }
        });
}
