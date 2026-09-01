using System.Text.Json.Serialization;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed class ZlmStreamInfo
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("vhost")]
    public string Vhost { get; set; } = string.Empty;

    [JsonPropertyName("app")]
    public string App { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public string Stream { get; set; } = string.Empty;

    [JsonPropertyName("originType")]
    public int? OriginType { get; set; }

    [JsonPropertyName("originTypeStr")]
    public string? OriginTypeStr { get; set; }

    [JsonPropertyName("originUrl")]
    public string? OriginUrl { get; set; }

    [JsonPropertyName("createStamp")]
    public long? CreateStamp { get; set; }

    [JsonPropertyName("aliveSecond")]
    public long? AliveSecond { get; set; }

    [JsonPropertyName("totalReaderCount")]
    public int TotalReaderCount { get; set; }
}
