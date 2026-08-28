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
}
