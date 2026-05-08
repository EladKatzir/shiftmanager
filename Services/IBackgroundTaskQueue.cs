using System.Threading.Channels;

namespace ShiftManager.Services;

/// <summary>
/// A unit of background work executed by <see cref="BackgroundTaskHostedService"/>.
/// The queue creates a fresh DI scope per work item and supplies the scoped
/// <see cref="IServiceProvider"/> here, so callers don't manage scopes themselves.
/// </summary>
public delegate Task BackgroundTask(IServiceProvider scopedProvider, CancellationToken cancellationToken);

/// <summary>
/// Singleton bounded queue for fire-and-forget background work that needs
/// observability and back-pressure. Replaces ad-hoc <c>_ = Task.Run(...)</c>
/// callsites — closes F-A-003 from the 2026-04-28 diagnostic review.
///
/// Distinct from <see cref="EmailBackgroundQueue"/>, which is strongly-typed for
/// <c>QueuedEmail</c> and includes retry/dead-letter logic specific to email
/// delivery. Two queues is correct: this one is generic, that one is
/// domain-specific.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Enqueues a work item without waiting. Returns false (and logs a warning)
    /// if the queue is at capacity — caller decides whether to drop the work,
    /// retry, or surface the failure to the user.
    /// </summary>
    bool Enqueue(BackgroundTask workItem);

    /// <summary>
    /// Enqueues a work item with up to 2 seconds of back-pressure wait if the
    /// queue is full. Returns false only after the wait expires.
    /// </summary>
    Task<bool> EnqueueAsync(BackgroundTask workItem, CancellationToken ct = default);

    /// <summary>
    /// Reader for the hosted service to consume queued work items.
    /// Internal-by-convention — only <see cref="BackgroundTaskHostedService"/>
    /// should read from this.
    /// </summary>
    ChannelReader<BackgroundTask> Reader { get; }
}

/// <summary>
/// Default implementation backed by a bounded <see cref="Channel{T}"/>.
/// </summary>
public partial class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<BackgroundTask> _channel;
    private readonly ILogger<BackgroundTaskQueue> _logger;

    public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<BackgroundTask>(new BoundedChannelOptions(500)
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public bool Enqueue(BackgroundTask workItem)
    {
        if (_channel.Writer.TryWrite(workItem))
            return true;

        LogQueueFullDropped(_logger);
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(BackgroundTask workItem, CancellationToken ct = default)
    {
        if (_channel.Writer.TryWrite(workItem))
            return true;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _channel.Writer.WriteAsync(workItem, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            LogQueueFullAfterWait(_logger);
            return false;
        }
    }

    /// <inheritdoc />
    public ChannelReader<BackgroundTask> Reader => _channel.Reader;

    [LoggerMessage(EventId = 11000, Level = LogLevel.Warning,
        Message = "Background task queue full (500 capacity), dropping work item")]
    private static partial void LogQueueFullDropped(ILogger logger);

    [LoggerMessage(EventId = 11001, Level = LogLevel.Warning,
        Message = "Background task queue full after 2s wait, dropping work item")]
    private static partial void LogQueueFullAfterWait(ILogger logger);
}
