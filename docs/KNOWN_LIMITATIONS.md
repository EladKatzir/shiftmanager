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

**Document Version:** 1.0
**Last Updated:** 2026-01-06
**Related Issues:** TEST-REPORT-ISSUE-005
