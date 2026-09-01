using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Media;

public sealed class MediaReconcilerHostedService : IHostedService
{
    private static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] UnavailableBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    private readonly IReadOnlyList<IMediaReconcileContributor> contributors;
    private readonly MediaServerHealthState healthState;
    private readonly IMediaRuntimeSettingsProvider settingsProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Channel<bool> recoverySignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly object lifecycleSync = new();
    private CancellationTokenSource? hostedCancellation;
    private Task? hostedRun;

    public MediaReconcilerHostedService(
        IEnumerable<IMediaReconcileContributor> contributors,
        MediaServerHealthState healthState,
        IMediaRuntimeSettingsProvider settingsProvider,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        this.contributors = contributors.ToArray();
        this.healthState = healthState ?? throw new ArgumentNullException(nameof(healthState));
        this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleSync)
        {
            if (hostedRun is not null)
            {
                return Task.CompletedTask;
            }

            hostedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            hostedRun = RunAsync(hostedCancellation.Token);
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? run;
        lock (lifecycleSync)
        {
            hostedCancellation?.Cancel();
            run = hostedRun;
        }

        if (run is null)
        {
            return;
        }

        try
        {
            await run.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        lock (lifecycleSync)
        {
            hostedCancellation?.Dispose();
            hostedCancellation = null;
            hostedRun = null;
        }
    }

    public void TriggerRecovery() => recoverySignals.Writer.TryWrite(true);

    public async Task WaitForNoReaderGraceAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        var seconds = Math.Max(0, settings.NoReaderGraceSeconds);
        await delayAsync(TimeSpan.FromSeconds(seconds), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var backoffIndex = 0;
        await ReconcileOnceAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = healthState.Health == Core.Media.MediaServerHealth.Unavailable
                ? UnavailableBackoff[backoffIndex]
                : NormalInterval;
            var delayTask = delayAsync(delay, cancellationToken);
            var recoveryTask = recoverySignals.Reader
                .WaitToReadAsync(cancellationToken)
                .AsTask();
            var completed = await Task.WhenAny(delayTask, recoveryTask).ConfigureAwait(false);

            if (completed == recoveryTask && await recoveryTask.ConfigureAwait(false))
            {
                while (recoverySignals.Reader.TryRead(out _))
                {
                }

                backoffIndex = 0;
            }
            else
            {
                await delayTask.ConfigureAwait(false);
            }

            await ReconcileOnceAsync(cancellationToken).ConfigureAwait(false);
            backoffIndex = healthState.Health == Core.Media.MediaServerHealth.Unavailable
                ? Math.Min(backoffIndex + 1, UnavailableBackoff.Length - 1)
                : 0;
            await Task.Yield();
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var contributor in contributors)
        {
            try
            {
                await contributor.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                healthState.MarkUnavailable();
            }
        }
    }
}
