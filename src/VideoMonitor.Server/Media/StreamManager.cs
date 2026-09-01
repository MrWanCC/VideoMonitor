using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public sealed class StreamManager : IStreamManager, IMediaReconcileContributor
{
    private const string IdentityConflictCode = "MediaStreamIdentityConflict";
    private const string NotRegisteredCode = "MEDIA_STREAM_NOT_REGISTERED";
    private const string SourceResolutionCode = "MEDIA_SOURCE_RESOLUTION_FAILED";
    private const string ServerUnavailableCode = "MEDIA_SERVER_UNAVAILABLE";
    private const string ServerUnconfiguredCode = "MEDIA_SERVER_UNCONFIGURED";

    private readonly IZlmMediaGateway gateway;
    private readonly ICameraSourceResolver sourceResolver;
    private readonly IMediaRuntimeSettingsProvider settingsProvider;
    private readonly MediaRuntimeRegistry runtimeRegistry;
    private readonly MediaStreamGate streamGate;
    private readonly MediaOwnershipClassifier ownershipClassifier;
    private readonly SourceBindingVerifier sourceBindingVerifier;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly int maxRegistrationPolls;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly MediaServerHealthState? reconcilerHealthState;

    public StreamManager(
        IZlmMediaGateway gateway,
        ICameraSourceResolver sourceResolver,
        IMediaRuntimeSettingsProvider settingsProvider,
        MediaRuntimeRegistry runtimeRegistry,
        MediaStreamGate streamGate,
        MediaOwnershipClassifier ownershipClassifier,
        SourceBindingVerifier sourceBindingVerifier,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        int maxRegistrationPolls = 5,
        Func<DateTimeOffset>? utcNow = null,
        MediaServerHealthState? reconcilerHealthState = null)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
        this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
        this.streamGate = streamGate ?? throw new ArgumentNullException(nameof(streamGate));
        this.ownershipClassifier = ownershipClassifier ?? throw new ArgumentNullException(nameof(ownershipClassifier));
        this.sourceBindingVerifier = sourceBindingVerifier ?? throw new ArgumentNullException(nameof(sourceBindingVerifier));
        this.delayAsync = delayAsync ?? Task.Delay;
        this.maxRegistrationPolls = Math.Max(1, maxRegistrationPolls);
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.reconcilerHealthState = reconcilerHealthState;
    }

    public async Task<StreamEnsureResult> EnsureStreamAsync(
        MediaStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetFormalKey(request, out var key))
        {
            return Failure("MEDIA_STREAM_IDENTITY_INVALID");
        }

        await using var lease = await streamGate
            .AcquireAsync(key, cancellationToken)
            .ConfigureAwait(false);
        runtimeRegistry.MarkStarting(key, utcNow());

        MediaRuntimeSettings settings;
        try
        {
            settings = await settingsProvider
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(key, MediaServerHealth.ConfigurationError, SourceObservation.Unknown, ServerUnavailableCode);
        }

        if (string.IsNullOrWhiteSpace(settings.ZlmApiBaseUrl))
        {
            return Fail(key, MediaServerHealth.Unconfigured, SourceObservation.Unknown, ServerUnconfiguredCode);
        }

        SetServerHealth(MediaServerHealth.Healthy);

        ResolvedCameraSource resolvedSource;
        try
        {
            resolvedSource = await sourceResolver
                .ResolveAsync(key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(key, MediaServerHealth.Healthy, SourceObservation.ConnectFailed, SourceResolutionCode);
        }

        var initialQuery = await gateway.GetMediaListAsync(
                request.Vhost,
                request.App,
                request.Stream,
                cancellationToken)
            .ConfigureAwait(false);
        if (!initialQuery.IsSuccess)
        {
            return Fail(key, MediaServerHealth.Unavailable, SourceObservation.ConnectFailed, ServerUnavailableCode);
        }

        var existing = FindExact(initialQuery.Data, request);
        if (existing is not null)
        {
            var classifier = ownershipClassifier.ForConfiguration(
                request.Vhost,
                request.App);
            var existingResult = TryReuseExisting(
                key,
                request,
                resolvedSource,
                existing,
                classifier);
            if (existingResult is not null)
            {
                return existingResult;
            }
        }

        var added = await gateway.AddStreamProxyAsync(
                request.Vhost,
                request.App,
                request.Stream,
                resolvedSource.SourceUri,
                cancellationToken)
            .ConfigureAwait(false);
        if (!added.IsSuccess || added.Data is null || string.IsNullOrWhiteSpace(added.Data.Key))
        {
            return Fail(key, MediaServerHealth.Healthy, SourceObservation.ConnectFailed, "MEDIA_STREAM_CREATE_FAILED");
        }

        runtimeRegistry.RememberCurrentProxy(key, added.Data.Key);
        for (var attempt = 0; attempt < maxRegistrationPolls; attempt++)
        {
            if (attempt > 0)
            {
                await delayAsync(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
            }

            var registrationQuery = await gateway.GetMediaListAsync(
                    request.Vhost,
                    request.App,
                    request.Stream,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!registrationQuery.IsSuccess)
            {
                break;
            }

            var registered = FindExact(registrationQuery.Data, request);
            if (registered is null)
            {
                continue;
            }

            var binding = sourceBindingVerifier.Verify(registered, resolvedSource);
            if (binding != SourceBindingResult.Matched)
            {
                await DeleteCurrentProxyQuietlyAsync(added.Data.Key, cancellationToken)
                    .ConfigureAwait(false);
                return Fail(
                    key,
                    MediaServerHealth.Healthy,
                    SourceObservation.ConnectFailed,
                    IdentityConflictCode);
            }

            runtimeRegistry.MarkReady(
                key,
                StreamOwnership.OwnedCurrentProcess,
                registered.TotalReaderCount,
                utcNow());
            return Success(request, key);
        }

        await DeleteCurrentProxyQuietlyAsync(added.Data.Key, cancellationToken)
            .ConfigureAwait(false);
        return Fail(key, MediaServerHealth.Healthy, SourceObservation.ConnectFailed, NotRegisteredCode);
    }

    public async Task CleanupOwnedStreamIfEligibleAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await streamGate
            .AcquireAsync(key, cancellationToken)
            .ConfigureAwait(false);

        MediaRuntimeSettings settings;
        try
        {
            settings = await settingsProvider
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetServerHealth(MediaServerHealth.ConfigurationError);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ZlmApiBaseUrl))
        {
            SetServerHealth(MediaServerHealth.Unconfigured);
            return;
        }

        var stream = MediaStreamIdGenerator.GenerateFormal(key);
        var query = await gateway.GetMediaListAsync(
                settings.Vhost,
                settings.FormalApp,
                stream,
                cancellationToken)
            .ConfigureAwait(false);
        if (!query.IsSuccess)
        {
            SetServerHealth(MediaServerHealth.Unavailable);
            return;
        }

        var evidence = query.Data?.FirstOrDefault(item =>
            string.Equals(item.Schema, "rtsp", StringComparison.Ordinal)
            && string.Equals(item.Vhost, settings.Vhost, StringComparison.Ordinal)
            && string.Equals(item.App, settings.FormalApp, StringComparison.Ordinal)
            && string.Equals(item.Stream, stream, StringComparison.Ordinal));
        if (evidence is null || evidence.TotalReaderCount != 0)
        {
            return;
        }

        ResolvedCameraSource resolvedSource;
        try
        {
            resolvedSource = await sourceResolver
                .ResolveAsync(key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return;
        }

        var binding = sourceBindingVerifier.Verify(evidence, resolvedSource);
        var classifier = ownershipClassifier.ForConfiguration(
            settings.Vhost,
            settings.FormalApp);
        var ownership = classifier.Classify(
            evidence,
            key,
            binding,
            runtimeRegistry.CurrentProcessOwnsProxy(key, out var proxyKey));
        if (ownership == StreamOwnership.OwnedCurrentProcess
            && !string.IsNullOrWhiteSpace(proxyKey))
        {
            await DeleteCurrentProxyQuietlyAsync(proxyKey, cancellationToken)
                .ConfigureAwait(false);
            runtimeRegistry.MarkIdle(key, utcNow());
        }
        else if (ownership == StreamOwnership.OwnedAdopted)
        {
            await gateway.CloseExactStreamAsync(
                    evidence.Schema,
                    evidence.Vhost,
                    evidence.App,
                    evidence.Stream,
                    cancellationToken)
                .ConfigureAwait(false);
            runtimeRegistry.MarkIdle(key, utcNow());
        }
    }

    public MediaRuntimeSnapshot GetSnapshot() => runtimeRegistry.GetSnapshot();

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        MediaRuntimeSettings settings;
        try
        {
            settings = await settingsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetServerHealth(MediaServerHealth.ConfigurationError);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ZlmApiBaseUrl))
        {
            SetServerHealth(MediaServerHealth.Unconfigured);
            return;
        }

        var response = await gateway.GetMediaListAsync(
                settings.Vhost,
                settings.FormalApp,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            SetServerHealth(MediaServerHealth.Unavailable);
            return;
        }

        SetServerHealth(MediaServerHealth.Healthy);
        var classifier = ownershipClassifier.ForConfiguration(
            settings.Vhost,
            settings.FormalApp);
        foreach (var evidence in response.Data ?? Array.Empty<ZlmMediaEvidence>())
        {
            if (!MediaStreamIdGenerator.TryParseFormal(evidence.Stream, out var key)
                || !string.Equals(evidence.Schema, "rtsp", StringComparison.Ordinal)
                || !string.Equals(evidence.Vhost, settings.Vhost, StringComparison.Ordinal)
                || !string.Equals(evidence.App, settings.FormalApp, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var source = await sourceResolver.ResolveAsync(key, cancellationToken)
                    .ConfigureAwait(false);
                var binding = sourceBindingVerifier.Verify(evidence, source);
                var ownership = classifier.Classify(
                    evidence,
                    key,
                    binding,
                    runtimeRegistry.CurrentProcessOwnsProxy(key, out _));
                if (ownership == StreamOwnership.OwnedAdopted)
                {
                    runtimeRegistry.RememberAdopted(key);
                    runtimeRegistry.MarkReady(
                        key,
                        ownership,
                        evidence.TotalReaderCount,
                        utcNow());
                }
                else if (ownership == StreamOwnership.OwnedCurrentProcess)
                {
                    runtimeRegistry.MarkReady(
                        key,
                        ownership,
                        evidence.TotalReaderCount,
                        utcNow());
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // An unprovable stream remains outside the managed runtime state.
            }
        }
    }

    private StreamEnsureResult? TryReuseExisting(
        MediaStreamKey key,
        MediaStreamRequest request,
        ResolvedCameraSource source,
        ZlmMediaEvidence existing,
        MediaOwnershipClassifier classifier)
    {
        var binding = sourceBindingVerifier.Verify(existing, source);
        var currentProcessOwnsProxy = runtimeRegistry.CurrentProcessOwnsProxy(key, out _);
        var ownership = classifier.Classify(
            existing,
            key,
            binding,
            currentProcessOwnsProxy);
        if (ownership is StreamOwnership.OwnedCurrentProcess or StreamOwnership.OwnedAdopted)
        {
            runtimeRegistry.MarkReady(
                key,
                ownership,
                existing.TotalReaderCount,
                utcNow());
            return Success(request, key);
        }

        runtimeRegistry.MarkFaulted(
            key,
            SourceObservation.ConnectFailed,
            IdentityConflictCode,
            "目标媒体身份已被无法安全接管的流占用。",
            utcNow());
        return Failure(IdentityConflictCode);
    }

    private async Task DeleteCurrentProxyQuietlyAsync(
        string proxyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await gateway.DeleteStreamProxyAsync(proxyKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Cleanup failure is reflected by the original safe setup failure.
        }
    }

    private static ZlmMediaEvidence? FindExact(
        IReadOnlyList<ZlmMediaEvidence>? evidence,
        MediaStreamRequest request) =>
        evidence?.FirstOrDefault(item =>
            string.Equals(item.Schema, "rtsp", StringComparison.Ordinal)
            && string.Equals(item.Vhost, request.Vhost, StringComparison.Ordinal)
            && string.Equals(item.App, request.App, StringComparison.Ordinal)
            && string.Equals(item.Stream, request.Stream, StringComparison.Ordinal));

    private static bool TryGetFormalKey(
        MediaStreamRequest request,
        out MediaStreamKey key)
    {
        key = default;
        if (request is null
            || request.Namespace != MediaStreamNamespace.Formal
            || request.CatalogKey is not MediaStreamKey requestedKey
            || !Enum.IsDefined(typeof(VideoMonitor.Core.Models.StreamType), requestedKey.StreamType)
            || requestedKey.ToFormalStreamId() != request.Stream)
        {
            return false;
        }

        key = requestedKey;
        return true;
    }

    private StreamEnsureResult Fail(
        MediaStreamKey key,
        MediaServerHealth health,
        SourceObservation observation,
        string failureCode)
    {
        SetServerHealth(health);
        runtimeRegistry.MarkFaulted(
            key,
            observation,
            failureCode,
            failureCode,
            utcNow());
        return Failure(failureCode);
    }

    private void SetServerHealth(MediaServerHealth health)
    {
        runtimeRegistry.SetServerHealth(health);
        switch (health)
        {
            case MediaServerHealth.Unconfigured:
                reconcilerHealthState?.MarkUnconfigured();
                break;
            case MediaServerHealth.Healthy:
                reconcilerHealthState?.MarkHealthy();
                break;
            case MediaServerHealth.Unavailable:
                reconcilerHealthState?.MarkUnavailable();
                break;
            case MediaServerHealth.ConfigurationError:
                reconcilerHealthState?.MarkConfigurationError();
                break;
        }
    }

    private static StreamEnsureResult Failure(string failureCode) =>
        new(false, null, failureCode);

    private static StreamEnsureResult Success(
        MediaStreamRequest request,
        MediaStreamKey key) =>
        new(
            true,
            new FormalStreamDescriptor(request.Vhost, request.App, request.Stream, key),
            null);
}
