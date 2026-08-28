using System.Net;
using System.Text;

namespace VideoMonitor.Core.Tests.Infrastructure;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode StatusCode, string Body)> responses;

    public StubHttpMessageHandler(params string[] bodies)
        : this(bodies.Select(body => (HttpStatusCode.OK, body)).ToArray())
    {
    }

    public StubHttpMessageHandler(params (HttpStatusCode StatusCode, string Body)[] responses)
    {
        this.responses = new Queue<(HttpStatusCode StatusCode, string Body)>(responses);
    }

    public List<Uri> Requests { get; } = [];

    public Uri? LastRequestUri => Requests.LastOrDefault();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            Requests.Add(request.RequestUri);
        }

        if (!responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException("没有为该HTTP请求准备测试响应。");
        }

        return Task.FromResult(new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
        });
    }
}
