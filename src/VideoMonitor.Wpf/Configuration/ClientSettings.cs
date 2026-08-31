namespace VideoMonitor.Wpf.Configuration;

public sealed record ClientServerSettings(string? BaseUrl);

public sealed record ClientSettings(ClientServerSettings Server)
{
    public static ClientSettings Empty { get; } = new(new ClientServerSettings(null));
}
