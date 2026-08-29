namespace VideoMonitor.Core.Tests.Services;

public sealed class ShutdownCleanupCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_AttemptsPersistenceCleanupWhenPlaybackCleanupFails()
    {
        var persistenceAttempted = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            VideoMonitor.Wpf.Services.ShutdownCleanupCoordinator.ExecuteAsync(
                () => Task.FromException(new InvalidOperationException("播放清理失败")),
                () =>
                {
                    persistenceAttempted = true;
                    return ValueTask.CompletedTask;
                }));

        Assert.True(persistenceAttempted);
        Assert.Equal("播放清理失败", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesBothFailuresWhenBothCleanupStepsFail()
    {
        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            VideoMonitor.Wpf.Services.ShutdownCleanupCoordinator.ExecuteAsync(
                () => Task.FromException(new InvalidOperationException("播放清理失败")),
                () => ValueTask.FromException(new InvalidOperationException("目录保存失败"))));

        Assert.Contains(exception.InnerExceptions, item => item.Message == "播放清理失败");
        Assert.Contains(exception.InnerExceptions, item => item.Message == "目录保存失败");
    }
}
