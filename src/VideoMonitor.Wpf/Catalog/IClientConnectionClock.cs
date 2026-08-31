namespace VideoMonitor.Wpf.Catalog;

public interface IClientConnectionClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken);

    double NextJitterUnit();
}
