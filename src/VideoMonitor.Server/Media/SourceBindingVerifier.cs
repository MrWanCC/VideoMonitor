using System.Security.Cryptography;
using System.Text;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public sealed class SourceBindingVerifier
{
    public SourceBindingResult Verify(
        ZlmMediaEvidence? evidence,
        ResolvedCameraSource? source)
    {
        if (evidence is null
            || source is null
            || string.IsNullOrWhiteSpace(evidence.Schema)
            || string.IsNullOrWhiteSpace(evidence.Vhost)
            || string.IsNullOrWhiteSpace(evidence.App)
            || string.IsNullOrWhiteSpace(evidence.Stream)
            || (string.IsNullOrWhiteSpace(evidence.OriginTypeStr)
                && evidence.OriginType is null)
            || string.IsNullOrWhiteSpace(evidence.OriginUrl)
            || string.IsNullOrWhiteSpace(source.SourceBindingFingerprint))
        {
            return SourceBindingResult.InsufficientEvidence;
        }

        if (!string.Equals(
                evidence.Stream,
                source.Key.ToFormalStreamId(),
                StringComparison.Ordinal))
        {
            return SourceBindingResult.Mismatch;
        }

        if (!Uri.TryCreate(evidence.OriginUrl, UriKind.Absolute, out var originUri))
        {
            return SourceBindingResult.InsufficientEvidence;
        }

        return string.Equals(
                Fingerprint(originUri),
                source.SourceBindingFingerprint,
                StringComparison.Ordinal)
            ? SourceBindingResult.Matched
            : SourceBindingResult.Mismatch;
    }

    internal static string Fingerprint(Uri sourceUri) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(sourceUri.ToString())));
}
