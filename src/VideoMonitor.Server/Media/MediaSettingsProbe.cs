using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.Security;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Server.Media;

public sealed class MediaSettingsProbe : IMediaSettingsProbe
{
    private readonly IMediaSettingsRepository repository;
    private readonly ISecretProtector protector;
    private readonly Func<HttpMessageHandler> handlerFactory;

    public MediaSettingsProbe(
        IMediaSettingsRepository repository,
        ISecretProtector protector)
        : this(repository, protector, static () => new SocketsHttpHandler())
    {
    }

    public MediaSettingsProbe(
        IMediaSettingsRepository repository,
        ISecretProtector protector,
        Func<HttpMessageHandler> handlerFactory)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
        this.handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
    }

    public async Task<MediaSettingsTestResult> TestAsync(
        TestMediaSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidZlmApiBaseUrl(request.ZlmApiBaseUrl))
        {
            return Failure("INVALID_ZLM_API_BASE_URL");
        }

        if (!IsValidPlaybackBaseUrl(request.PlaybackBaseUrl))
        {
            return Failure("INVALID_PLAYBACK_BASE_URL");
        }

        var secret = request.ZlmSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            var stored = await repository.ReadStorageAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(stored.ZlmSecretCiphertext))
            {
                return Failure("ZLM_SECRET_REQUIRED");
            }

            secret = await protector.UnprotectAsync(
                    stored.ZlmSecretCiphertext,
                    SqliteMediaRuntimeSettingsProvider.MediaSecretPurpose,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var httpClient = new HttpClient(handlerFactory(), disposeHandler: true);
        var client = new ZlmClient(
            httpClient,
            new ZlmOptions
            {
                BaseUrl = request.ZlmApiBaseUrl,
                Secret = secret!,
                Vhost = request.Vhost,
                App = request.FormalApp
            });

        var response = await client.CheckServerAsync(cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccess
            ? new MediaSettingsTestResult(true, null)
            : Failure(MapFailure(response));
    }

    private static bool IsValidZlmApiBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);

    private static bool IsValidPlaybackBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static string MapFailure<T>(ZlmApiResponse<T> response) =>
        response.Code is 400 or 401 or 403
            ? "AuthFailed"
            : "MediaServerUnavailable";

    private static MediaSettingsTestResult Failure(string failureCode) =>
        new(false, failureCode);
}
