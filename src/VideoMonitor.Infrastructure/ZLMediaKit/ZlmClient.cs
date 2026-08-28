using System.Net;
using System.Text.Json;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed class ZlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly ZlmOptions options;

    public ZlmClient(HttpClient httpClient, ZlmOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ZlmApiResponse<JsonElement>> CheckServerAsync(CancellationToken cancellationToken) =>
        GetAsync<JsonElement>(
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
            "addStreamProxy",
            new Dictionary<string, string?>
            {
                ["vhost"] = options.Vhost,
                ["app"] = options.App,
                ["stream"] = streamId,
                ["url"] = sourceUri.ToString(),
                ["rtp_type"] = "0",
                ["timeout_sec"] = "5",
                ["retry_count"] = "1"
            },
            cancellationToken);
    }

    public async Task<ZlmApiResponse<IReadOnlyList<ZlmStreamInfo>>> GetMediaListAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        var response = await GetAsync<List<ZlmStreamInfo>>(
            "getMediaList",
            new Dictionary<string, string?>
            {
                ["schema"] = "rtsp",
                ["vhost"] = options.Vhost,
                ["app"] = options.App,
                ["stream"] = streamId
            },
            cancellationToken).ConfigureAwait(false);

        return new ZlmApiResponse<IReadOnlyList<ZlmStreamInfo>>(
            response.IsSuccess,
            response.Code,
            response.Message,
            response.Data ?? [],
            response.HttpStatusCode);
    }

    public Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
        string proxyKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyKey);
        return GetAsync<ZlmDeleteStreamProxyData>(
            "delStreamProxy",
            new Dictionary<string, string?> { ["key"] = proxyKey },
            cancellationToken);
    }

    private async Task<ZlmApiResponse<T>> GetAsync<T>(
        string endpoint,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                BuildRequestUri(endpoint, values),
                cancellationToken).ConfigureAwait(false);

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
                cancellationToken).ConfigureAwait(false);

            if (envelope is null)
            {
                return Failure<T>(-1, "ZLMediaKit返回了空响应。");
            }

            if (envelope.Code != 0)
            {
                return Failure<T>(
                    envelope.Code,
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "ZLMediaKit API调用失败。"
                        : envelope.Message);
            }

            var data = DeserializeData<T>(envelope.Data);
            return new ZlmApiResponse<T>(
                true,
                envelope.Code,
                envelope.Message ?? string.Empty,
                data,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<T>(-1, "ZLMediaKit请求超时。");
        }
        catch (HttpRequestException exception)
        {
            return Failure<T>(-1, $"无法连接ZLMediaKit：{exception.GetType().Name}");
        }
        catch (JsonException)
        {
            return Failure<T>(-1, "ZLMediaKit响应格式无效。");
        }
    }

    private Uri BuildRequestUri(
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

    private static ZlmApiResponse<T> Failure<T>(
        int code,
        string message,
        HttpStatusCode? statusCode = null) =>
        new(false, code, message, default, statusCode is null ? null : (int)statusCode.Value);
}
