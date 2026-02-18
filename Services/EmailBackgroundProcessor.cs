namespace ShiftManager.Services;

/// <summary>
/// Background service that processes queued emails from the EmailBackgroundQueue.
/// Creates a new DI scope per email to properly resolve scoped services (IEmailConfigService, etc.).
/// Falls back to appsettings.json config when no tenant context is available.
/// Implements retry with exponential backoff and dead letter logging to EmailApiLog.
/// </summary>
public class EmailBackgroundProcessor : BackgroundService
{
    private readonly EmailBackgroundQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailBackgroundProcessor> _logger;

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = new[]
    {
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90)
    };

    public EmailBackgroundProcessor(
        EmailBackgroundQueue queue,
        IServiceProvider serviceProvider,
        ILogger<EmailBackgroundProcessor> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailBackgroundProcessor started (max retries: {MaxRetries})", MaxRetries);

        await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var attempt = email with { FirstAttemptAt = email.FirstAttemptAt ?? DateTime.UtcNow };
            bool sent = false;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();
                sent = await mailService.SendMailDirectAsync(attempt.Recipient, attempt.Subject, attempt.HtmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Recipient} (attempt {Attempt}/{MaxAttempts})",
                    attempt.Recipient, attempt.RetryCount + 1, MaxRetries + 1);
            }

            if (!sent && attempt.RetryCount < MaxRetries)
            {
                var delay = RetryDelays[attempt.RetryCount];
                _logger.LogWarning("Scheduling retry {Retry}/{MaxRetries} for email to {Recipient} in {Delay}s",
                    attempt.RetryCount + 1, MaxRetries, attempt.Recipient, delay.TotalSeconds);

                // Schedule retry after delay (don't block the processor).
                // Known limitation: During application shutdown, pending Task.Run retry delays may be
                // cancelled via stoppingToken before they can re-enqueue. This means emails mid-retry-delay
                // will be lost on shutdown. Acceptable for air-gapped deployment where restarts are rare
                // and controlled. A persistent retry queue (e.g. database-backed) would be needed to
                // survive restarts, but is not warranted for the current deployment model.
                var retryEmail = attempt with { RetryCount = attempt.RetryCount + 1 };
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                        if (!_queue.Enqueue(retryEmail))
                        {
                            // Queue is full — retry email would be silently dropped. Log to dead letter.
                            _logger.LogError(
                                "Retry queue full: email to {Recipient} dropped after attempt {Attempt}. Logging to dead letter. Subject: {Subject}",
                                retryEmail.Recipient, retryEmail.RetryCount, retryEmail.Subject);

                            await LogDeadLetterAsync(retryEmail, "RETRY_QUEUE_FULL",
                                $"Retry {retryEmail.RetryCount} could not be enqueued (queue full). Email dropped.");
                        }
                    }
                    catch (OperationCanceledException) { /* shutting down — see comment above */ }
                }, stoppingToken);
            }
            else if (!sent)
            {
                // All retries exhausted — log to dead letter
                _logger.LogError("Email to {Recipient} failed after {Attempts} attempts, logging to dead letter. Subject: {Subject}",
                    attempt.Recipient, attempt.RetryCount + 1, attempt.Subject);

                await LogDeadLetterAsync(attempt, "RETRY_EXHAUSTED",
                    $"All {attempt.RetryCount + 1} attempts failed. Email moved to dead letter.");
            }
        }

        _logger.LogInformation("EmailBackgroundProcessor stopped");
    }

    /// <summary>
    /// Logs a dead letter entry to EmailApiLog for emails that could not be delivered.
    /// Used for both retry-exhausted and retry-queue-full scenarios.
    ///
    /// Tenant context note: This runs in a background service without HTTP context.
    /// When EnforceCompanyScope is true (production default), CompanyIdInterceptor will throw
    /// InvalidOperationException on SaveChanges because CompanyId cannot be resolved. The throw
    /// is caught by EmailApiLogService's defensive try/catch, so the processor won't crash, but
    /// the dead letter record won't be persisted to the database. The _logger.LogError calls
    /// above still capture all dead letter information in structured logs, which is where
    /// operators look in air-gapped IIS deployments. This pre-existing limitation affects all
    /// EmailApiLog writes from the background processor, not just dead letter entries.
    /// Future fix: carry CompanyId on QueuedEmail and set it explicitly before saving.
    /// </summary>
    private async Task LogDeadLetterAsync(QueuedEmail email, string requestMethod, string errorMessage)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var emailApiLogService = scope.ServiceProvider.GetRequiredService<IEmailApiLogService>();
            await emailApiLogService.LogEmailApiCallAsync(
                requestUrl: "dead-letter",
                requestMethod: requestMethod,
                requestHeaders: new Dictionary<string, string>
                {
                    ["X-Retry-Count"] = email.RetryCount.ToString(),
                    ["X-First-Attempt"] = email.FirstAttemptAt?.ToString("o") ?? "unknown"
                },
                requestBody: "",
                responseStatusCode: null,
                responseHeaders: null,
                responseBody: null,
                recipientEmail: email.Recipient,
                emailSubject: email.Subject,
                success: false,
                errorMessage: errorMessage,
                durationMs: 0,
                validationErrors: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log dead letter for email to {Recipient}", email.Recipient);
        }
    }
}
