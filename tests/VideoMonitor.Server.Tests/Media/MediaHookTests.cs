using System.Net;
using System.Text;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
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

    [Fact]
    public async Task StreamChangedTriggersRecoveryReconcileOnlyAfterDequeue()
    {
        var initialReconcile = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryReconcile = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reconcileCalls = 0;
        var contributor = new DelegateContributor(_ =>
        {
            var call = Interlocked.Increment(ref reconcileCalls);
            if (call == 1)
            {
                initialReconcile.TrySetResult(null);
            }
            else if (call == 2)
            {
                recoveryReconcile.TrySetResult(null);
            }

            return Task.CompletedTask;
        });
        var reconcile = new MediaReconcilerHostedService(
            new[] { contributor },
            new MediaServerHealthState(),
            new FakeRuntimeSettingsProvider(),
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var manager = new FakeStreamManager();
        using var processor = new MediaEventProcessor(manager, reconcile);

        await reconcile.StartAsync(CancellationToken.None);
        await initialReconcile.Task;
        await processor.StartAsync(CancellationToken.None);

        var status = await SendAsync(processor, "on-stream-changed", IPAddress.Loopback);
        var recoveryWasTriggered =
            await Task.WhenAny(recoveryReconcile.Task, Task.Delay(TimeSpan.FromSeconds(1)))
            == recoveryReconcile.Task;

        await processor.StopAsync(CancellationToken.None);
        await reconcile.StopAsync(CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, status);
        Assert.True(recoveryWasTriggered);
        Assert.Equal(2, reconcileCalls);
        Assert.False(manager.CleanupCalled.Task.IsCompleted);
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

    private sealed class DelegateContributor : IMediaReconcileContributor
    {
        private readonly Func<CancellationToken, Task> action;

        public DelegateContributor(Func<CancellationToken, Task> action)
        {
            this.action = action;
        }

        public Task ReconcileAsync(CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }

    private sealed class FakeRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://127.0.0.1:8080",
                "",
                "configured-vhost",
                "videomonitor",
                "videomonitor-test",
                "",
                30,
                1));
    }
}
