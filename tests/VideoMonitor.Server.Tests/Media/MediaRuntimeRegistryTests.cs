using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaRuntimeRegistryTests
{
    [Fact]
    public void RuntimeSnapshotContainsNoSecretOrOriginUrl()
    {
        var key = new MediaStreamKey(
            Guid.Parse("77000000-0000-0000-0000-000000000001"),
            Guid.Parse("78000000-0000-0000-0000-000000000001"),
            StreamType.Main);
        var registry = new MediaRuntimeRegistry();
        registry.Record(
            key,
            SourceObservation.Reachable,
            DateTimeOffset.UtcNow,
            "MEDIA_OK",
            "safe");

        var snapshot = registry.GetSnapshot();
        var serialized = JsonSerializer.Serialize(snapshot);
        var propertyNames = typeof(MediaRuntimeSnapshot)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(MediaStreamRuntimeInfo)
                .GetProperties()
                .Select(property => property.Name))
            .ToArray();

        foreach (var forbidden in new[]
                 {
                     "OriginUrl",
                     "SourceUri",
                     "Password",
                     "PasswordCiphertext",
                     "ZlmSecret",
                     "ProxyKey"
                 })
        {
            Assert.DoesNotContain(forbidden, propertyNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
        }
    }
}
