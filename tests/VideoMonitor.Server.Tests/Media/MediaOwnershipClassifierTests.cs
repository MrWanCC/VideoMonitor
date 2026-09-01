using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaOwnershipClassifierTests
{
    private static readonly MediaStreamKey Key = new(
        Guid.Parse("73000000-0000-0000-0000-000000000001"),
        Guid.Parse("74000000-0000-0000-0000-000000000001"),
        StreamType.Main);

    [Fact]
    public void RestartAdoptionRequiresAllProof()
    {
        var classifier = new MediaOwnershipClassifier(
            "configured-vhost",
            "videomonitor",
            requestedKey => requestedKey == Key);

        var result = classifier.Classify(
            Evidence(),
            Key,
            SourceBindingResult.Matched,
            currentProcessOwnsProxy: false);

        Assert.Equal(StreamOwnership.OwnedAdopted, result);
    }

    [Fact]
    public void MissingEvidenceIsNotOwned()
    {
        var classifier = new MediaOwnershipClassifier(
            "configured-vhost",
            "videomonitor",
            requestedKey => requestedKey == Key);

        var result = classifier.Classify(
            Evidence() with
            {
                OriginType = null,
                OriginTypeStr = null,
                OriginUrl = null
            },
            Key,
            SourceBindingResult.InsufficientEvidence,
            currentProcessOwnsProxy: false);

        Assert.Equal(StreamOwnership.NotOwned, result);
    }

    [Fact]
    public void CurrentProcessRetainsProxyKey()
    {
        var classifier = new MediaOwnershipClassifier(
            "configured-vhost",
            "videomonitor",
            requestedKey => requestedKey == Key);

        var result = classifier.Classify(
            Evidence(),
            Key,
            SourceBindingResult.Matched,
            currentProcessOwnsProxy: true);

        Assert.Equal(StreamOwnership.OwnedCurrentProcess, result);
    }

    private static ZlmMediaEvidence Evidence() => new(
        "rtsp",
        "configured-vhost",
        "videomonitor",
        Key.ToFormalStreamId(),
        4,
        "rtsp_pull",
        "rtsp://camera.example/live",
        1,
        1,
        0);
}
