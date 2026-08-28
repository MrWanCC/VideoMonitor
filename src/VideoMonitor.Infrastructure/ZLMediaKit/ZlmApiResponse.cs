using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed record ZlmApiResponse<T>(
    bool IsSuccess,
    int Code,
    string Message,
    T? Data,
    int? HttpStatusCode = null);

public sealed class ZlmAddStreamProxyData
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
}

public sealed class ZlmDeleteStreamProxyData
{
    [JsonPropertyName("flag")]
    public bool Flag { get; set; }
}

internal sealed class ZlmResponseEnvelope
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}
