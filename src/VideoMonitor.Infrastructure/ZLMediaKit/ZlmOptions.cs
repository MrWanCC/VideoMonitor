namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed class ZlmOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string Vhost { get; set; } = "__defaultVhost__";

    public string App { get; set; } = "live";

    public string RtspHost { get; set; } = string.Empty;

    public int RtspPort { get; set; } = 554;
}
