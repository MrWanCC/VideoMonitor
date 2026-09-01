using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestCameraSourceResolverTests
{
    private static readonly Guid DeviceId =
        Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("92000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task NewDraftReturnsSourceWithoutFormalIdentity()
    {
        var reader = new RecordingCredentialReader();
        var resolver = new TestCameraSourceResolver(reader);

        var result = await resolver.ResolveAsync(Request(
            null,
            null,
            password: "draft-secret"));

        Assert.Null(result.ExistingDeviceId);
        Assert.Null(result.ExistingChannelId);
        Assert.Equal(1, result.ChannelNo);
        Assert.Equal(StreamType.Main, result.StreamType);
        Assert.Equal(
            "rtsp://admin:draft-secret@10.0.0.5:554/Streaming/Channels/101",
            result.SourceUri.AbsoluteUri);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public async Task ExistingBlankPasswordUsesSavedCredential()
    {
        var reader = new RecordingCredentialReader
        {
            Credential = Credential("saved-secret")
        };
        var resolver = new TestCameraSourceResolver(reader);

        var result = await resolver.ResolveAsync(Request(
            DeviceId,
            ChannelId,
            password: " "));

        Assert.Equal(DeviceId, result.ExistingDeviceId);
        Assert.Equal(ChannelId, result.ExistingChannelId);
        Assert.Contains("admin:saved-secret@", result.SourceUri.AbsoluteUri);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task ExistingNonEmptyPasswordIsTransient()
    {
        var reader = new RecordingCredentialReader
        {
            Credential = Credential("saved-secret")
        };
        var resolver = new TestCameraSourceResolver(reader);

        var result = await resolver.ResolveAsync(Request(
            DeviceId,
            ChannelId,
            password: "transient-secret"));

        Assert.Contains("admin:transient-secret@", result.SourceUri.AbsoluteUri);
        Assert.DoesNotContain("saved-secret", result.SourceUri.AbsoluteUri);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task NewEmptyPasswordIsAllowed()
    {
        var reader = new RecordingCredentialReader();
        var resolver = new TestCameraSourceResolver(reader);

        var result = await resolver.ResolveAsync(Request(
            null,
            null,
            password: string.Empty));

        Assert.Equal("rtsp://admin@10.0.0.5:554/Streaming/Channels/101", result.SourceUri.AbsoluteUri);
        Assert.Equal(0, reader.ReadCalls);
    }

    private static TestStreamStartRequest Request(
        Guid? deviceId,
        Guid? channelId,
        string password) =>
        new(
            deviceId,
            channelId,
            new CameraDeviceDraftDto(
                "10.0.0.5",
                554,
                "admin",
                password,
                1,
                StreamType.Main,
                TransportMode.Auto),
            DateTimeOffset.UtcNow);

    private static CameraMediaCredential Credential(string password) =>
        new(
            DeviceId,
            ChannelId,
            "10.0.0.5",
            554,
            "admin",
            password,
            1,
            StreamType.Main,
            TransportMode.Auto);

    private sealed class RecordingCredentialReader : ICameraMediaCredentialReader
    {
        public CameraMediaCredential? Credential { get; init; }

        public int ReadCalls { get; private set; }

        public Task<CameraMediaCredential> ReadAsync(
            Guid deviceId,
            Guid channelId,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(Credential
                ?? throw new InvalidDataException("credential not configured"));
        }
    }
}
