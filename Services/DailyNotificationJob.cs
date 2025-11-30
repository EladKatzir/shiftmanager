using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;

namespace ShiftManager.Services;

/// <summary>
/// Phase 6: Background service that sends daily digest emails to users based on their preferences.
/// Runs every 15 minutes to check for users whose preferred time matches the current time.
/// </summary>
public class DailyNotificationJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyNotificationJob> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    public DailyNotificationJob(
        IServiceProvider serviceProvider,
        ILogger<DailyNotificationJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Daily Notification Job started");

        // Wait 1 minute on startup to let the application fully initialize
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDailyDigestsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing daily digests");
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
        var currentTime = TimeOnly.FromDateTime(DateTime.UtcNow);
        _logger.LogInformation("Processing daily digests at {Time} UTC", currentTime);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

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

                        // Small delay between emails to avoid overwhelming the mail service
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
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
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Daily Notification Job is stopping...");
        await base.StopAsync(cancellationToken);
    }
}
