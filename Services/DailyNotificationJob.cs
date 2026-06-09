using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;

namespace ShiftManager.Services;

/// <summary>
/// Phase 6: Background service that sends daily digest emails to users based on their preferences.
/// Runs every 15 minutes to check for users whose preferred time matches the current time.
/// Can be disabled via feature flag: FF_ENABLE_DAILY_NOTIFICATIONS
/// </summary>
public class DailyNotificationJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyNotificationJob> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);
    // C-04: Overlap detection — prevent job from running concurrently with itself
    private int _isRunning;

    public DailyNotificationJob(
        IServiceProvider serviceProvider,
        ILogger<DailyNotificationJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Batch T (F-R-007): atomic acquire of the per-process job lock. Returns true
    /// if this caller acquired the lock, false if a previous iteration is still running.
    /// Wraps the raw Interlocked.CompareExchange so the intent ("am I allowed to start?")
    /// is obvious at the call site.
    /// </summary>
    private bool TryAcquireRunningLock() => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;

    /// <summary>
    /// Batch T (F-R-007): release the per-process job lock. Idempotent.
    /// </summary>
    private void ReleaseRunningLock() => Interlocked.Exchange(ref _isRunning, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Daily Notification Job started");

        // Check if daily notifications are enabled via feature flag
        bool isEnabled;
        using (var scope = _serviceProvider.CreateScope())
        {
            var flagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
            isEnabled = await flagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnableDailyNotifications);
        }

        if (!isEnabled)
        {
            _logger.LogInformation("Daily notifications disabled by feature flag ({FlagName}=false). Job will exit gracefully.",
                FeatureFlagSeed.Flags.EnableDailyNotifications);
            return;
        }

        // Wait 1 minute on startup to let the application fully initialize
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        // Establish the app-default culture as the fallback for users whose PreferredLanguage
        // has never been observed. Per-recipient learned languages override this via CultureScope
        // inside SendDailyDigestAsync / SendDayBeforeRemindersAsync. CultureInfo is AsyncLocal-backed,
        // so this flows into the awaited per-user sends without affecting other threads.
        using (var cultureScope = _serviceProvider.CreateScope())
        {
            var locOptions = cultureScope.ServiceProvider
                .GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>();
            var defaultCulture = locOptions?.Value.DefaultRequestCulture.Culture;
            var defaultUiCulture = locOptions?.Value.DefaultRequestCulture.UICulture;
            if (defaultCulture != null)
                System.Globalization.CultureInfo.CurrentCulture = defaultCulture;
            if (defaultUiCulture != null)
                System.Globalization.CultureInfo.CurrentUICulture = defaultUiCulture;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Re-check feature flag at start of each iteration to allow runtime disabling
            using (var flagScope = _serviceProvider.CreateScope())
            {
                var flagService = flagScope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
                isEnabled = await flagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnableDailyNotifications);
            }

            if (!isEnabled)
            {
                _logger.LogInformation("Daily notifications disabled by feature flag. Exiting job gracefully.");
                break;
            }

            // C-04: Skip if previous iteration is still running
            if (!TryAcquireRunningLock())
            {
                _logger.LogWarning("Daily digest processing still running from previous iteration, skipping this cycle");
                await Task.Delay(_checkInterval, stoppingToken);
                continue;
            }

            try
            {
                await ProcessDailyDigestsAsync(stoppingToken);

                // Section 10C: Cleanup old telemetry data daily (prevents unbounded DB growth in air-gapped environments)
                try
                {
                    using var cleanupScope = _serviceProvider.CreateScope();
                    var telemetryService = cleanupScope.ServiceProvider.GetRequiredService<IClientTelemetryService>();
                    var (events, errors, metrics) = await telemetryService.CleanupOldDataAsync(30);
                    if (events > 0 || errors > 0 || metrics > 0)
                    {
                        _logger.LogInformation("Telemetry cleanup: removed {Events} events, {Errors} errors, {Metrics} metrics older than 30 days",
                            events, errors, metrics);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Telemetry data cleanup failed (non-critical)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing daily digests");
            }
            finally
            {
                ReleaseRunningLock();
            }

            // Wait for next check interval
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected when stopping
                break;
            }
        }

        _logger.LogInformation("Daily Notification Job stopped");
    }

    private async Task ProcessDailyDigestsAsync(CancellationToken stoppingToken)
    {
        // F-05: Generate job-execution ID for log correlation (background jobs have no HTTP context)
        var jobExecutionId = $"job-daily-{Guid.NewGuid():N}";
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = jobExecutionId });

        var currentTime = TimeOnly.FromDateTime(DateTime.UtcNow);
        _logger.LogInformation("Processing daily digests at {Time} UTC (JobExecutionId: {JobId})", currentTime, jobExecutionId);

        using var serviceScope = _serviceProvider.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationService = serviceScope.ServiceProvider.GetRequiredService<INotificationService>();

        // Get all companies
        var companyIds = await db.Companies
            .Select(c => c.Id)
            .ToListAsync();

        _logger.LogInformation("Found {CompanyCount} companies to check", companyIds.Count);

        var totalDigestsSent = 0;
        var totalErrors = 0;

        foreach (var companyId in companyIds)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                // Get users who should receive digest at this time for this company
                var userIds = await notificationService.GetUsersForDailyDigestAsync(currentTime, companyId);

                if (!userIds.Any())
                {
                    _logger.LogDebug("No users scheduled for digest at {Time} UTC in company {CompanyId}",
                        currentTime, companyId);
                    continue;
                }

                _logger.LogInformation("Sending daily digest to {Count} users in company {CompanyId}",
                    userIds.Count, companyId);

                // Send digest to each user
                foreach (var userId in userIds)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    try
                    {
                        var success = await notificationService.SendDailyDigestAsync(userId, companyId);
                        if (success)
                        {
                            totalDigestsSent++;
                        }
                        else
                        {
                            totalErrors++;
                        }

                        // Phase 6 Extension: Also send day-before reminders if user has any enabled
                        try
                        {
                            await notificationService.SendDayBeforeRemindersAsync(userId, companyId);
                        }
                        catch (Exception reminderEx)
                        {
                            _logger.LogError(reminderEx, "Error sending day-before reminders to user {UserId} in company {CompanyId}",
                                userId, companyId);
                            // Don't increment totalErrors - this is a separate optional feature
                        }

                        // C-04: Reduced from 2s to 200ms to prevent job overlap at scale (500 users × 2s = 16min > 15min interval)
                        await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending digest to user {UserId} in company {CompanyId}",
                            userId, companyId);
                        totalErrors++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing company {CompanyId} for daily digests", companyId);
            }
        }

        if (totalDigestsSent > 0 || totalErrors > 0)
        {
            _logger.LogInformation(
                "Daily digest processing completed: {Sent} sent, {Errors} errors",
                totalDigestsSent, totalErrors);
        }

        // F-08: Write delivery stats to file for SystemAlerts banner to read
        try
        {
            var statsPath = Path.Combine(AppContext.BaseDirectory, "notification-stats.json");
            var stats = System.Text.Json.JsonSerializer.Serialize(new
            {
                LastRun = DateTime.UtcNow,
                Sent = totalDigestsSent,
                Errors = totalErrors,
                Companies = companyIds.Count
            });
            await File.WriteAllTextAsync(statsPath, stats, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write notification stats file");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Daily Notification Job is stopping...");
        await base.StopAsync(cancellationToken);
    }
}
