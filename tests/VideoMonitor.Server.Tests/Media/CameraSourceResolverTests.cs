using System.Security.Cryptography;
using System.Text;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class CameraSourceResolverTests
{
    [Fact]
    public async Task ResolveUsesCredentialReaderAndRuntimeSettings()
    {
        var deviceId = Guid.Parse("52000000-0000-0000-0000-000000000001");
        var channelId = Guid.Parse("62000000-0000-0000-0000-000000000001");
        var key = new MediaStreamKey(deviceId, channelId, StreamType.Sub);
        var reader = new FakeCredentialReader(new CameraMediaCredential(
            deviceId,
            channelId,
            "192.168.0.22",
            554,
            "camera-user",
            "fake-camera-password",
            2,
            StreamType.Sub,
            TransportMode.Tcp));

        var resolver = new CameraSourceResolver(reader);
        var resolved = await resolver.ResolveAsync(key);

        Assert.Equal(key, resolved.Key);
        Assert.Equal("192.168.0.22", resolved.SourceUri.Host);
        Assert.Equal(554, resolved.SourceUri.Port);
        Assert.Equal("/Streaming/Channels/202", resolved.SourceUri.AbsolutePath);
        Assert.NotEmpty(resolved.SourceBindingFingerprint);
        Assert.DoesNotContain("fake-camera-password", resolved.SourceBindingFingerprint, StringComparison.Ordinal);
        Assert.Equal(key, reader.LastKey);
    }

    [Fact]
    public void PublicCatalogReadRemainsPasswordSafe()
    {
        var devicePropertyNames = typeof(CameraDeviceDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var channelPropertyNames = typeof(CameraChannelDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Password", devicePropertyNames, StringComparer.Ordinal);
        Assert.DoesNotContain("PasswordCiphertext", devicePropertyNames, StringComparer.Ordinal);
        Assert.DoesNotContain("Password", channelPropertyNames, StringComparer.Ordinal);
        Assert.DoesNotContain("PasswordCiphertext", channelPropertyNames, StringComparer.Ordinal);
    }

    private sealed class FakeCredentialReader : ICameraMediaCredentialReader
    {
        private readonly CameraMediaCredential credential;

        public FakeCredentialReader(CameraMediaCredential credential)
        {
            this.credential = credential;
        }

        public MediaStreamKey? LastKey { get; private set; }

        public Task<CameraMediaCredential> ReadAsync(
            Guid deviceId,
            Guid channelId,
            CancellationToken cancellationToken = default)
        {
            LastKey = new MediaStreamKey(deviceId, channelId, credential.StreamType);
            return Task.FromResult(credential);
        }
    }
}
