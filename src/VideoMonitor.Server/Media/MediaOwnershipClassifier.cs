using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public sealed class MediaOwnershipClassifier
{
    private readonly string configuredVhost;
    private readonly string configuredFormalApp;
    private readonly Func<MediaStreamKey, bool> catalogIdentityExists;

    public MediaOwnershipClassifier(
        string configuredVhost = "__defaultVhost__",
        string configuredFormalApp = "videomonitor",
        Func<MediaStreamKey, bool>? catalogIdentityExists = null)
    {
        this.configuredVhost = configuredVhost ?? throw new ArgumentNullException(nameof(configuredVhost));
        this.configuredFormalApp = configuredFormalApp ?? throw new ArgumentNullException(nameof(configuredFormalApp));
        this.catalogIdentityExists = catalogIdentityExists ?? (_ => true);
    }

    public StreamOwnership Classify(
        ZlmMediaEvidence evidence,
        MediaStreamKey key,
        SourceBindingResult binding,
        bool currentProcessOwnsProxy)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var identityMatches = string.Equals(
                evidence.Vhost,
                configuredVhost,
                StringComparison.Ordinal)
            && string.Equals(
                evidence.App,
                configuredFormalApp,
                StringComparison.Ordinal)
            && MediaStreamIdGenerator.TryParseFormal(evidence.Stream, out var parsedKey)
            && parsedKey == key;

        if (!identityMatches)
        {
            return string.Equals(evidence.App, configuredFormalApp, StringComparison.Ordinal)
                && evidence.Stream.StartsWith("vm_", StringComparison.Ordinal)
                ? StreamOwnership.NotOwned
                : StreamOwnership.External;
        }

        if (!catalogIdentityExists(key)
            || !IsPullOrProxyCompatible(evidence)
            || binding != SourceBindingResult.Matched)
        {
            return StreamOwnership.NotOwned;
        }

        if (currentProcessOwnsProxy)
        {
            return StreamOwnership.OwnedCurrentProcess;
        }

        return StreamOwnership.OwnedAdopted;
    }

    public MediaOwnershipClassifier ForConfiguration(
        string vhost,
        string formalApp) =>
        new(vhost, formalApp, catalogIdentityExists);

    private static bool IsPullOrProxyCompatible(ZlmMediaEvidence evidence)
    {
        if (evidence.OriginType.HasValue)
        {
            return evidence.OriginType is 4 or 7;
        }

        var originType = evidence.OriginTypeStr;
        return !string.IsNullOrWhiteSpace(originType)
            && (originType.Contains("pull", StringComparison.OrdinalIgnoreCase)
                || originType.Contains("proxy", StringComparison.OrdinalIgnoreCase));
    }
}
