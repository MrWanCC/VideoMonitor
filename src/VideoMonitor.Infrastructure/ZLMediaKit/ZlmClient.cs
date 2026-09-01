using System.Net;
using System.Text.Json;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed class ZlmClient : IZlmMediaGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient? localHttpClient;
    private readonly ZlmOptions? localOptions;
    private readonly ZlmServerHttpTransport? formalTransport;
    private readonly IMediaRuntimeSettingsProvider? runtimeSettingsProvider;

    public ZlmClient(HttpClient httpClient, ZlmOptions options)
    {
        localHttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        localOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ZlmClient(
        ZlmServerHttpTransport transport,
        IMediaRuntimeSettingsProvider runtimeSettingsProvider)
    {
        formalTransport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.runtimeSettingsProvider = runtimeSettingsProvider
            ?? throw new ArgumentNullException(nameof(runtimeSettingsProvider));
    }

    public Task<ZlmApiResponse<JsonElement>> CheckServerAsync(
        CancellationToken cancellationToken) =>
        GetAsync<JsonElement>(
            localHttpClient!,
            localOptions!,
            "getServerConfig",
            new Dictionary<string, string?>(),
            cancellationToken);

    public Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
        string streamId,
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(sourceUri);

        return GetAsync<ZlmAddStreamProxyData>(
            localHttpClient!,
            localOptions!,
            "addStreamProxy",
            new Dictionary<string, string?>
            {
                ["vhost"] = localOptions!.Vhost,
                ["app"] = localOptions.App,
                ["stream"] = streamId,
                ["url"] = sourceUri.ToString(),
                ["rtp_type"] = "0",
                ["timeout_sec"] = "5",
                ["retry_count"] = "1",
                ["enable_rtsp"] = "1",
                ["enable_rtmp"] = "0",
                ["enable_hls"] = "0",
                ["enable_hls_fmp4"] = "0",
                ["enable_ts"] = "0",
                ["enable_fmp4"] = "0"
            },
            cancellationToken);
    }

    public async Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
        string vhost,
        string app,
        string? stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vhost);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);

        var response = await ExecuteFormalAsync(
            vhost,
            app,
            cancellationToken,
            (options, client) => GetAsync<List<ZlmStreamInfo>>(
                client,
                options,
                "getMediaList",
                new Dictionary<string, string?>
                {
                    ["schema"] = "rtsp",
                    ["vhost"] = vhost,
                    ["app"] = app,
                    ["stream"] = stream
                },
                cancellationToken,
                sanitizeMessage: true))
            .ConfigureAwait(false);
        return MapMediaListResponse(response);
    }

    public async Task<ZlmApiResponse<IReadOnlyList<ZlmStreamInfo>>> GetMediaListAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        var response = await GetAsync<List<ZlmStreamInfo>>(
                localHttpClient!,
                localOptions!,
                "getMediaList",
                new Dictionary<string, string?>
                {
                    ["schema"] = "rtsp",
                    ["vhost"] = localOptions!.Vhost,
                    ["app"] = localOptions.App,
                    ["stream"] = streamId
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ZlmApiResponse<IReadOnlyList<ZlmStreamInfo>>(
            response.IsSuccess,
            response.Code,
            response.Message,
            response.Data ?? [],
            response.HttpStatusCode);
    }

    public Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
        string vhost,
        string app,
        string stream,
        Uri sourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vhost);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentNullException.ThrowIfNull(sourceUri);

        return ExecuteFormalAsync(
            vhost,
            app,
            cancellationToken,
            (options, client) => GetAsync<ZlmAddStreamProxyData>(
                client,
                options,
                "addStreamProxy",
                new Dictionary<string, string?>
                {
                    ["vhost"] = vhost,
                    ["app"] = app,
                    ["stream"] = stream,
                    ["url"] = sourceUri.ToString(),
                    ["rtp_type"] = "0",
                    ["timeout_sec"] = "5",
                    ["retry_count"] = "1",
                    ["enable_rtsp"] = "1",
                    ["enable_rtmp"] = "0",
                    ["enable_hls"] = "0",
                    ["enable_hls_fmp4"] = "0",
                    ["enable_ts"] = "0",
                    ["enable_fmp4"] = "0"
                },
                cancellationToken,
                sanitizeMessage: true));
    }

    public Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
        string proxyKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyKey);
        if (runtimeSettingsProvider is null)
        {
            return GetAsync<ZlmDeleteStreamProxyData>(
                localHttpClient!,
                localOptions!,
                "delStreamProxy",
                new Dictionary<string, string?> { ["key"] = proxyKey },
                cancellationToken);
        }

        return ExecuteFormalAsync(
            string.Empty,
            string.Empty,
            cancellationToken,
            (options, client) => GetAsync<ZlmDeleteStreamProxyData>(
                client,
                options,
                "delStreamProxy",
                new Dictionary<string, string?> { ["key"] = proxyKey },
                cancellationToken,
                sanitizeMessage: true));
    }

    public Task<ZlmApiResponse<JsonElement>> CloseExactStreamAsync(
        string schema,
        string vhost,
        string app,
        string stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(vhost);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);

        return ExecuteFormalAsync(
            vhost,
            app,
            cancellationToken,
            (options, client) => GetAsync<JsonElement>(
                client,
                options,
                "close_streams",
                new Dictionary<string, string?>
                {
                    ["schema"] = schema,
                    ["vhost"] = vhost,
                    ["app"] = app,
                    ["stream"] = stream
                },
                cancellationToken,
                sanitizeMessage: true));
    }

    private async Task<ZlmApiResponse<T>> ExecuteFormalAsync<T>(
        string vhost,
        string app,
        CancellationToken cancellationToken,
        Func<ZlmOptions, HttpClient, Task<ZlmApiResponse<T>>> operation)
    {
        try
        {
            var settings = await runtimeSettingsProvider!
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
            var options = new ZlmOptions
            {
                BaseUrl = settings.ZlmApiBaseUrl,
                Secret = settings.ZlmSecret,
                Vhost = string.IsNullOrEmpty(vhost) ? settings.Vhost : vhost,
                App = string.IsNullOrEmpty(app) ? settings.FormalApp : app
            };
            return await operation(options, formalTransport!.Client).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return Failure<T>(-1, $"ZLMediaKit请求失败：{exception.GetType().Name}");
        }
        catch (Exception exception)
        {
            return Failure<T>(-1, $"ZLMediaKit请求失败：{exception.GetType().Name}");
        }
    }

    private static async Task<ZlmApiResponse<T>> GetAsync<T>(
        HttpClient httpClient,
        ZlmOptions options,
        string endpoint,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken,
        bool sanitizeMessage = false)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                    BuildRequestUri(options, endpoint, values),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Failure<T>(
                    (int)response.StatusCode,
                    $"ZLMediaKit HTTP请求失败：{(int)response.StatusCode} {response.StatusCode}",
                    response.StatusCode);
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync<ZlmResponseEnvelope>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (envelope is null)
            {
                return Failure<T>(-1, "ZLMediaKit返回了空响应。");
            }

            if (envelope.Code != 0)
            {
                return Failure<T>(
                    envelope.Code,
                    sanitizeMessage || string.IsNullOrWhiteSpace(envelope.Message)
                        ? "ZLMediaKit API调用失败。"
                        : envelope.Message);
            }

            var data = DeserializeData<T>(envelope.Data);
            return new ZlmApiResponse<T>(
                true,
                envelope.Code,
                sanitizeMessage ? string.Empty : envelope.Message ?? string.Empty,
                data,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<T>(-1, "ZLMediaKit请求超时。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return Failure<T>(-1, $"无法连接ZLMediaKit：{exception.GetType().Name}");
        }
        catch (JsonException)
        {
            return Failure<T>(-1, "ZLMediaKit响应格式无效。");
        }
        catch (Exception exception)
        {
            return Failure<T>(-1, $"ZLMediaKit请求失败：{exception.GetType().Name}");
        }
    }

    private static Uri BuildRequestUri(
        ZlmOptions options,
        string endpoint,
        IReadOnlyDictionary<string, string?> values)
    {
        var pairs = new List<KeyValuePair<string, string?>>
        {
            new("secret", options.Secret)
        };
        pairs.AddRange(values);
        var query = string.Join(
            "&",
            pairs
                .Where(pair => pair.Value is not null)
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return new Uri($"{options.BaseUrl.TrimEnd('/')}/index/api/{endpoint}?{query}");
    }

    private static T? DeserializeData<T>(JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        if (typeof(T) == typeof(JsonElement))
        {
            return (T)(object)data.Clone();
        }

        return data.Deserialize<T>(SerializerOptions);
    }

    private static ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>> MapMediaListResponse(
        ZlmApiResponse<List<ZlmStreamInfo>> response)
    {
        var evidence = response.Data is null
            ? Array.Empty<ZlmMediaEvidence>()
            : response.Data.Select(ToEvidence).ToArray();
        return new ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>(
            response.IsSuccess,
            response.Code,
            response.Message,
            evidence,
            response.HttpStatusCode);
    }

    private static ZlmMediaEvidence ToEvidence(ZlmStreamInfo stream) =>
        new(
            stream.Schema,
            stream.Vhost,
            stream.App,
            stream.Stream,
            stream.OriginType,
            stream.OriginTypeStr,
            stream.OriginUrl,
            stream.CreateStamp,
            stream.AliveSecond,
            stream.TotalReaderCount);

    private static ZlmApiResponse<T> Failure<T>(
        int code,
        string message,
        HttpStatusCode? statusCode = null) =>
        new(false, code, message, default, statusCode is null ? null : (int)statusCode.Value);
}
