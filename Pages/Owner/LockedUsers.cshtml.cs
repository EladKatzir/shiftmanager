using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Page for viewing and unlocking locked user accounts.
/// Fixes A-06, I-01, I-05.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner page requires Grant:AdminAccess (all 107 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class LockedUsersModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<LockedUsersModel> _logger;

    public LockedUsersModel(AppDbContext db, IAuditLogService auditLogService, ILogger<LockedUsersModel> logger)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public List<LockedUserInfo> LockedUsers { get; set; } = new();
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string success) SuccessMessage = success;
        if (TempData["ErrorMessage"] is string error) ErrorMessage = error;

        await LoadLockedUsersAsync();
    }

    public async Task<IActionResult> OnPostUnlockAsync(int userId)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found";
            return RedirectToPage();
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await _db.SaveChangesAsync();

        var currentUserId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;

        _logger.LogInformation("User {UserId} ({Email}) unlocked by admin {AdminId}",
            userId, user.Email, currentUserId);

        await _auditLogService.LogUserActionAsync(
            currentUserId,
            "UserUnlocked",
            "AppUser",
            userId,
            $"Unlocked account for {user.DisplayName} ({user.Email})",
            null);

        TempData["SuccessMessage"] = $"Account for {user.DisplayName} has been unlocked.";
        return RedirectToPage();
    }

    private async Task LoadLockedUsersAsync()
    {
        var now = DateTime.UtcNow;
        LockedUsers = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > now)
            .OrderByDescending(u => u.LockoutEnd)
            .Select(u => new LockedUserInfo
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                FailedAttempts = u.FailedLoginAttempts,
                LockoutEnd = u.LockoutEnd!.Value,
                MinutesRemaining = (int)(u.LockoutEnd!.Value - now).TotalMinutes + 1,
                // I-04: Include last attempt timestamp
                LastAttempt = u.LastLoginAttempt
            })
            .ToListAsync();

        // Also show recently locked users (unlocked in last hour) for awareness
        var hourAgo = now.AddHours(-1);
        var recentlyLocked = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.FailedLoginAttempts >= 5
                && (u.LockoutEnd == null || u.LockoutEnd <= now)
                && u.LockoutEnd > hourAgo)
            .OrderByDescending(u => u.LockoutEnd)
            .Select(u => new LockedUserInfo
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                FailedAttempts = u.FailedLoginAttempts,
                LockoutEnd = u.LockoutEnd ?? now,
                MinutesRemaining = 0,
                IsExpired = true,
                LastAttempt = u.LastLoginAttempt
            })
            .ToListAsync();

        LockedUsers.AddRange(recentlyLocked);

        // I-04: Enrich with source IP from audit logs (most recent login failure per user)
        var lockedUserIds = LockedUsers.Select(u => u.UserId).ToList();
        if (lockedUserIds.Any())
        {
            var recentFailedLogins = await _db.AuditLogs.IgnoreQueryFilters()
                .Where(al => al.Action == "LoginFailed" && al.UserId != null && lockedUserIds.Contains(al.UserId.Value))
                .GroupBy(al => al.UserId)
                .Select(g => new { UserId = g.Key, LastIp = g.OrderByDescending(al => al.Timestamp).First().IpAddress })
                .ToListAsync();

            foreach (var entry in recentFailedLogins)
            {
                var user = LockedUsers.FirstOrDefault(u => u.UserId == entry.UserId);
                if (user != null) user.LastFailedIp = entry.LastIp;
            }
        }
    }
}

public class LockedUserInfo
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public int FailedAttempts { get; set; }
    public DateTime LockoutEnd { get; set; }
    public int MinutesRemaining { get; set; }
    public bool IsExpired { get; set; }
    // I-04: Additional detail for admin investigation
    public DateTime? LastAttempt { get; set; }
    public string? LastFailedIp { get; set; }
}
