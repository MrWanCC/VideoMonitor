using System.Runtime.ExceptionServices;

namespace VideoMonitor.Wpf.Services;

public static class ShutdownCleanupCoordinator
{
    public static async Task ExecuteAsync(
        Func<Task> playbackCleanup,
        Func<ValueTask> persistenceCleanup)
    {
        ArgumentNullException.ThrowIfNull(playbackCleanup);
        ArgumentNullException.ThrowIfNull(persistenceCleanup);

        Exception? playbackException = null;
        try
        {
            await playbackCleanup().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            playbackException = exception;
        }

        Exception? persistenceException = null;
        try
        {
            await persistenceCleanup().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            persistenceException = exception;
        }

        if (playbackException is not null && persistenceException is not null)
        {
            throw new AggregateException(
                "退出清理失败。",
                playbackException,
                persistenceException);
        }

        if (playbackException is not null)
        {
            ExceptionDispatchInfo.Capture(playbackException).Throw();
        }

        if (persistenceException is not null)
        {
            ExceptionDispatchInfo.Capture(persistenceException).Throw();
        }
    }
}
