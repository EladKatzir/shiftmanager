using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services.Notifications;

namespace ShiftManager.Services;

/// <summary>
/// DB-backed implementation of <see cref="INotificationPreferenceService"/>. Combines the pure
/// <see cref="NotificationGate"/> rules with persisted engagement mode, per-category mutes, and
/// throttle state.
///
/// <para>
/// SECURITY-AUDITED: uses <c>IgnoreQueryFilters()</c> because callers pass an explicit
/// (userId, companyId) pair — both from in-request UI (the user editing their own prefs) and from
/// the anonymous, signed-token opt-out endpoint (no tenant context). The companyId is always known
/// and applied explicitly, so this does not widen tenant exposure: every query is constrained to
/// the exact (userId, companyId) supplied.
/// </para>
/// </summary>
public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationPreferenceService> _logger;

    public NotificationPreferenceService(AppDbContext db, ILogger<NotificationPreferenceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private Task<DailyNotificationPreference?> FindPrefAsync(int userId, int companyId)
        => _db.DailyNotificationPreferences
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

    public async Task<EngagementMode> GetEngagementModeAsync(int userId, int companyId)
    {
        var pref = await FindPrefAsync(userId, companyId);
        return pref?.EngagementMode ?? EngagementMode.Engaged;
    }

    public async Task SetEngagementModeAsync(int userId, int companyId, EngagementMode mode)
    {
        var pref = await FindPrefAsync(userId, companyId);
        if (pref == null)
        {
            // Create a preference row carrying the explicit CompanyId. The CompanyIdInterceptor
            // skips entities that already have a non-zero CompanyId, so this works without HTTP
            // tenant context (the opt-out endpoint path).
            pref = new DailyNotificationPreference
            {
                UserId = userId,
                CompanyId = companyId,
                EngagementMode = mode,
                CreatedAt = DateTime.UtcNow
            };
            _db.DailyNotificationPreferences.Add(pref);
        }
        else
        {
            pref.EngagementMode = mode;
            pref.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlySet<NotificationCategory>> GetMutedCategoriesAsync(int userId, int companyId)
    {
        var ints = await _db.NotificationCategoryMutes
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.CompanyId == companyId)
            .Select(m => m.Category)
            .ToListAsync();
        return ints.Select(i => (NotificationCategory)i).ToHashSet();
    }

    public async Task SetCategoryMuteAsync(int userId, int companyId, NotificationCategory category, bool muted)
    {
        var categoryValue = (int)category;
        var existing = await _db.NotificationCategoryMutes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == companyId && m.Category == categoryValue);

        if (muted && existing == null)
        {
            _db.NotificationCategoryMutes.Add(new NotificationCategoryMute
            {
                UserId = userId,
                CompanyId = companyId,
                Category = categoryValue,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        else if (!muted && existing != null)
        {
            _db.NotificationCategoryMutes.Remove(existing);
            await _db.SaveChangesAsync();
        }
        // else: already in the desired state → no-op
    }

    public Task<bool> ShouldSendEmailAsync(int userId, int companyId, NotificationEvent evt)
        => ShouldSendEmailAsync(userId, companyId, evt.Category, evt.PersonallyActionable, evt.SecurityCritical);

    public async Task<bool> ShouldSendEmailAsync(int userId, int companyId, NotificationCategory category, bool personallyActionable, bool securityCritical)
    {
        var mode = await GetEngagementModeAsync(userId, companyId);

        var muted = false;
        if (!securityCritical)
        {
            var categoryValue = (int)category;
            muted = await _db.NotificationCategoryMutes
                .IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == userId && m.CompanyId == companyId && m.Category == categoryValue);
        }

        return NotificationGate.ShouldSendEmail(securityCritical, personallyActionable, mode, muted);
    }

    public async Task<bool> TryBeginCatchUpAsync(int userId, int companyId, int unreadCount)
    {
        var pref = await FindPrefAsync(userId, companyId);
        if (pref == null)
            return false; // no row → Engaged default → no catch-up

        // Self-heal: clear the guard once the user has read back below the threshold.
        if (NotificationGate.ShouldResetCatchUpGuard(unreadCount, pref.CatchUpEmailPending))
        {
            pref.CatchUpEmailPending = false;
            await _db.SaveChangesAsync();
        }

        if (NotificationGate.ShouldSendCatchUp(pref.EngagementMode, unreadCount, pref.CatchUpEmailPending))
        {
            pref.CatchUpEmailPending = true;
            pref.LastCatchUpEmailAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        return false;
    }
}
