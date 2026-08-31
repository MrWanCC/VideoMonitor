namespace VideoMonitor.Wpf.Catalog;

public sealed class SystemClientConnectionClock : IClientConnectionClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    public double NextJitterUnit() => Random.Shared.NextDouble();
}
