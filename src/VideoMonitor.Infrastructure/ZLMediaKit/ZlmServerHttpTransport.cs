namespace VideoMonitor.Infrastructure.ZLMediaKit;

public sealed class ZlmServerHttpTransport : IDisposable
{
    public ZlmServerHttpTransport()
        : this(new SocketsHttpHandler())
    {
    }

    public ZlmServerHttpTransport(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Client = new HttpClient(handler, disposeHandler: true);
    }

    public HttpClient Client { get; }

    public void Dispose() => Client.Dispose();
}
