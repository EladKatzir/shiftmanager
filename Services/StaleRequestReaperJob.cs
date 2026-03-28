using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Background service that runs weekly to clean up stale Pending requests
/// older than a configurable threshold (default 30 days).
/// </summary>
public class StaleRequestReaperJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleRequestReaperJob> _logger;
    private readonly IConfiguration _configuration;

    public StaleRequestReaperJob(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleRequestReaperJob> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalDays = _configuration.GetValue("Reaper:IntervalDays", 7);
        var interval = TimeSpan.FromDays(intervalDays);

        _logger.LogInformation("StaleRequestReaperJob started. Interval: {IntervalDays} days", intervalDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ReapStaleRequestsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StaleRequestReaperJob encountered an error");
            }
        }
    }

    private async Task ReapStaleRequestsAsync(CancellationToken ct)
    {
        var maxAgeDays = _configuration.GetValue("Reaper:MaxAgeDays", 30);
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        _logger.LogInformation("Reaping stale requests older than {MaxAgeDays} days (cutoff: {Cutoff})", maxAgeDays, cutoff);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var totalReaped = 0;

        // 1. TimeOffRequests — set Status to Canceled
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design.
        // Only modifies Status field on records matching Pending + age criteria.
        try
        {
            var staleTimeOff = await db.TimeOffRequests
                .IgnoreQueryFilters()
                .Where(r => r.Status == RequestStatus.Pending && r.CreatedAt < cutoff)
                .ToListAsync(ct);

            foreach (var r in staleTimeOff)
                r.Status = RequestStatus.Canceled;

            if (staleTimeOff.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                totalReaped += staleTimeOff.Count;
                _logger.LogInformation("Reaped {Count} stale TimeOffRequests", staleTimeOff.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reap stale TimeOffRequests");
        }

        // 2. SwapRequests — set Status to Canceled
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design.
        try
        {
            var staleSwaps = await db.SwapRequests
                .IgnoreQueryFilters()
                .Where(r => r.Status == RequestStatus.Pending && r.CreatedAt < cutoff)
                .ToListAsync(ct);

            foreach (var r in staleSwaps)
                r.Status = RequestStatus.Canceled;

            if (staleSwaps.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                totalReaped += staleSwaps.Count;
                _logger.LogInformation("Reaped {Count} stale SwapRequests", staleSwaps.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reap stale SwapRequests");
        }

        // 3. UserJoinRequests — set Status to Rejected
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design.
        try
        {
            var staleJoins = await db.UserJoinRequests
                .IgnoreQueryFilters()
                .Where(r => r.Status == JoinRequestStatus.Pending && r.CreatedAt < cutoff)
                .ToListAsync(ct);

            foreach (var r in staleJoins)
                r.Status = JoinRequestStatus.Rejected;

            if (staleJoins.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                totalReaped += staleJoins.Count;
                _logger.LogInformation("Reaped {Count} stale UserJoinRequests", staleJoins.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reap stale UserJoinRequests");
        }

        // 4. ApiKeyRequests — set Status to Expired (purpose-built enum value)
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design.
        try
        {
            var staleApiKeys = await db.ApiKeyRequests
                .IgnoreQueryFilters()
                .Where(r => r.Status == Models.Api.ApiKeyRequestStatus.Pending && r.RequestedAt < cutoff)
                .ToListAsync(ct);

            foreach (var r in staleApiKeys)
                r.Status = Models.Api.ApiKeyRequestStatus.Expired;

            if (staleApiKeys.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                totalReaped += staleApiKeys.Count;
                _logger.LogInformation("Reaped {Count} stale ApiKeyRequests", staleApiKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reap stale ApiKeyRequests");
        }

        _logger.LogInformation("StaleRequestReaperJob completed. Total reaped: {Total}", totalReaped);
    }
}
