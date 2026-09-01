using System.Net;
using System.Text;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Media;
using Microsoft.AspNetCore.Http;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaHookTests
{
    private static readonly MediaStreamKey Key = new(
        Guid.Parse("75000000-0000-0000-0000-000000000001"),
        Guid.Parse("76000000-0000-0000-0000-000000000001"),
        StreamType.Main);

    [Fact]
    public async Task HookOnlyEnqueuesAndDoesNotRunZlmWorkInline()
    {
        var manager = new FakeStreamManager();
        using var processor = new MediaEventProcessor(manager);

        var result = await SendAsync(processor, "on-stream-changed", IPAddress.Loopback);

        Assert.Equal(StatusCodes.Status202Accepted, result);
        Assert.False(manager.CleanupCalled.Task.IsCompleted);
        await processor.StartAsync(CancellationToken.None);
        Assert.False(manager.CleanupCalled.Task.IsCompleted);
        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoopbackCallerIsAccepted()
    {
        using var processor = new MediaEventProcessor(new FakeStreamManager());

        var status = await SendAsync(processor, "on-stream-changed", IPAddress.Loopback);

        Assert.Equal(StatusCodes.Status202Accepted, status);
        Assert.Equal(1, processor.EnqueuedCount);
    }

    [Fact]
    public async Task NonLoopbackCallerReturns403WithoutEnqueue()
    {
        using var processor = new MediaEventProcessor(new FakeStreamManager());

        var status = await SendAsync(
            processor,
            "on-stream-changed",
            IPAddress.Parse("192.0.2.20"));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal(0, processor.EnqueuedCount);
    }

    [Fact]
    public async Task NoneReaderTrustedHookStillRechecksZlmBeforeCleanup()
    {
        var manager = new FakeStreamManager();
        using var processor = new MediaEventProcessor(manager);
        await processor.StartAsync(CancellationToken.None);

        var status = await SendAsync(processor, "on-stream-none-reader", IPAddress.Loopback);

        Assert.Equal(StatusCodes.Status202Accepted, status);
        await manager.CleanupCalled.Task;
        Assert.Equal(Key, manager.LastCleanupKey);
        await processor.StopAsync(CancellationToken.None);
    }

    private static async Task<int> SendAsync(
        MediaEventProcessor processor,
        string routeKind,
        IPAddress remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        var payload = "{\"schema\":\"rtsp\",\"vhost\":\"configured-vhost\","
            + "\"app\":\"videomonitor\",\"stream\":\""
            + Key.ToFormalStreamId()
            + "\"}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var result = await MediaHookEndpoints.HandleAsync(
            context,
            routeKind,
            new LoopbackZlmHookTrustPolicy(),
            processor);
        return Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode!.Value;
    }

    private sealed class FakeStreamManager : IStreamManager
    {
        public TaskCompletionSource<object?> CleanupCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MediaStreamKey LastCleanupKey { get; private set; }

        public Task<StreamEnsureResult> EnsureStreamAsync(
            MediaStreamRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StreamEnsureResult(false, null, "unused"));

        public Task CleanupOwnedStreamIfEligibleAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default)
        {
            LastCleanupKey = key;
            CleanupCalled.TrySetResult(null);
            return Task.CompletedTask;
        }

        public MediaRuntimeSnapshot GetSnapshot() =>
            new(MediaServerHealth.Healthy, Array.Empty<MediaStreamRuntimeInfo>());
    }
}
