namespace ShiftManager.Services;

/// <summary>
/// Background service that processes queued emails from the EmailBackgroundQueue.
/// Creates a new DI scope per email to properly resolve scoped services (IEmailConfigService, etc.).
/// Falls back to appsettings.json config when no tenant context is available.
/// </summary>
public class EmailBackgroundProcessor : BackgroundService
{
    private readonly EmailBackgroundQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailBackgroundProcessor> _logger;

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
        _logger.LogInformation("EmailBackgroundProcessor started");

        await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();
                await mailService.SendMailDirectAsync(email.Recipient, email.Subject, email.HtmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send queued email to {Recipient}", email.Recipient);
            }
        }

        _logger.LogInformation("EmailBackgroundProcessor stopped");
    }
}
