# Release Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all findings from the forensic release-readiness audit — code defects, UX issues, test coverage gaps, and data integrity improvements.

**Architecture:** Fixes are organized into independent tasks that can be executed in any order. Each task targets a specific file or small set of files. Test-writing tasks follow TDD pattern where applicable.

**Tech Stack:** ASP.NET Core 8.0, Razor Pages, C#, xUnit + FluentAssertions + Moq, SQLite, CSS, JavaScript

---

## File Structure

### Files to Modify
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs:892` — Fix assertion count (expects 126, actual is 127)
- `Services/StoreService.cs:314` — Localize hardcoded `Message = "No hours set"` (the bug: view renders `@store.Message` at Default.cshtml:136 without localization, while badge at line 139 IS localized via `GetStatusLabel()`)
- `Services/WidgetService.cs:121-171` — Add null-check for QuickInfoConfig EntityId resolution (line 121: OnDutyTypeConfig lookup, line 171: Store lookup via StoreService)
- `Pages/Shared/Components/ContextSwitcher/Default.cshtml` — Fix collapsed-sidebar "CUR VIEW" truncation (NOT in _Layout.cshtml — it's the ContextSwitcher component)
- `wwwroot/css/navigation.css:26-68` — Existing collapse pattern uses `.sidebar-text` opacity/width hiding; extend to cover context switcher text
- `Pages/MyTeam/Index.cshtml:857,877` — Fix "Loading..." flash (`<loc key="Loading" />...` in calendar title and week range)
- `wwwroot/js/myteam.js:~153` — `loadWeekView()` calls `formatWeekRange()` after API response; needs earlier initialization
- `Pages/Admin/Users.cshtml.cs:1479-1660` — User deletion already does full cascading cleanup; improve generic error message at line 1656
- `Pages/Admin/Users.cshtml:475-479,700-704,1014-1023` — Error display already exists via TempData["ErrorMessage"] and Model.Error; confirm delete uses inline JS confirm()
- `Data/SeedData/GrantTypeSeed.cs` — (read-only, count reference: 127 `id++` calls)
- `Resources/SharedResources.resx` — Verify localization keys for store status messages
- `Resources/SharedResources.he-IL.resx` — Verify Hebrew translations match

### Files to Create
- `ShiftManager.Tests/UnitTests/Services/ShiftValidationTests.cs` — Tests for overlap, rest period, weekly cap
- `ShiftManager.Tests/UnitTests/Services/SwapRequestLifecycleTests.cs` — Tests for swap request workflow
- `ShiftManager.Tests/UnitTests/Services/TimeOffConflictTests.cs` — Tests for time-off overlap detection
- `Data/SeedData/OnDutyTypeSeed.cs` — Seed Backup-hakam duty type (TypeValue=2)

### Files to Read (Reference Only)
- `Services/ShiftAssignmentService.cs:179` — `ValidateShiftAssignmentAsync` with 18 validation checks (OVERLAP at line 321, REST_HOURS at 338, WEEKLY_CAP at 444)
- `Services/VacationApprovalService.cs` — 14 public methods, 723 lines. Key: `ApproveAsync` checks overlapping approved requests (line 209-224)
- `Models/SwapRequest.cs` — 11 properties: Id, CompanyId, FromAssignmentId, ToAssignmentId?, FromUserId, ToUserId?, Status, Reason, DeclineReason, CreatedAt, ReviewedAt, ReviewedBy
- `Models/TimeOffRequest.cs` — 10 properties with `GetActualStartDateTime()`/`GetActualEndDateTime()` utility methods for Vacation vs After types
- `ViewComponents/OnCallWidgetViewComponent.cs:138` — StoreStatus has `Status` (enum string) and `Message` (the hardcoded text)
- `Pages/Shared/Components/OnCallWidget/Default.cshtml:136` — `@store.Message` renders the hardcoded English; line 139 renders localized badge
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs` — 6 existing tests; setup pattern: InMemory DB, Moq for IHierarchySettingsService (returns RestHours:11, WeeklyCap:48), IStringLocalizer, ILogger, IAuditLogService, IConfiguration, IAppConfigCacheService
- `ShiftManager.Tests/UnitTests/Services/VacationApprovalServiceTests.cs` — 4 existing tests; setup: InMemory DB, Moq for IGrantService, INotificationService, ITraineeService, ILogger
- `Pages/Admin/Organization/DutyTypes/Index.cshtml.cs` — Hakam(0) and Lead(1) are hard-coded defaults in the page model, NOT in a seed file

---

## Task 1: Fix Failing GrantType Seed Count Test

**Files:**
- Modify: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs:892`

**⚠️ REVIEW NOTE:** Code Architect found the seed has 126 `id++` calls and the test already expects 126. However, the audit test run showed 1 failure on this exact test. Before changing anything:

- [ ] **Step 1: Verify the test actually fails**

Run:
```bash
dotnet test --no-build --filter "GrantTypeSeed_Creates_ExpectedNumberOfGrants" -v n
```

**If PASS:** The test is already correct. Skip to Step 5 (no changes needed).
**If FAIL:** Note the actual count from the error message (e.g., "Expected 126, but found 127").

- [ ] **Step 2: Get the definitive count**

Run both:
```bash
grep -c "id++" Data/SeedData/GrantTypeSeed.cs
grep -c "new GrantType" Data/SeedData/GrantTypeSeed.cs
```

The `id++` count is authoritative (each grant increments the counter). If counts disagree, trace manually.

- [ ] **Step 3: Update the assertion to match actual count**

In `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` line 892, update the `HaveCount(N)` value to match the actual count from Step 2. Update the comment on line 891 accordingly.

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test --no-build --filter "GrantTypeSeed_Creates_ExpectedNumberOfGrants"
```
Expected: PASS

- [ ] **Step 5: Run all tests**

```bash
dotnet test --no-build
```
Expected: 363 passed, 0 failed

- [ ] **Step 5: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs
git commit -m "fix: update GrantType seed count assertion to 127"
```

---

## Task 2: Fix Quick Info Widget Mixed-Language Bug

**Files:**
- Modify: `Services/StoreService.cs:314`

**Root cause:** `StoreService.GetStoreStatusAsync()` sets `Message = "No hours set"` as a hardcoded English string (line 314). The widget template at `Pages/Shared/Components/OnCallWidget/Default.cshtml:136` renders `@store.Message` directly, bypassing localization. The badge label at line 139 IS localized via `GetStatusLabel()`, causing both English message and Hebrew badge to appear simultaneously.

**Fix approach:** The `Message` property should use the same status key pattern. Since the widget already localizes status via `GetStatusLabel()`, we should make `Message` use the localized label from the view. The cleanest fix is to stop using `Message` for the "no hours set" case and let the view handle it via the existing `GetStatusLabel()` function.

However, `Message` is also used for time-based messages like "Opens at 08:00" and "Break until 14:00" which DO need to be constructed in the service. So we should localize the `Message` for the `NoHoursSet` case specifically.

- [ ] **Step 1: Inject IStringLocalizer into StoreService**

The constructor at line 18 currently is:
```csharp
public StoreService(AppDbContext db, IMemoryCache cache, ILogger<StoreService> logger)
{
    _db = db;
    _cache = cache;
    _logger = logger;
}
```

Change it to:
```csharp
public StoreService(AppDbContext db, IMemoryCache cache, ILogger<StoreService> logger, IStringLocalizer<SharedResources> localizer)
{
    _db = db;
    _cache = cache;
    _logger = logger;
    _localizer = localizer;
}
```

Add field declaration near the top:
```csharp
private readonly IStringLocalizer<SharedResources> _localizer;
```

Add using if not present:
```csharp
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
```

No DI registration change needed — `StoreService` is already registered via `AddScoped<IStoreService, StoreService>()` and constructor injection auto-resolves.

- [ ] **Step 3: Remove `static` from `ComputeStatusFromHours`**

**⚠️ REVIEW CORRECTION:** `ComputeStatusFromHours` is `private static` (line 296). A `static` method cannot access `_localizer`. Remove the `static` modifier:

```csharp
// Change from:
private static StoreStatus ComputeStatusFromHours(...)
// To:
private StoreStatus ComputeStatusFromHours(...)
```

This allows the method to access `_localizer` for localized messages.

- [ ] **Step 4: Replace ALL hardcoded Message strings**

**⚠️ REVIEW CORRECTION:** Code Architect found 5 hardcoded English messages, not just 1:

| Line | Current Hardcoded | Localization Key | English Value | Hebrew Value |
|------|-------------------|-----------------|---------------|--------------|
| 314 | `"No hours set"` | `Widget_NoHoursSet` | Already exists | Already exists |
| 327 | `"Closed — opens {time}"` | `Widget_StoreClosedOpensAt` | `Closed — opens {0}` | `סגור — נפתח ב-{0}` |
| 344 | `"Open until {time}"` | `Widget_StoreOpenUntil` | `Open until {0}` | `פתוח עד {0}` |
| 360 | `"Break — reopens {time}"` | `Widget_StoreBreakReopens` | `Break — reopens {0}` | `הפסקה — נפתח מחדש ב-{0}` |
| 376 | `"Closed"` | `Widget_StoreClosed` | Already exists | Already exists |
| 380 | `"Closed — opens {dayName} {time}"` | `Widget_StoreClosedOpensDay` | `Closed — opens {0} {1}` | `סגור — נפתח ב{0} {1}` |

Replace each with `_localizer["KeyName", arg1, arg2].Value` using the corresponding key.

- [ ] **Step 5: Add new localization keys to both .resx files**

Add the 4 NEW keys (Widget_StoreClosedOpensAt, Widget_StoreOpenUntil, Widget_StoreBreakReopens, Widget_StoreClosedOpensDay) to both `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx` with the values from the table above. The 2 existing keys (Widget_NoHoursSet, Widget_StoreClosed) are already present.

- [ ] **Step 5: Verify localization keys exist in both .resx files**

Check that `Widget_NoHoursSet` exists in both:
- `Resources/SharedResources.resx`
- `Resources/SharedResources.he-IL.resx`

If any new keys are needed for other Message strings (StoreOpensAt, StoreClosesAt, etc.), add them to both files.

- [ ] **Step 6: Build and verify**

Run:
```bash
dotnet build
```
Expected: 0 warnings, 0 errors

- [ ] **Step 7: Commit**

```bash
git add Services/StoreService.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix: localize store hours messages in Quick Info widget"
```

---

## Task 3: Fix QuickInfoConfig Orphan Risk

**Files:**
- Modify: `Services/WidgetService.cs:86-187` — `BuildMoleculeBasedWidgetAsync` method
- Modify: `Services/StoreService.cs` — `ComputeStoreStatusAsync` method

**⚠️ REVIEW CORRECTION:** Code Architect found that `ComputeStoreStatusAsync` (line 171) never returns null — it always returns a valid `StoreStatus`. The plan's original `if (store == null) continue` pattern won't compile because there is no `stores` variable — the code calls `_storeService.ComputeStoreStatusAsync(item.EntityId, DateTime.Now)` directly.

The OnDutyTypeConfig path (line 121-125) already handles null with a fallback: `dutyTypeConfig != null ? ... : "On-Call"`.

**Actual orphan risk:** If a Store is deleted, `ComputeStoreStatusAsync` queries a store that no longer exists. The method loads the store via `_context.Stores.FirstOrDefaultAsync(s => s.Id == storeId)`. If null, it returns a `StoreStatus` with empty `StoreName`. The widget then renders an unnamed store entry.

**Fix approach:**
1. In `StoreService.ComputeStoreStatusAsync`, return null when the store doesn't exist
2. In `WidgetService.BuildMoleculeBasedWidgetAsync`, skip null store results

- [ ] **Step 1: Change return type to nullable in interface**

In `Services/IStoreService.cs` line 25, change:
```csharp
Task<StoreStatus> ComputeStoreStatusAsync(int storeId, DateTime now);
```
To:
```csharp
Task<StoreStatus?> ComputeStoreStatusAsync(int storeId, DateTime now);
```

- [ ] **Step 2: Add early-return null in implementation**

In `Services/StoreService.cs`, inside `ComputeStoreStatusAsync` (line 259), after the store lookup, add a null check. Find the line that loads the store (e.g., `var store = await _db.Stores...FirstOrDefaultAsync(...)`) and add immediately after:
```csharp
if (store == null) return null; // Store was deleted — orphaned QuickInfoConfig
```

Also update the method return type in `StoreService.cs` to match the interface:
```csharp
public async Task<StoreStatus?> ComputeStoreStatusAsync(int storeId, DateTime now)
```

**Caller verification:** Only 1 caller exists — `Services/WidgetService.cs` line 171. No other files call this method.

- [ ] **Step 3: Filter null results in WidgetService**

In `Services/WidgetService.cs` line 171, change:
```csharp
var storeStatus = await _storeService.ComputeStoreStatusAsync(item.EntityId, DateTime.Now);
storeStatuses.Add(storeStatus);
```
To:
```csharp
var storeStatus = await _storeService.ComputeStoreStatusAsync(item.EntityId, DateTime.Now);
if (storeStatus != null)
    storeStatuses.Add(storeStatus);
```

- [ ] **Step 4: Build and verify**

Run:
```bash
dotnet build
```
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Commit**

```bash
git add Services/StoreService.cs Services/IStoreService.cs Services/WidgetService.cs
git commit -m "fix: gracefully handle deleted stores in Quick Info widget"
```

---

## Task 4: Fix Sidebar "Currently Viewing" Truncation

**Files:**
- Modify: `Pages/Shared/Components/ContextSwitcher/Default.cshtml` — the context area component
- Modify: `wwwroot/css/navigation.css` — collapsed sidebar styles

**Root cause:** When sidebar is collapsed (64px width), the ContextSwitcher component's "Currently Viewing" text and company badge get truncated to "CUR VIEW" instead of being hidden. The existing collapse pattern at `navigation.css:26-68` uses `.sidebar-text` class with `opacity: 0; width: 0; overflow: hidden;` to hide text, and `.sidebar-initially-collapsed` applies `display: none` to `.sidebar-text`, `.sidebar-brand__text`, `.sidebar-user__info`, `.sidebar-user__menu-icon`, `.sidebar-shortcut`. The ContextSwitcher text elements are NOT included in this list.

- [ ] **Step 1: Add ContextSwitcher text classes to collapse rules**

The ContextSwitcher component (`Pages/Shared/Components/ContextSwitcher/Default.cshtml`) has these text elements that need hiding when collapsed:
- `.context-switcher__label` (lines 36, 44, 64, 72) — contains "Currently Viewing" text
- `.context-switcher__name` (lines 53, 97) — contains company/context display name
- `.context-switcher__count` (line 122) — total context count
- `.context-switcher__static` — wrapper for single-context display

In `wwwroot/css/navigation.css`, add to the `.sidebar-initially-collapsed` block (around line 44-52):

```css
.sidebar-initially-collapsed .app-sidebar .context-switcher__label,
.sidebar-initially-collapsed .app-sidebar .context-switcher__name,
.sidebar-initially-collapsed .app-sidebar .context-switcher__count,
.sidebar-initially-collapsed .app-sidebar .context-switcher__static {
    opacity: 0;
    width: 0;
    overflow: hidden;
    display: none;
}
```

Add to the `.app-sidebar.is-collapsed` block (around line 26-34):

```css
.app-sidebar.is-collapsed .context-switcher__label,
.app-sidebar.is-collapsed .context-switcher__name,
.app-sidebar.is-collapsed .context-switcher__count,
.app-sidebar.is-collapsed .context-switcher__static {
    opacity: 0;
    width: 0;
    overflow: hidden;
}
```

- [ ] **Step 2: Center the context area icon when collapsed**

```css
.app-sidebar.is-collapsed .context-switcher {
    justify-content: center;
    padding: var(--space-2);
}

.sidebar-initially-collapsed .app-sidebar .context-switcher {
    justify-content: center;
    padding: var(--space-2);
}
```

- [ ] **Step 4: Build and verify**

Run:
```bash
dotnet build
```
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Browser-test collapsed sidebar**

Launch app, collapse sidebar, verify:
- No truncated "CUR VIEW" text
- Icon still visible as compact indicator
- Expanding sidebar restores full "Currently Viewing" text + company name

- [ ] **Step 6: Commit**

```bash
git add Pages/Shared/Components/ContextSwitcher/Default.cshtml wwwroot/css/navigation.css
git commit -m "fix: hide context switcher text when sidebar is collapsed"
```

---

## Task 5: Fix MyTeam "Loading..." Text Flash

**Files:**
- Modify: `Pages/MyTeam/Index.cshtml:857,877`
- Modify: `wwwroot/js/myteam.js:~153`

**Root cause:** Two elements show `<loc key="Loading" />...` as initial content:
- Line 857: `<h2 class="calendar-title" id="calendarName"><loc key="Loading" />...</h2>`
- Line 877: `<div class="week-range" id="weekRange"><loc key="Loading" />...</div>`

The JS function `loadWeekView()` (myteam.js ~line 153) calls `formatWeekRange(currentWeekStart)` to populate `weekRange` AFTER the API response comes back. The delay causes the flash.

- [ ] **Step 1: Replace loading text with invisible placeholders**

**⚠️ REVIEW CORRECTION:** Both reviewers flagged that a server-rendered week range would use `DateTime.Today.DayOfWeek` (Sunday=0) but the app has configurable `WeekStartDay` via `IAppConfigCacheService`. Using `&nbsp;` for both elements avoids this mismatch entirely — JS will populate the correct dates immediately on load.

In `Pages/MyTeam/Index.cshtml`, replace line 857:
```html
<h2 class="calendar-title" id="calendarName"><loc key="Loading" />...</h2>
```
With:
```html
<h2 class="calendar-title" id="calendarName">&nbsp;</h2>
```

Replace line 877:
```html
<div class="week-range" id="weekRange"><loc key="Loading" />...</div>
```
With:
```html
<div class="week-range" id="weekRange">&nbsp;</div>
```

Both elements preserve their layout height with `&nbsp;` but show no misleading text. JS populates them within milliseconds.

- [ ] **Step 3: Build and verify**

Run:
```bash
dotnet build
```
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add Pages/MyTeam/Index.cshtml
git commit -m "fix: eliminate loading text flash on MyTeam date navigation"
```

---

## Task 6: Delete Orphaned Test Company Data

**Files:**
- Database operation (not code change)

**Context:** The Organization page shows "Test Company" (6 users) as an unassigned company — this is leftover test data, not a real company.

- [ ] **Step 1: Back up the database**

**⚠️ REVIEW ADDITION:** QA reviewer flagged this as a destructive operation needing a backup.

```bash
cp app.db app.db.pre-cleanup-$(date +%Y%m%d)
```

- [ ] **Step 2: Identify the test company in the database**

Use the Owner DatabaseConsole page or direct SQLite access to find the company:
```sql
SELECT Id, Name, MoleculeId FROM Companies WHERE Name = 'Test Company';
```

- [ ] **Step 3: Check for dependent data**

```sql
SELECT COUNT(*) FROM Users WHERE CompanyId = <id>;
SELECT COUNT(*) FROM ShiftAssignments WHERE CompanyId = <id>;
SELECT COUNT(*) FROM TimeOffRequests WHERE CompanyId = <id>;
```

- [ ] **Step 4: Clean up or reassign**

If the company has no critical data, delete it and its users. If it has data worth preserving, assign it to a molecule instead. Document the deleted IDs for auditability.

This is a data operation, not a code change — no commit needed.

---

## Task 7: Improve User Deletion Error Message

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs:1479-1660` — delete handler
- Modify: `Pages/Admin/Users.cshtml` — error display already exists at lines 475-479 and 700-704
- Modify: `Resources/SharedResources.resx` — improve error message key
- Modify: `Resources/SharedResources.he-IL.resx` — Hebrew translation

**Context:** The delete handler at `OnPostDeleteUserAsync` (lines 1479-1660) ALREADY does comprehensive cascading cleanup:
- Lines 1512-1600: Manually deletes swap requests, shift assignments, time-off requests, grants, DirectorCompany mappings, audit records, chores, on-duty entries, game scores
- Lines 1602-1610: Nullifies AuditLog.UserId, DutyRotationLog refs, ApprovalRule refs
- Lines 1612-1625: Reassigns CreatedBy/UpdatedBy for shared config records
- Line 1650: Hard deletes the user
- Lines 1653-1660: Generic catch block sets `Error = _localizer["Error_DeletingUser"]`

The error UI already exists (lines 475-479, 700-704 with `TempData["ErrorMessage"]` and `Model.Error`). The improvement needed is making the generic `Error_DeletingUser` message more specific about what went wrong.

- [ ] **Step 1: Read the current error message key**

Check what `Error_DeletingUser` says in both .resx files. If it's too generic (e.g., "An error occurred"), make it more descriptive.

- [ ] **Step 2: Improve the error message**

In `Resources/SharedResources.resx`, update or add:
```xml
<data name="Error_DeletingUser" xml:space="preserve">
  <value>Failed to delete this user. The user may have data that could not be automatically cleaned up. Please try again or contact a system administrator.</value>
</data>
```

In `Resources/SharedResources.he-IL.resx`, update or add:
```xml
<data name="Error_DeletingUser" xml:space="preserve">
  <value>מחיקת המשתמש נכשלה. ייתכן שלמשתמש יש נתונים שלא ניתן לנקות אוטומטית. נסה שוב או פנה למנהל מערכת.</value>
</data>
```

- [ ] **Step 3: Add specific FK error handling as fallback**

In the catch block at lines 1653-1660, add a more specific check before the generic error:
**⚠️ REVIEW FIX:** Use SQLite error code instead of fragile string matching. The codebase already uses this pattern at `Pages/Api/Hierarchy/Create.cshtml.cs` line 222:

```csharp
catch (DbUpdateException dbEx) when (dbEx.InnerException is Microsoft.Data.Sqlite.SqliteException sqliteEx
    && sqliteEx.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (includes FK violations)
{
    await transaction.RollbackAsync();
    _logger.LogError(dbEx, "FK constraint prevented deleting user {UserId}", id);
    Error = _localizer["Admin_UserDeleteBlockedByDependencies"];
    await OnGetAsync();
    return Page();
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    _logger.LogError(ex, "Error deleting user {UserId}", id);
    Error = _localizer["Error_DeletingUser"];
    await OnGetAsync();
    return Page();
}
```

Add using at the top of the file if not present:
```csharp
using Microsoft.Data.Sqlite;
```

And add the new key to both .resx files:
```xml
<!-- SharedResources.resx -->
<data name="Admin_UserDeleteBlockedByDependencies" xml:space="preserve">
  <value>Cannot delete this user because they have records that could not be automatically cleaned up. Please contact a system administrator for assistance.</value>
</data>

<!-- SharedResources.he-IL.resx -->
<data name="Admin_UserDeleteBlockedByDependencies" xml:space="preserve">
  <value>לא ניתן למחוק משתמש זה מכיוון שיש לו רשומות שלא ניתן לנקות אוטומטית. אנא פנה למנהל מערכת לסיוע.</value>
</data>
```

- [ ] **Step 4: Build and verify**

Run:
```bash
dotnet build
```
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Commit**

```bash
git add Pages/Admin/Users.cshtml.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix: improve user deletion error messages with specific FK violation handling"
```

---

## Task 8: Seed Backup-Hakam Duty Type

**Files:**
- Create: `Data/SeedData/OnDutyTypeSeed.cs` (if doesn't exist) OR modify existing seed
- Modify: `Program.cs` — call the seed during startup (if not already)

**Context:** Custom duty types like "Backup-hakam" (🛡️) are created via admin UI but have no seed backup. If deleted, no restore path exists. "Daily King" is test data and should NOT be seeded.

- [ ] **Step 1: Check if OnDutyTypeConfig seed already exists**

Search for existing seed files:
```bash
grep -r "OnDutyTypeConfig" Data/SeedData/
grep -r "OnDutyTypeSeed" Program.cs
```

Also check how Hakam(0) and Lead(1) are created — they may be in a migration.

- [ ] **Step 2: Verify TypeValue=2 is not already taken**

Check the database for existing custom duty types:
```bash
sqlite3 app.db "SELECT TypeValue, NameEn, NameHe FROM OnDutyTypeConfigs ORDER BY TypeValue;"
```

If TypeValue=2 already exists (created via admin UI), the idempotent seed check `if (!existingDutyTypes.Contains(dt.TypeValue))` will skip insertion. This is safe — just verify it's the right entity.

The `OnDutyTypeConfig` model fields are: `Id`, `TypeValue`, `NameEn`, `NameHe`, `Icon`, `Color`, `IsActive`, `RequiresOfficerRank`, `CreatedAt` (DateTime), `CreatedBy` (int, non-nullable — use 0 for system seed).

- [ ] **Step 3: Create or update the seed**

If no seed exists, create `Data/SeedData/OnDutyTypeSeed.cs`:

**⚠️ REVIEW CORRECTION:** Both reviewers found the plan used wrong field names. The actual model (`Models/OnDutyTypeConfig.cs`) uses `NameEn` (not `Name`) and has NO `IsBuiltIn` property. Required fields: `TypeValue`, `NameEn`, `NameHe`, `Icon`, `Color`, `IsActive`, `RequiresOfficerRank`, `CreatedAt`, `CreatedBy`.

```csharp
using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

public static class OnDutyTypeSeed
{
    public static List<OnDutyTypeConfig> GetOnDutyTypes()
    {
        var now = DateTime.UtcNow;
        return new List<OnDutyTypeConfig>
        {
            new() { TypeValue = 2, NameEn = "Backup-hakam", NameHe = "חק\"מ רזרבה", Icon = "🛡️", Color = "#6B8E23", IsActive = true, RequiresOfficerRank = false, CreatedAt = now, CreatedBy = 0 },
        };
    }
}
```

Note: Hakam(0) and Lead(1) are NOT seeded here — they are hard-coded defaults in `Pages/Admin/Organization/DutyTypes/Index.cshtml.cs` and created via admin UI interaction. Only seeding the Backup-hakam custom type that has no other creation path.

- [ ] **Step 4: Wire up seeding in Program.cs**

If not already present, add idempotent seeding in the startup seed section of `Program.cs`:
```csharp
// Seed OnDutyTypeConfigs
var existingDutyTypes = await db.OnDutyTypeConfigs.Select(d => d.TypeValue).ToListAsync();
foreach (var dt in OnDutyTypeSeed.GetOnDutyTypes())
{
    if (!existingDutyTypes.Contains(dt.TypeValue))
        db.OnDutyTypeConfigs.Add(dt);
}
await db.SaveChangesAsync();
```

- [ ] **Step 5: Build and run**

Run:
```bash
dotnet build && dotnet run --urls "http://localhost:5000" &
```
Check startup logs for seed activity. Verify no errors.

- [ ] **Step 6: Commit**

```bash
git add Data/SeedData/OnDutyTypeSeed.cs Program.cs
git commit -m "feat: seed Backup-hakam duty type for resilient on-duty configuration"
```

---

## Task 9: Write Shift Validation Unit Tests (Overlap, Rest Period, Weekly Cap)

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/ShiftValidationTests.cs`
- Read: `Services/ShiftAssignmentService.cs` — `ValidateShiftAssignmentAsync` method

**Context:** The `ValidateShiftAssignmentAsync` method in `ShiftAssignmentService` performs multiple validation checks (overlap, rest period 8h default, weekly cap 56h default). These rules have NO unit tests currently. Per project memory:
- **Errors** (hard blocks): Overlap, Rest period (8h default), User not found, Molecule boundary, Duplicate
- **Warnings** (overrideable): Vacation, Chore, On-duty, Weekly cap (56h default), Job type mismatch, Shift grouping, Tech shift, Past date
- Exempt shifts (`IsOffline || IsHome`) skip overlap, rest, weekly cap, and past-date checks

- [ ] **Step 1: Understand ValidateShiftAssignmentAsync contract**

`ValidateShiftAssignmentAsync` at `Services/ShiftAssignmentService.cs` line 179:
```csharp
public async Task<ShiftAssignmentValidation> ValidateShiftAssignmentAsync(int userId, int shiftInstanceId)
```

Returns `ShiftAssignmentValidation` with:
- `CanAssign` (bool) — true if `Errors.Count == 0`
- `Errors` (List<ValidationIssue>) — hard blocks (18 checks, see Step 4 for all codes)
- `Warnings` (List<ValidationIssue>) — overrideable via HMAC token

Each `ValidationIssue` has a `Key` string property (the error code).

Dependencies to mock: `IHierarchySettingsService`, `IAppConfigCacheService`, `IStringLocalizer<SharedResources>`, `ILogger<ShiftAssignmentService>`, `IAuditLogService`, `IConfiguration`

- [ ] **Step 2: Set up test class matching existing pattern**

The existing `ShiftAssignmentServiceTests.cs` uses:
```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
```

Setup pattern:
- InMemory DB: `new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())`
- Mock `IHierarchySettingsService` returning `EffectiveSettings { RestHours = 8, WeeklyCap = 56 }` (use 8/56 for these tests, not the existing tests' 11/48)
- Mock `IAppConfigCacheService` returning `WeekStartDay = DayOfWeek.Sunday`
- `Mock.Of<IStringLocalizer<SharedResources>>()` for localizer
- Implements `IDisposable` with `_db.Dispose()`

- [ ] **Step 3: Create the test file with setup**

Create `ShiftManager.Tests/UnitTests/Services/ShiftValidationTests.cs` with the class structure and setup matching the existing pattern. Include test data helpers for creating ShiftTypes, ShiftInstances, ShiftAssignments, and Users.

- [ ] **Step 4: Write all validation tests**

The `ValidateShiftAssignmentAsync` method has 18 validation checks. Write one test per check plus boundary tests. All tests call `ValidateShiftAssignmentAsync(userId, shiftInstanceId)` and assert on the returned `ShiftAssignmentValidation` object's `Errors` and `Warnings` lists. Each list item has a `Key` property with the error code string.

**Mock setup for all tests:**
```csharp
// IHierarchySettingsService returns configurable rest/cap values
_hierarchySettingsMock.Setup(x => x.GetEffectiveSettingsAsync(It.IsAny<int>()))
    .ReturnsAsync(new EffectiveSettings { RestHours = 8, WeeklyCap = 56, RestHoursSource = "Test", WeeklyCapSource = "Test" });

// IAppConfigCacheService returns Sunday as week start (default)
_appConfigCacheMock.Setup(x => x.GetWeekStartDayAsync())
    .ReturnsAsync(DayOfWeek.Sunday);
```

**Error tests (hard blocks):**

```csharp
// 1. USER_NOT_FOUND — user doesn't exist
[Fact]
public async Task Validate_UserNotFound_ReturnsError()
{
    // Arrange: shiftInstance exists, userId=9999 doesn't exist in DB
    // Assert: validation.Errors.Any(e => e.Key == "USER_NOT_FOUND")
}

// 2. SHIFT_NOT_FOUND — shift instance doesn't exist
[Fact]
public async Task Validate_ShiftNotFound_ReturnsError()
{
    // Arrange: user exists, shiftInstanceId=9999 doesn't exist in DB
    // Assert: validation.Errors.Any(e => e.Key == "SHIFT_NOT_FOUND")
}

// 3. USER_NOT_IN_MOLECULE — user's company in different molecule
[Fact]
public async Task Validate_UserNotInMolecule_ReturnsError()
{
    // Arrange: user in Company A (Molecule 1), shift in Molecule 2
    // Assert: validation.Errors.Any(e => e.Key == "USER_NOT_IN_MOLECULE")
}

// 4. ALREADY_ASSIGNED — duplicate assignment
[Fact]
public async Task Validate_AlreadyAssigned_ReturnsError()
{
    // Arrange: user already has ShiftAssignment for this shiftInstanceId
    // Assert: validation.Errors.Any(e => e.Key == "ALREADY_ASSIGNED")
}

// 5. COMPANY_INELIGIBLE — company not in eligible list
[Fact]
public async Task Validate_CompanyIneligible_ReturnsError()
{
    // Arrange: ShiftType has EligibleCompanyIds set, user's company NOT in list
    // Assert: validation.Errors.Any(e => e.Key == "COMPANY_INELIGIBLE")
}

// 6. OFFICER_RANK_REQUIRED — shift needs officer, user isn't one
[Fact]
public async Task Validate_OfficerRankRequired_ReturnsError()
{
    // Arrange: ShiftType.RequiresOfficerRank = true, user has no officer rank
    // Assert: validation.Errors.Any(e => e.Key == "OFFICER_RANK_REQUIRED")
}

// 7. OVERLAP — time overlap with existing assignment (line 331)
[Fact]
public async Task Validate_OverlappingShift_ReturnsError()
{
    // Arrange: user assigned to Morning (08:00-16:00) on March 30
    //          new shift is Afternoon (12:00-20:00) on March 30 (overlaps 12:00-16:00)
    // Assert: validation.Errors.Any(e => e.Key == "OVERLAP")
}

// 8. OVERLAP negative — adjacent shifts don't overlap
[Fact]
public async Task Validate_AdjacentShifts_NoOverlapError()
{
    // Arrange: user assigned to Morning (08:00-16:00) on March 30
    //          new shift is Afternoon (16:00-00:00) on March 30 (no overlap)
    // Assert: !validation.Errors.Any(e => e.Key == "OVERLAP")
}

// 9. REST_HOURS_VIOLATION — insufficient rest (line 366)
[Fact]
public async Task Validate_InsufficientRest_ReturnsError()
{
    // Arrange: user assigned to Night (00:00-08:00) on March 30
    //          new shift is Afternoon starting 14:00 on March 30 (6h rest, need 8h)
    //          Mock RestHours = 8
    // Assert: validation.Errors.Any(e => e.Key == "REST_HOURS_VIOLATION")
}

// 10. REST_HOURS_VIOLATION boundary — exactly at threshold passes
[Fact]
public async Task Validate_ExactRestThreshold_NoError()
{
    // Arrange: user assigned to Night (00:00-08:00) on March 30
    //          new shift starts at 16:00 on March 30 (8h rest, exactly meets 8h threshold)
    //          Mock RestHours = 8
    // Assert: !validation.Errors.Any(e => e.Key == "REST_HOURS_VIOLATION")
}

// 11. DUPLICATE_HOME — two HOME shifts same date (line 400)
[Fact]
public async Task Validate_DuplicateHome_ReturnsError()
{
    // Arrange: user has HOME assignment on March 30
    //          try to assign another HOME on March 30
    // Assert: validation.Errors.Any(e => e.Key == "DUPLICATE_HOME")
}
```

**Warning tests (overrideable):**

```csharp
// 12. JOB_TYPE_MISMATCH (line 251) — already tested in existing tests, skip if redundant

// 13. NOT_IN_SHIFT_GROUPING (line 266) — already tested in existing tests, skip if redundant

// 14. HOME_CONFLICT — real shift on HOME day (line 378)
[Fact]
public async Task Validate_HomeConflict_ReturnsWarning()
{
    // Arrange: user has HOME assignment on March 30
    //          try to assign Morning (08:00-16:00) on March 30
    // Assert: validation.Warnings.Any(w => w.Key == "HOME_CONFLICT")
}

// 15. SHIFT_EXISTS_CONFLICT — HOME on day with real shifts (line 391)
[Fact]
public async Task Validate_ShiftExistsConflict_ReturnsWarning()
{
    // Arrange: user has Morning (08:00-16:00) on March 30
    //          try to assign HOME on March 30
    // Assert: validation.Warnings.Any(w => w.Key == "SHIFT_EXISTS_CONFLICT")
}

// 16. VACATION_CONFLICT (line 415)
[Fact]
public async Task Validate_VacationConflict_ReturnsWarning()
{
    // Arrange: user has approved TimeOffRequest covering March 30
    //          try to assign shift on March 30
    // Assert: validation.Warnings.Any(w => w.Key == "VACATION_CONFLICT")
}

// 17. CHORE_CONFLICT (line 428)
[Fact]
public async Task Validate_ChoreConflict_ReturnsWarning()
{
    // Arrange: user has active Chore (CanceledAt == null) on March 30
    //          try to assign shift on March 30
    // Assert: validation.Warnings.Any(w => w.Key == "CHORE_CONFLICT")
}

// 18. ONDUTY_CONFLICT (line 440)
[Fact]
public async Task Validate_OnDutyConflict_ReturnsWarning()
{
    // Arrange: user has active OnDuty (CanceledAt == null) on March 30
    //          try to assign shift on March 30
    // Assert: validation.Warnings.Any(w => w.Key == "ONDUTY_CONFLICT")
}

// 19. EXCEEDS_WEEKLY_CAP — weekly hours > 56 (line 478, uses > not >=)
[Fact]
public async Task Validate_ExceedsWeeklyCap_ReturnsWarning()
{
    // Arrange: user has 49 hours of shifts this week (Sun-Sat)
    //          try to assign 8-hour shift (total = 57, exceeds 56 cap)
    //          Mock WeeklyCap = 56
    // Assert: validation.Warnings.Any(w => w.Key == "EXCEEDS_WEEKLY_CAP")
}

// 20. EXCEEDS_WEEKLY_CAP boundary — exactly at cap does NOT trigger (> not >=)
[Fact]
public async Task Validate_ExactlyAtWeeklyCap_NoWarning()
{
    // Arrange: user has 48 hours of shifts this week
    //          try to assign 8-hour shift (total = 56, exactly at cap)
    //          Mock WeeklyCap = 56
    // Assert: !validation.Warnings.Any(w => w.Key == "EXCEEDS_WEEKLY_CAP")
}

// 21. PAST_DATE (line 489)
[Fact]
public async Task Validate_PastDate_ReturnsWarning()
{
    // Arrange: shift date is yesterday
    // Assert: validation.Warnings.Any(w => w.Key == "PAST_DATE")
}
```

**Exemption tests:**

```csharp
// 22. Exempt shifts skip OVERLAP, REST_HOURS_VIOLATION, EXCEEDS_WEEKLY_CAP, PAST_DATE
[Fact]
public async Task Validate_OfflineShift_SkipsOverlapRestCapPast()
{
    // Arrange: user has overlapping shift, insufficient rest, exceeds weekly cap, past date
    //          new shift type has IsOffline = true
    // Assert: no OVERLAP, REST_HOURS_VIOLATION, EXCEEDS_WEEKLY_CAP errors/warnings
}

[Fact]
public async Task Validate_HomeShift_SkipsOverlapRestCapPast()
{
    // Arrange: same as above but IsHome = true
    // Assert: same — no OVERLAP, REST_HOURS_VIOLATION, EXCEEDS_WEEKLY_CAP, PAST_DATE
}
```

- [ ] **Step 5: Run all validation tests**

Run:
```bash
dotnet test --filter "ShiftValidationTests"
```
Expected: ALL PASS (20+ tests)

- [ ] **Step 11: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/ShiftValidationTests.cs
git commit -m "test: add unit tests for shift overlap, rest period, and weekly cap validation"
```

---

## Task 10: Write AssignShiftAsync Service Tests (Swap Approval Path)

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/AssignShiftAsyncTests.cs`
- Read: `Services/ShiftAssignmentService.cs` — `AssignShiftAsync` method (line ~604)
- Read: `Models/SwapRequest.cs` — model structure (11 properties)

**⚠️ REVIEW REDESIGN:** Both reviewers found that swap request logic lives entirely in page handlers (`Pages/Requests/Index.cshtml.cs`), NOT in a service. There is no `SwapRequestService`. The page handler calls `_assignmentService.ValidateShiftAssignmentAsync()` then directly reassigns `assign.UserId`. This page-handler code cannot be unit-tested without `WebApplicationFactory` infrastructure.

**Redesigned approach:** Test `AssignShiftAsync` — the service-level method that swap approval delegates to after validation. This method handles override tokens for warnings, blocks on hard errors, and creates the actual assignment. It IS testable at the service level.

- [ ] **Step 1: Understand AssignShiftAsync contract**

`AssignShiftAsync` is at `Services/ShiftAssignmentService.cs` line 581:
```csharp
public async Task<ShiftAssignmentResult> AssignShiftAsync(
    int userId,
    int shiftInstanceId,
    int assignedByUserId,
    string? overrideToken = null,
    string? notes = null)
```

It calls `ValidateShiftAssignmentAsync(userId, shiftInstanceId)` at line 588. Then:
- If `validation.Errors.Count > 0` → returns `ShiftAssignmentResult(Success: false, ErrorKey: first error key)`
- If `validation.Warnings.Count > 0` and no valid override token → returns `ShiftAssignmentResult(Success: false, ErrorKey: "WARNINGS_REQUIRE_OVERRIDE")`
- If warnings exist but override token is valid → proceeds with assignment
- On success → creates `ShiftAssignment`, saves, returns `ShiftAssignmentResult(Success: true, AssignmentId: id)`

- [ ] **Step 2: Create test file**

Create `ShiftManager.Tests/UnitTests/Services/AssignShiftAsyncTests.cs` using the same setup pattern as `ShiftAssignmentServiceTests.cs` (InMemory DB, mocked `IHierarchySettingsService`, `IAppConfigCacheService`, etc.).

- [ ] **Step 3: Write tests**

Tests to cover:
1. `AssignShiftAsync` succeeds when validation returns no errors/warnings
2. `AssignShiftAsync` returns warnings and requires override token when warnings exist
3. `AssignShiftAsync` blocks when validation returns hard errors (even with override token)
4. `AssignShiftAsync` succeeds with valid override token when only warnings exist
5. `AssignShiftAsync` creates the ShiftAssignment record in the database

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "AssignShiftAsyncTests"
```
Expected: ALL PASS

- [ ] **Step 5: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/AssignShiftAsyncTests.cs
git commit -m "test: add AssignShiftAsync service tests for assignment creation and override token handling"
```

**Note:** Swap request page-handler orchestration (ownership check, self-swap prevention, status transitions) is tested by the Python E2E tests and browser testing. Extracting a `SwapRequestService` for unit-testable logic is a recommended follow-up.

---

## Task 11: Write VacationApprovalService Approval-Path Tests

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/VacationApprovalApproveTests.cs`
- Read: `Services/VacationApprovalService.cs:158-257` — `ApproveAsync` method
- Read: `ShiftManager.Tests/UnitTests/Services/VacationApprovalServiceTests.cs` — existing 4 tests for setup pattern

**⚠️ REVIEW REDESIGN:** Both reviewers found that time-off overlap detection does NOT happen at creation time. Users CAN create overlapping pending requests. Overlap is checked ONLY during approval in `VacationApprovalService.ApproveAsync` (lines 209-224), which queries for existing APPROVED requests with overlapping date ranges. The existing 4 tests (VA-01 through VA-04) only cover routing logic, not the approval execution path.

**Redesigned approach:** Test `ApproveAsync` — the service method that performs overlap detection, self-approval prevention, and side effects.

- [ ] **Step 1: Understand ApproveAsync contract**

`VacationApprovalService.ApproveAsync` at line 158:
```csharp
public async Task<(bool Success, string Message)> ApproveAsync(int requestId, int approverId, string? notes = null)
```

Key validation steps:
- Line 170: Status must be `Pending`
- Line 176: `request.UserId == approverId` → self-approval blocked
- Line 185: Grant check via `CanUserApproveAsync(approverId, requestId)`
- Lines 209-224: Overlap detection — queries DB for `Status == RequestStatus.Approved` with overlapping `StartDate`/`EndDate`
- On success: sets status to Approved, calls `ProcessApprovalSideEffectsAsync`
- Returns: `(true, "Approved")` or `(false, error message)`

- [ ] **Step 2: Read existing test setup pattern**

Read `ShiftManager.Tests/UnitTests/Services/VacationApprovalServiceTests.cs`. Copy the class setup pattern: InMemory DB, mocked `IGrantService`, `INotificationService`, `ITraineeService`, `ILogger`. Use the `CreateBaseEntitiesAsync` helper.

- [ ] **Step 3: Create test file with approval-path tests**

Create `ShiftManager.Tests/UnitTests/Services/VacationApprovalApproveTests.cs`. Tests:

1. **ApproveAsync rejects when overlapping APPROVED request exists**
   - Create user, create approved TimeOff for March 1-5
   - Create pending TimeOff for March 3-7
   - ApproveAsync(pendingId, managerId) → Success = false, message mentions overlap

2. **ApproveAsync allows when no overlapping approved requests**
   - Create user, create approved TimeOff for March 1-5
   - Create pending TimeOff for March 10-15
   - ApproveAsync(pendingId, managerId) → Success = true

3. **Canceled requests do NOT block approval**
   - Create user, create CANCELED TimeOff for March 1-5
   - Create pending TimeOff for March 3-7
   - ApproveAsync(pendingId, managerId) → Success = true

4. **Self-approval is blocked**
   - Create user (userId=1), create pending TimeOff
   - ApproveAsync(requestId, approverId=1) → Success = false

5. **Already-processed request cannot be approved**
   - Create user, create TimeOff with Status = Approved
   - ApproveAsync(requestId, managerId) → Success = false

6. **Pending requests do NOT block each other**
   - Create user, create pending TimeOff for March 1-5
   - Create pending TimeOff for March 3-7
   - ApproveAsync(secondId, managerId) → Success = true (only Approved status blocks)

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "VacationApprovalApproveTests"
```
Expected: ALL PASS

- [ ] **Step 5: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/VacationApprovalApproveTests.cs
git commit -m "test: add VacationApprovalService.ApproveAsync tests for overlap detection and access control"
```

---

## Task 12: Verify HomeTypes and Blueprints Pages Completeness

**Files:**
- Read: `Pages/Admin/HomeTypes/Index.cshtml.cs` (252 lines, 7 handlers)
- Read: `Pages/Admin/HomeTypes/Index.cshtml` (189 lines)
- Read: `Pages/Owner/Blueprints.cshtml.cs` (397 lines, 6 handlers)
- Read: `Pages/Owner/Blueprints.cshtml` (890 lines)

**Context:** Agent findings show both pages are MORE complete than initially reported:

**HomeTypes** (252 lines) has 7 handlers:
1. `OnGetAsync` (line 73) — Load molecules + home types
2. `OnPostCreateAsync` (line 93) — Create with pattern painter + time parsing
3. `OnPostEditAsync` (line 139) — Update name/pattern
4. `OnPostDeleteAsync` (line 165) — Delete home type
5. `OnPostToggleActiveAsync` (line 175) — Toggle active status
6. `OnPostAssignUsersAsync` (line 186) — Assign users to home type
7. `OnPostGenerateAsync` (line 203) — Generate home shifts (KeepManualChanges or OverwriteAll modes)

**Blueprints** (397 lines) has 6 handlers:
1. `OnGetAsync` (line 77) — Load shift types across scopes (Area > Molecule > Company)
2. `OnPostCreateShiftTypeAsync` (line 144) — Create with bilingual names + scope selection
3. `OnPostUpdateShiftNameAsync` (line 238) — Update names (English/Hebrew)
4. `OnPostUpdateShiftTimesAsync` (line 276) — Update start/end times
5. `OnGetCheckShiftTypeUsageAsync` (line 309) — Check program/instance counts (JSON API)
6. `OnPostDeleteShiftTypeAsync` (line 321) — Delete with usage checking + confirmation

Both are functionally complete CRUD pages. This task is now a **verification pass** to confirm:
- All labels are localized
- Empty states exist
- Grant checks are proper
- Dark mode works
- RTL works

- [ ] **Step 1: Browser-test HomeTypes page**

Navigate to `/Admin/HomeTypes`. Verify:
- Page loads
- Molecule selector works
- Create form renders
- Calendar painter for pattern works
- Labels are localized (switch to Hebrew)
- Dark mode works

- [ ] **Step 2: Check HomeTypes localization**

Search for hardcoded English strings in `Pages/Admin/HomeTypes/Index.cshtml`:
```bash
grep -n '"[A-Z][a-z]' Pages/Admin/HomeTypes/Index.cshtml | grep -v '@\|loc\|Localizer\|class\|id\|name\|type\|style\|value\|data-'
```

If any user-facing strings are not localized, add `<loc key="..." />` or `@Localizer["..."]` wrappers.

- [ ] **Step 3: Browser-test Blueprints page**

Navigate to `/Owner/Blueprints`. Verify:
- Page loads with shift type list
- Create form works
- Inline edit buttons work (name, time)
- Delete with usage check works
- Scope selector (Area/Molecule/Company) works
- Labels are localized

- [ ] **Step 4: Check Blueprints localization**

Same grep as Step 2 for `Pages/Owner/Blueprints.cshtml`.

- [ ] **Step 5: Fix any issues found, or confirm no issues**

If localization gaps found, add keys to both .resx files. If UI issues found, fix them.

- [ ] **Step 6: Commit (if changes made)**

```bash
git add Pages/Admin/HomeTypes/ Pages/Owner/Blueprints.cshtml Pages/Owner/Blueprints.cshtml.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix: verify and polish HomeTypes and Blueprints pages"
```

---

## Task 13: Browser-Test Game/Leaderboard and Capacity Mode

**Files:** None (browser testing only)

- [ ] **Step 1: Start the app**

```bash
dotnet run --urls "http://localhost:5000"
```

- [ ] **Step 2: Test Game/Leaderboard**

Navigate to `http://localhost:5000/Game/Leaderboard`. Verify:
- Page loads without errors
- Leaderboard displays (even if empty)
- Navigation works (back to main app)
- Localization works (switch to Hebrew)

- [ ] **Step 3: Test Capacity Mode**

Navigate to `http://localhost:5000/Calendar/Shifts?CapacityMode=True`. Verify:
- Capacity view renders
- Staffing numbers are visible
- No JS errors in console

- [ ] **Step 4: Test RTL at mobile viewport**

Use browser dev tools to set viewport to 375px width. Switch to Hebrew. Verify:
- Calendar is usable on small screen
- Sidebar hamburger works
- No layout breakage
- Text is readable

- [ ] **Step 5: Document findings**

Note any issues found. If issues are found, create follow-up fix tasks.

---

## Task 14: Integrate Python E2E Tests into CI

**Files:**
- Read: `test_quick_entry.py` — understand test structure
- Read: `test_text_entry.py` — understand test structure
- Create: CI configuration for Python E2E tests

**Context:** Python E2E tests exist but require manual server startup and use screenshot-based verification. They need to be integrated into CI.

Both Python test files use Playwright (`from playwright.sync_api import sync_playwright`). They are standalone scripts with custom `report()` functions — NOT pytest-based. Key issues:
- `test_quick_entry.py` uses `BASE_URL = "http://localhost:5999"` and credentials `admin@local` / `admin123`
- `test_text_entry.py` uses `BASE_URL = "http://localhost:5000"` and credentials `test.manager@shifty.test` / `TestManager123!`
- Both use hardcoded future dates (e.g., `"2026-03-29"`)
- No test isolation — tests modify shared state

**Chosen approach:** Option B — CI orchestration script that builds, starts, runs, collects.

- [ ] **Step 1: Standardize base URL via environment variable**

In both `test_quick_entry.py` and `test_text_entry.py`, replace the hardcoded `BASE_URL` with:
```python
import os
BASE_URL = os.environ.get("E2E_BASE_URL", "http://localhost:5000")
```

Also standardize credentials:
```python
E2E_EMAIL = os.environ.get("E2E_EMAIL", "test.manager@shifty.test")
E2E_PASSWORD = os.environ.get("E2E_PASSWORD", "TestManager123!")
```

Replace all hardcoded usages of email/password with these variables.

- [ ] **Step 2: Fix hardcoded dates to be relative**

Replace hardcoded date strings like `"2026-03-29"` with:
```python
from datetime import datetime, timedelta
FUTURE_DATE = (datetime.now() + timedelta(days=7)).strftime("%Y-%m-%d")
```

- [ ] **Step 3: Create CI orchestration script**

Create `scripts/run-e2e-tests.sh`:
```bash
#!/bin/bash
set -e

# Build
echo "Building ShiftManager..."
dotnet build --configuration Release

# Start app in background
echo "Starting app..."
dotnet run --configuration Release --urls "http://localhost:5000" &
APP_PID=$!

# Wait for health check
echo "Waiting for app to start..."
for i in $(seq 1 30); do
    if curl -sf http://localhost:5000/health > /dev/null 2>&1; then
        echo "App is ready!"
        break
    fi
    if [ $i -eq 30 ]; then
        echo "ERROR: App failed to start"
        kill $APP_PID 2>/dev/null
        exit 1
    fi
    sleep 2
done

# Install Playwright browsers
pip install playwright 2>/dev/null
playwright install chromium 2>/dev/null

# Run tests
export E2E_BASE_URL="http://localhost:5000"
export E2E_EMAIL="test.manager@shifty.test"
export E2E_PASSWORD="TestManager123!"

echo "Running test_text_entry.py..."
python test_text_entry.py
TEXT_RESULT=$?

echo "Running test_quick_entry.py..."
python test_quick_entry.py
QUICK_RESULT=$?

# Cleanup
echo "Stopping app..."
kill $APP_PID 2>/dev/null

# Report
if [ $TEXT_RESULT -ne 0 ] || [ $QUICK_RESULT -ne 0 ]; then
    echo "E2E TESTS FAILED"
    exit 1
fi
echo "ALL E2E TESTS PASSED"
```

Make executable: `chmod +x scripts/run-e2e-tests.sh`

- [ ] **Step 4: Create screenshots artifact directory**

```bash
mkdir -p test_screenshots
echo "test_screenshots/" >> .gitignore  # Don't commit screenshots
```

- [ ] **Step 5: Run the integrated tests locally**

```bash
./scripts/run-e2e-tests.sh
```
Expected: Both test scripts run and pass.

- [ ] **Step 6: Commit**

```bash
git add scripts/run-e2e-tests.sh test_quick_entry.py test_text_entry.py .gitignore
git commit -m "ci: integrate Python E2E tests with orchestration script and env-configurable URLs"
```

---

## Execution Order (Recommended)

**Phase 1: Quick Fixes (30 min)**
1. Task 1 — Fix test assertion (5 min)
2. Task 2 — Fix localization bug (15 min)
3. Task 3 — Fix orphan risk (10 min)

**Phase 2: UX Fixes (1 hour)**
4. Task 4 — Sidebar truncation (15 min)
5. Task 5 — MyTeam loading flash (20 min)
6. Task 7 — User deletion error (30 min)

**Phase 3: Data & Seeds (30 min)**
7. Task 6 — Delete test company (10 min)
8. Task 8 — Seed duty types (20 min)

**Phase 4: Browser Testing (15 min)**
9. Task 13 — Game, Capacity Mode, RTL mobile

**Phase 5: Feature Completeness (1-2 hours)**
10. Task 12 — HomeTypes & Blueprints

**Phase 6: Test Writing (4+ hours)**
11. Task 9 — Shift validation tests (2 hours)
12. Task 10 — Swap lifecycle tests (1 hour)
13. Task 11 — Time-off conflict tests (1 hour)

**Phase 7: CI Integration (5-8 hours — ⚠️ revised up from 2-4 per QA review)**
14. Task 14 — Python E2E CI integration (standardize ports, add startup script, Playwright CI setup, seed verification)
