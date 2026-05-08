namespace ShiftManager.Services;

/// <summary>
/// Drains <see cref="IBackgroundTaskQueue"/> on a single reader, creating a fresh
/// DI scope per work item so scoped services (DbContext, etc.) resolve correctly.
///
/// One unhandled exception in a work item is caught and logged — it must not
/// stop the loop, since other queued items still need to run. Cancellation
/// during shutdown ends the loop cleanly.
///
/// Closes F-A-003 (raw fire-and-forget Task.Run had no observability or back-pressure).
/// </summary>
public partial class BackgroundTaskHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundTaskHostedService> _logger;

    public BackgroundTaskHostedService(
        IBackgroundTaskQueue queue,
        IServiceProvider serviceProvider,
        ILogger<BackgroundTaskHostedService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);

        try
        {
            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    await workItem(scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown — propagate to break the foreach
                    throw;
                }
                catch (Exception ex)
                {
                    LogWorkItemFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }

        LogStopped(_logger);
    }

    [LoggerMessage(EventId = 11010, Level = LogLevel.Information,
        Message = "BackgroundTaskHostedService started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 11011, Level = LogLevel.Information,
        Message = "BackgroundTaskHostedService stopped")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(EventId = 11012, Level = LogLevel.Error,
        Message = "Background work item threw an unhandled exception (caught to keep the loop alive)")]
    private static partial void LogWorkItemFailed(ILogger logger, System.Exception ex);
}
