using System.Collections.Generic;
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
    private const string ScopeInvalidCode = "MEDIA_STREAM_SCOPE_INVALID";
    private const string CleanupFailedCode = "MEDIA_STREAM_CLEANUP_FAILED";

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

        if (!string.Equals(request.Vhost, settings.Vhost, StringComparison.Ordinal)
            || !string.Equals(request.App, settings.FormalApp, StringComparison.Ordinal))
        {
            return Fail(key, MediaServerHealth.Healthy, SourceObservation.Unknown, ScopeInvalidCode);
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
                var cleanupSucceeded = await TryDeleteCurrentProxyAsync(added.Data.Key, cancellationToken)
                    .ConfigureAwait(false);
                var result = Fail(
                    key,
                    MediaServerHealth.Healthy,
                    SourceObservation.ConnectFailed,
                    cleanupSucceeded ? IdentityConflictCode : CleanupFailedCode);
                if (cleanupSucceeded)
                {
                    runtimeRegistry.MarkIdle(key, utcNow());
                }

                return result;
            }

            runtimeRegistry.MarkReady(
                key,
                StreamOwnership.OwnedCurrentProcess,
                registered.TotalReaderCount,
                utcNow());
            return Success(request, key);
        }

        var timeoutCleanupSucceeded = await TryDeleteCurrentProxyAsync(added.Data.Key, cancellationToken)
            .ConfigureAwait(false);
        var timeoutResult = Fail(
            key,
            MediaServerHealth.Healthy,
            SourceObservation.ConnectFailed,
            timeoutCleanupSucceeded ? NotRegisteredCode : CleanupFailedCode);
        if (timeoutCleanupSucceeded)
        {
            runtimeRegistry.MarkIdle(key, utcNow());
        }

        return timeoutResult;
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
            var deleted = await TryDeleteCurrentProxyAsync(proxyKey, cancellationToken)
                .ConfigureAwait(false);
            if (deleted)
            {
                runtimeRegistry.MarkIdle(key, utcNow());
            }
            else
            {
                MarkCleanupFailed(key);
            }
        }
        else if (ownership == StreamOwnership.OwnedAdopted)
        {
            var closed = await TryCloseExactStreamAsync(
                    evidence.Schema,
                    evidence.Vhost,
                    evidence.App,
                    evidence.Stream,
                    cancellationToken)
                .ConfigureAwait(false);
            if (closed)
            {
                runtimeRegistry.MarkIdle(key, utcNow());
            }
            else
            {
                MarkCleanupFailed(key);
            }
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
        var observedKeys = new HashSet<MediaStreamKey>();
        var cleanupCandidates = new List<MediaStreamKey>();
        var observedAtUtc = utcNow();
        var noReaderGrace = TimeSpan.FromSeconds(Math.Max(0, settings.NoReaderGraceSeconds));
        foreach (var evidence in response.Data ?? Array.Empty<ZlmMediaEvidence>())
        {
            if (!MediaStreamIdGenerator.TryParseFormal(evidence.Stream, out var key)
                || !string.Equals(evidence.Schema, "rtsp", StringComparison.Ordinal)
                || !string.Equals(evidence.Vhost, settings.Vhost, StringComparison.Ordinal)
                || !string.Equals(evidence.App, settings.FormalApp, StringComparison.Ordinal))
            {
                continue;
            }

            observedKeys.Add(key);

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
                        observedAtUtc);
                }
                else if (ownership == StreamOwnership.OwnedCurrentProcess)
                {
                    runtimeRegistry.MarkReady(
                        key,
                        ownership,
                        evidence.TotalReaderCount,
                        observedAtUtc);
                }

                if (ownership is StreamOwnership.OwnedAdopted or StreamOwnership.OwnedCurrentProcess)
                {
                    if (evidence.TotalReaderCount == 0)
                    {
                        var noReaderSince = runtimeRegistry.MarkNoReaderSince(
                            key,
                            observedAtUtc);
                        if (observedAtUtc - noReaderSince >= noReaderGrace)
                        {
                            cleanupCandidates.Add(key);
                        }
                    }
                    else
                    {
                        runtimeRegistry.ClearNoReaderSince(key);
                    }
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

        foreach (var runtime in runtimeRegistry.GetSnapshot().Streams)
        {
            if (runtime.RuntimeState == StreamRuntimeState.Ready
                && !observedKeys.Contains(runtime.Key))
            {
                runtimeRegistry.MarkIdle(runtime.Key, observedAtUtc);
            }
        }

        foreach (var key in cleanupCandidates.Distinct())
        {
            await CleanupOwnedStreamIfEligibleAsync(key, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task<bool> TryDeleteCurrentProxyAsync(
        string proxyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await gateway.DeleteStreamProxyAsync(proxyKey, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccess && response.Data?.Flag == true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryCloseExactStreamAsync(
        string schema,
        string vhost,
        string app,
        string stream,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await gateway.CloseExactStreamAsync(
                    schema,
                    vhost,
                    app,
                    stream,
                    cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void MarkCleanupFailed(MediaStreamKey key) =>
        runtimeRegistry.MarkFaulted(
            key,
            SourceObservation.Unknown,
            CleanupFailedCode,
            CleanupFailedCode,
            utcNow());

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
