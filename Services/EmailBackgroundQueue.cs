using System.Threading.Channels;

namespace ShiftManager.Services;

/// <summary>
/// Queued email item containing all data needed for sending.
/// </summary>
public record QueuedEmail(string Recipient, string Subject, string HtmlBody, int CompanyId = 0, int RetryCount = 0, DateTime? FirstAttemptAt = null, int RecipientUserId = 0);

/// <summary>
/// Singleton bounded channel for background email delivery.
/// Emails are enqueued by MailService.SendMailAsync and processed by EmailBackgroundProcessor.
/// </summary>
public class EmailBackgroundQueue
{
    private readonly Channel<QueuedEmail> _channel;
    private readonly ILogger<EmailBackgroundQueue> _logger;

    public EmailBackgroundQueue(ILogger<EmailBackgroundQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(500)
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Enqueues an email for background delivery. Returns false if the queue is full.
    /// </summary>
    public bool Enqueue(QueuedEmail email)
    {
        if (_channel.Writer.TryWrite(email))
            return true;

        _logger.LogWarning("Email queue full (500 capacity), dropping email to {Recipient} with subject: {Subject}",
            email.Recipient, email.Subject);
        return false;
    }

    /// <summary>
    /// Enqueues an email for background delivery with backpressure support.
    /// Waits up to 2 seconds for space to open if the queue is full before dropping.
    /// </summary>
    public async Task<bool> EnqueueAsync(QueuedEmail email, CancellationToken ct = default)
    {
        // Try non-blocking first
        if (_channel.Writer.TryWrite(email))
            return true;

        // Brief wait (2 seconds) for space to open up
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _channel.Writer.WriteAsync(email, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Email queue full after 2s wait, dropping email to {Recipient} with subject: {Subject}",
                email.Recipient, email.Subject);
            return false;
        }
    }

    /// <summary>
    /// Reader for the background processor to consume queued emails.
    /// </summary>
    public ChannelReader<QueuedEmail> Reader => _channel.Reader;
}
