using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Backs the self-hosted iCalendar subscription feed (Phase 4): per-user token management, feed
/// generation from the user's shifts/chores/on-duty, and the "actively subscribed?" check that
/// drives subscription-aware routing of Felix calendar invites (Phase 5).
/// </summary>
public interface ICalendarFeedService
{
    /// <summary>Get the user's feed token, creating one on first use.</summary>
    Task<string> GetOrCreateTokenAsync(int userId, int companyId);

    /// <summary>Resolve a feed token to its owner and stamp LastPolledAt. Null if unknown/blank.</summary>
    Task<(int userId, int companyId)?> ResolveAndStampAsync(string? token);

    /// <summary>Build the user's .ics feed (upcoming shifts/chores/on-duty).</summary>
    Task<string> BuildFeedAsync(int userId, int companyId, DateTime nowUtc);

    /// <summary>True if the user's feed was polled within <paramref name="window"/> (i.e. they're subscribed).</summary>
    Task<bool> IsActivelySubscribedAsync(int userId, int companyId, TimeSpan window);
}

public sealed class CalendarFeedService : ICalendarFeedService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CalendarFeedService> _logger;

    public CalendarFeedService(AppDbContext db, ILogger<CalendarFeedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> GetOrCreateTokenAsync(int userId, int companyId)
    {
        var existing = await _db.CalendarFeedTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.CompanyId == companyId);
        if (existing != null)
            return existing.Token;

        var token = GenerateToken();
        _db.CalendarFeedTokens.Add(new CalendarFeedToken
        {
            UserId = userId,
            CompanyId = companyId,
            Token = token,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return token;
    }

    public async Task<(int userId, int companyId)?> ResolveAndStampAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var row = await _db.CalendarFeedTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == token);
        if (row == null)
            return null;

        row.LastPolledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (row.UserId, row.CompanyId);
    }

    public async Task<bool> IsActivelySubscribedAsync(int userId, int companyId, TimeSpan window)
    {
        var last = await _db.CalendarFeedTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.CompanyId == companyId)
            .Select(t => t.LastPolledAt)
            .FirstOrDefaultAsync();
        return last.HasValue && (DateTime.UtcNow - last.Value) <= window;
    }

    public async Task<string> BuildFeedAsync(int userId, int companyId, DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var from = today.AddDays(-7);
        var to = today.AddDays(120);
        var events = new List<IcsEvent>();

        var shifts = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
            .Where(sa => sa.UserId == userId && sa.CompanyId == companyId
                      && sa.ShiftInstance.WorkDate >= from && sa.ShiftInstance.WorkDate <= to)
            .ToListAsync();
        foreach (var s in shifts)
        {
            var (st, en) = IsraelTime.ShiftWindowUtc(s.ShiftInstance.WorkDate, s.ShiftInstance.ShiftType.Start, s.ShiftInstance.ShiftType.End);
            events.Add(new IcsEvent($"shift-{s.Id}@shiftmanager", s.ShiftInstance.ShiftType.Name, st, en, IsAllDay: false));
        }

        var chores = await _db.Chores
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.CompanyId == companyId && c.Date >= from && c.Date <= to && c.CanceledAt == null)
            .ToListAsync();
        foreach (var c in chores)
        {
            var d = c.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            events.Add(new IcsEvent($"chore-{c.Id}@shiftmanager", c.Title, d, d, IsAllDay: true));
        }

        var onDuty = await _db.OnDuties
            .IgnoreQueryFilters()
            .Where(o => o.UserId == userId && o.Date >= from && o.Date <= to && o.CanceledAt == null)
            .ToListAsync();
        foreach (var o in onDuty)
        {
            var d = o.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            events.Add(new IcsEvent($"onduty-{o.Id}@shiftmanager", o.Type.ToString(), d, d, IsAllDay: true));
        }

        return IcsBuilder.Build(events, "ShiftManager", nowUtc);
    }

    private static string GenerateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
