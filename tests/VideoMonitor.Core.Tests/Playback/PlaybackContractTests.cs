using VideoMonitor.Core.Media;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class PlaybackContractTests
{
    [Fact]
    public void EnsureRequestContainsOnlyIdsAndStreamType()
    {
        Assert.Equal(
            new[] { "DeviceId", "ChannelId", "StreamType" },
            typeof(EnsurePlaybackStreamRequest)
                .GetProperties()
                .Select(property => property.Name));
    }

    [Fact]
    public void ResponseContainsOnlySafePlaybackFields()
    {
        Assert.Equal(
            new[] { "StreamId", "PlaybackUrl", "ExpiresAtUtc", "RuntimeState" },
            typeof(EnsurePlaybackStreamResponse)
                .GetProperties()
                .Select(property => property.Name));
        var forbidden = new[]
        {
            "Password",
            "PasswordCiphertext",
            "SourceUri",
            "OriginUrl",
            "ZlmSecret",
            "ProxyKey",
            "SigningKey",
            "AdminSecret"
        };

        var contractTypes = new[]
        {
            typeof(EnsurePlaybackStreamRequest),
            typeof(PlaybackMediaIdentity),
            typeof(EnsurePlaybackStreamResponse)
        };
        Assert.All(contractTypes, type =>
            Assert.DoesNotContain(
                type.GetProperties().Select(property => property.Name),
                property => forbidden.Contains(property, StringComparer.Ordinal)));
    }
}
