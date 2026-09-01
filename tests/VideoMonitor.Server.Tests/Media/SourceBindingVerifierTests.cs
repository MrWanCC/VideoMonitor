using System.Security.Cryptography;
using System.Text;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class SourceBindingVerifierTests
{
    [Fact]
    public void ReturnsInsufficientEvidenceWhenOriginOrIdentityEvidenceIsMissing()
    {
        var source = CreateSource();
        var verifier = new SourceBindingVerifier();

        Assert.Equal(
            SourceBindingResult.InsufficientEvidence,
            verifier.Verify(
                new ZlmMediaEvidence(
                    "rtsp",
                    "vhost",
                    "app",
                    source.Key.ToFormalStreamId(),
                    4,
                    "rtsp_pull",
                    null,
                    1,
                    2,
                    0),
                source));
        Assert.Equal(
            SourceBindingResult.InsufficientEvidence,
            verifier.Verify(
                new ZlmMediaEvidence(
                    string.Empty,
                    "vhost",
                    "app",
                    source.Key.ToFormalStreamId(),
                    4,
                    "rtsp_pull",
                    "rtsp://camera/live",
                    1,
                    2,
                    0),
                source));
    }

    [Fact]
    public void MatchingOriginAndIdentityReturnsMatched()
    {
        var source = CreateSource();
        var verifier = new SourceBindingVerifier();
        var evidence = CreateEvidence(source, source.SourceUri.ToString());

        Assert.Equal(SourceBindingResult.Matched, verifier.Verify(evidence, source));
    }

    [Fact]
    public void DifferentOriginOrIdentityReturnsMismatch()
    {
        var source = CreateSource();
        var verifier = new SourceBindingVerifier();

        Assert.Equal(
            SourceBindingResult.Mismatch,
            verifier.Verify(CreateEvidence(source, "rtsp://camera-other/live"), source));

        var identityMismatch = CreateEvidence(source, source.SourceUri.ToString()) with
        {
            Stream = "vm_52000000000000000000000000000001_62000000000000000000000000000001_main"
        };
        Assert.Equal(
            SourceBindingResult.Mismatch,
            verifier.Verify(identityMismatch, source));
    }

    private static ResolvedCameraSource CreateSource()
    {
        var key = new MediaStreamKey(
            Guid.Parse("52000000-0000-0000-0000-000000000001"),
            Guid.Parse("62000000-0000-0000-0000-000000000001"),
            StreamType.Sub);
        var uri = new Uri("rtsp://camera-user:fake-camera-password@camera-host/stream");
        return new ResolvedCameraSource(key, uri, Fingerprint(uri));
    }

    private static ZlmMediaEvidence CreateEvidence(
        ResolvedCameraSource source,
        string originUrl) =>
        new(
            "rtsp",
            "vhost",
            "app",
            source.Key.ToFormalStreamId(),
            4,
            "rtsp_pull",
            originUrl,
            1,
            2,
            0);

    private static string Fingerprint(Uri sourceUri) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(sourceUri.ToString())));
}
