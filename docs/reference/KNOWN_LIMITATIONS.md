# ShiftManager - Known Limitations

This document outlines known limitations in the current version of ShiftManager and provides guidance for addressing them if needed.

---

## 1. Single-Timezone Architecture

**Status:** Known Limitation (Low Priority)
**Impact:** Works perfectly for single-timezone deployments
**Affected:** Multi-timezone organizations only

### Description

ShiftManager currently uses server timezone (`DateTime.Today`) for all date operations. User-specific timezone preferences are not supported.

### Current Behavior

```csharp
var today = DateOnly.FromDateTime(DateTime.Today); // Uses server timezone
```

All users see dates based on the server's timezone setting, not their personal timezone.

### When This Works Well

- ✅ **Single-timezone organizations** (most common deployment)
- ✅ **Organizations where all users are in same timezone**
- ✅ **Small to medium businesses with local operations**

### When This Could Be An Issue

- ⚠️ **Multi-national organizations** with employees across timezones
- ⚠️ **24/7 operations** spanning multiple timezones
- ⚠️ **Remote-first companies** with global workforce

### Example Scenario

- Server timezone: UTC-5 (New York)
- User A in California (UTC-8): Sees "today" as server's today
- User B in London (UTC+0): Also sees "today" as server's today
- Both users see the same date, which may not match their local timezone

### Impact Assessment

**For 90% of deployments:** No impact - single timezone operation
**For multi-timezone deployments:** Minor inconvenience - dates may be off by 1 day near midnight

---

## How to Implement Multi-Timezone Support (Future Enhancement)

If your organization requires user-specific timezone support, follow these steps:

### Phase 1: Database Changes (1 hour)

**1. Add TimeZone property to AppUser model:**

```csharp
// File: Models/AppUser.cs
public class AppUser
{
    // Existing properties...

    /// <summary>
    /// User's timezone in IANA format (e.g., "America/New_York", "Europe/London")
    /// If null, defaults to server timezone.
    /// </summary>
    public string? TimeZone { get; set; }
}
```

**2. Create migration:**

```bash
dotnet ef migrations add AddUserTimezone
dotnet ef database update
```

### Phase 2: Timezone Service (2 hours)

**Create `Services/ITimezoneService.cs`:**

```csharp
public interface ITimezoneService
{
    DateOnly GetUserToday(int userId);
    DateTime ConvertToUserTime(DateTime utcDateTime, int userId);
    DateTime ConvertToUtc(DateTime userDateTime, int userId);
}
```

**Create `Services/TimezoneService.cs`:**

```csharp
public class TimezoneService : ITimezoneService
{
    private readonly AppDbContext _db;

    public TimezoneService(AppDbContext db)
    {
        _db = db;
    }

    public DateOnly GetUserToday(int userId)
    {
        var user = _db.Users.Find(userId);
        var userTimeZone = user?.TimeZone ?? TimeZoneInfo.Local.Id;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(userTimeZone);
        var userNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        return DateOnly.FromDateTime(userNow);
    }

    // ... implement other methods
}
```

**Register service in `Program.cs`:**

```csharp
builder.Services.AddScoped<ITimezoneService, TimezoneService>();
```

### Phase 3: Update Date Logic (3-4 hours)

**Replace all instances of `DateTime.Today` with user-aware logic:**

```csharp
// OLD:
var today = DateOnly.FromDateTime(DateTime.Today);

// NEW:
var userId = User.GetUserId();
var today = _timezoneService.GetUserToday(userId);
```

**Files to update:**
- `Pages/Requests/TimeOff/Create.cshtml.cs`
- `Pages/Calendar/*.cshtml.cs`
- `Pages/Schedule/Index.cshtml.cs`
- `Services/NotificationService.cs`
- `Services/AnalyticsService.cs`

### Phase 4: UI Updates (1 hour)

**Add timezone selector to profile page:**

```html
<!-- File: Pages/My/Profile.cshtml -->
<label>
    Timezone
    <select asp-for="TimeZone">
        <option value="">Use Server Default</option>
        <option value="America/New_York">Eastern Time (US)</option>
        <option value="America/Chicago">Central Time (US)</option>
        <option value="America/Los_Angeles">Pacific Time (US)</option>
        <option value="Europe/London">London (UK)</option>
        <!-- Add more as needed -->
    </select>
</label>
```

**Display user's timezone in UI:**

```html
<!-- Show current time in user's timezone -->
<small>Your timezone: @User.TimeZone ?? "Server Default"</small>
```

### Phase 5: Testing (2 hours)

**Test scenarios:**
1. User in UTC-8 creates shift for "tomorrow" → Verify correct date
2. User in UTC+5 views calendar → Verify dates match their timezone
3. Notification sent at midnight server time → Verify users receive at their midnight
4. User changes timezone → Verify all dates update correctly

---

## Estimated Total Effort

**Total Time:** 9-11 hours
**Complexity:** Medium
**Risk:** Low (backward compatible if implemented with null-check fallback)

---

## Recommendation

**For most deployments:** No action required - current single-timezone architecture is sufficient.

**If you need multi-timezone support:**
1. Follow the implementation guide above
2. Test thoroughly with users in different timezones
3. Consider using a library like `NodaTime` for advanced timezone handling
4. Document timezone requirements in deployment guide

---

## Alternative: Server Configuration Approach

**Simpler option for organizations with multiple offices but within 2-3 timezones:**

Instead of per-user timezone preferences, deploy multiple instances:
- `app-useast.company.com` (UTC-5)
- `app-uswest.company.com` (UTC-8)
- `app-europe.company.com` (UTC+0)

Each instance runs with its own server timezone, users access the appropriate instance for their region.

**Pros:**
- No code changes required
- Simple to implement
- Easier to maintain

**Cons:**
- Separate databases per region
- Users can't switch timezones dynamically
- Requires infrastructure setup

---

---

## 2. DST Transition Duration (2 Days/Year)

**Status:** Mitigated (DST-aware calculation added)
**Impact:** Shift duration calculations on DST transition dates are now UTC-corrected via `TimeHelpers.Hours(ShiftType, DateOnly)`. However, some secondary calculations (analytics, rest hours) may still use naive local time.

### Workaround
Manually verify shift durations on the 2 DST transition dates per year (spring-forward/fall-back).

---

## 3. CSP `unsafe-inline`

**Status:** Known Limitation (Long-term fix: nonce-based CSP)
**Impact:** CSP header allows `unsafe-inline` for scripts, weakening XSS protection.

### Mitigation
- All user-supplied content is sanitized before rendering
- `@Html.Raw()` usage has been audited
- Full nonce-based CSP migration is a future enhancement

---

## 4. No CAPTCHA on Login

**Status:** Accepted by Policy
**Impact:** No CAPTCHA challenge on login attempts. Per policy, lockout mitigation is handled via admin tooling (OwnerHub > Locked Users).

### Mitigation
- Per-IP rate limiting (10 attempts / 15 minutes)
- Per-account rate limiting (15 attempts / 15 minutes)
- Account lockout after 10 failed attempts (3-minute lockout)
- OwnerHub page for viewing and unlocking locked accounts

---

## 5. ~~No User Data Export (GDPR-style)~~ — RESOLVED

**Status:** Resolved (implemented in v3.1.x audit fixes)
**Impact:** Owner-level user data export with batch support is now available.

### Resolution
`Pages/Owner/Hub/ExportUserData` provides single-user and batch multi-user JSON export with selection UI, search/filter, and select-all/deselect-all. Accessible via Owner Hub > Export User Data. Employee self-service export is still not implemented (owner-initiated only).

---

## 6. Calendar Performance with 100+ Users

**Status:** Known Limitation
**Impact:** Calendars with >100 users may experience slow rendering due to DOM element count (no virtual scrolling).

### Mitigation
- Calendar views are scoped by molecule/job type, limiting typical user counts to 20-50
- Consider splitting large molecules if performance degrades

---

## 7. Mobile Calendar Limitations

**Status:** Known Limitation
**Impact:** Calendar fill-handle and inline editing are mouse-centric. Touch devices may have difficulty with small targets.

### Workaround
Use desktop/laptop for calendar management. Mobile view is suitable for viewing only.

---

## 8. Feature Flags Without Cleanup Lifecycle

**Status:** Low Priority
**Impact:** Stale feature flags accumulate over time.

### Mitigation
Feature flags have `CreatedAt`/`UpdatedAt` timestamps for tracking. Periodic manual review recommended.

---

## 9. Onboarding Wizard Not Implemented (B-05)

**Status:** Deferred to next release
**Impact:** New units must manually discover setup steps (shift types, hierarchy, grants, email, users).

### Mitigation
Use Setup Tasks page and Admin documentation. Owner dashboard shows system alerts for misconfiguration.

---

## 10. Database Rollback Requires Manual Procedure (H-03)

**Status:** Known limitation
**Impact:** Rolling back code to a previous version after migrations have run causes EF model mismatch.

### Recovery Procedure
1. Stop the application
2. Restore pre-migration backup from `Backups/app.db.pre-migration-*`
3. Copy restored file to `app.db`
4. Deploy the previous application version
5. Start the application

Pre-migration backups are created automatically before each startup migration.

---

## 11. Database Corruption Recovery (I-02)

**Status:** Documented procedure
**Impact:** SQLite corruption from hardware failure or interrupted writes.

### Recovery Procedure
1. Stop the application immediately
2. Check backup integrity: `sqlite3 Backups/latest.db "PRAGMA integrity_check;"`
3. If backup is clean, restore: copy backup to `app.db`
4. If backup is corrupt, try `.recover` command: `sqlite3 corrupt.db ".recover" | sqlite3 recovered.db`
5. Copy `DataProtection-Keys/` directory alongside the restored database
6. Restart application
7. Verify data in admin diagnostic page

### Prevention
- Daily backups with integrity verification (PRAGMA integrity_check)
- WAL checkpoint monitoring (admin alerts for WAL > 100MB)
- Disk space monitoring (alert at < 20% free)

---

## 12. User Company Transfer Not Supported (A-11/E-05)

**Status:** Not implemented
**Impact:** Changing a user's CompanyId directly orphans their related records (assignments, time-off, chores) behind EF query filters.

### Workaround
Do not modify user CompanyId directly. Instead: deactivate user in old company, create new account in new company.

---

## 13. Tenant Isolation Breach Response (I-05)

**Status:** Process documentation
**Impact:** No automated detection of cross-tenant data leaks.

### Response Procedure
1. Immediately disable the affected user account
2. Check audit logs for the user's recent actions (Admin > Audit Logs)
3. Review SignalR group memberships in server logs (search for "JoinCalendarGroup")
4. Check for `IgnoreQueryFilters()` usage in custom/diagnostic pages
5. Rotate the HMAC secret (Security:ApiKeyHmacSecret) if API keys may be compromised
6. Generate a new DataProtection key set and restart to invalidate all sessions
7. Document the incident and affected data scope

---

**Document Version:** 3.1
**Last Updated:** 2026-03-23
**Related Issues:** TEST-REPORT-ISSUE-005, Pre-Release QA Audit 2026-02, v3.1.x Audit Fixes 2026-03
