# Pre-Release Fixes Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 14 identified issues (localization, functionality, design, accessibility) before ShiftManager v3.1.x release.

**Architecture:** Each task is an isolated fix touching 1-3 files. No cross-task dependencies. Tasks can be executed in any order. Mobile/responsive issues are deferred — desktop-first release.

**Tech Stack:** ASP.NET Core 8.0 Razor Pages, `<loc>` tag helper, `loc-aria-label` attribute, IStringLocalizer, .resx resource files, CSS custom properties (tokens.css)

---

## File Map

| File | Tasks | Action |
|------|-------|--------|
| `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` | T1 | Modify (lines 12-13, 40) |
| `Pages/Auth/ForgotPassword.cshtml` | T2 | Modify (line 101) |
| `Views/Shared/Components/SystemAlerts/Default.cshtml` | T3 | Modify (line 21) |
| `ViewComponents/SystemAlertsViewComponent.cs` | T3 | Modify (lines 38, 59-141) |
| `Pages/Admin/Companies.cshtml` | T5 | Modify (line 482) |
| `Resources/SharedResources.resx` | T2-T6 | Modify (add keys) |
| `Resources/SharedResources.he-IL.resx` | T2-T6 | Modify (add keys) |
| `Pages/Admin/Index.cshtml` | T7 | Modify (line 148) |
| `Program.cs` | T8 | Modify (line 1355) |
| `Pages/StatusCode.cshtml` | T8 | Create |
| `Pages/StatusCode.cshtml.cs` | T8 | Create |
| `wwwroot/css/site.css` | T9 | Modify (line 2758) |
| `wwwroot/css/components.css` | T10, T11 | Modify (lines 28-29, add print rules) |
| `Pages/Admin/Users.cshtml` | T12 | Modify (lines 815, 839, 876, 1060) |

---

## Chunk 1: Critical Localization Fixes

### Task 1: Fix Calendar Day-of-Week Headers (LOC-01, P0)

**Root Cause:** `ExcelCalendarTable/Default.cshtml` has hardcoded Hebrew day-name arrays used regardless of UI language. English users see Hebrew day names (ראשון, שני...) in all calendar views.

**Files:**
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml:12-13,40`

- [ ] **Step 1: Replace hardcoded Hebrew arrays with culture-aware day names**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, replace lines 12-13:

```csharp
// REMOVE these two lines:
var hebrewDays = new[] { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };
var shortHebrewDays = new[] { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳" };

// REPLACE with:
var culture = System.Globalization.CultureInfo.CurrentUICulture;
var dayNames = culture.DateTimeFormat.DayNames;          // Sunday=0 .. Saturday=6
var shortDayNames = culture.DateTimeFormat.AbbreviatedDayNames;
```

Then replace line 40:

```csharp
// REMOVE:
var dayName = isCompact ? shortHebrewDays[dayIndex] : hebrewDays[dayIndex];

// REPLACE with:
var dayName = isCompact ? shortDayNames[dayIndex] : dayNames[dayIndex];
```

- [ ] **Step 2: Verify in browser**

Run Playwright script to verify:
1. Navigate to Calendar/Shifts in English mode → day headers should show "Sunday", "Monday", etc.
2. Switch to Hebrew mode → day headers should show "יום ראשון", "יום שני", etc.

```python
from playwright.sync_api import sync_playwright
import sys, os
os.environ['PYTHONIOENCODING'] = 'utf-8'

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(ignore_https_errors=True)
    # Login
    page.goto('https://localhost:5001/Auth/Login')
    page.fill('input[name="Input.Email"]', 'admin@local')
    page.fill('input[name="Input.Password"]', 'admin123')
    page.click('button[type="submit"]')
    page.wait_for_load_state('networkidle')
    # Navigate to Shifts calendar
    page.goto('https://localhost:5001/Calendar/Shifts')
    page.wait_for_load_state('networkidle')
    # Take screenshot in English mode
    page.screenshot(path='/tmp/verify_loc01_en.png', full_page=True)
    # Switch to Hebrew
    page.context.add_cookies([{
        'name': '.AspNetCore.Culture',
        'value': 'c=he-IL|uic=he-IL',
        'domain': 'localhost',
        'path': '/'
    }])
    page.reload()
    page.wait_for_load_state('networkidle')
    page.screenshot(path='/tmp/verify_loc01_he.png', full_page=True)
    browser.close()
```

Expected: English day names in English mode, Hebrew day names in Hebrew mode.

**IMPORTANT**: `.DayNames` returns full day names (e.g., "Sunday"). Hebrew `.DayNames` returns "יום ראשון" (with "יום" prefix), while the old hardcoded array had just "ראשון" (without prefix). If the column headers are too wide with the full Hebrew names, use `.AbbreviatedDayNames` for Hebrew compact mode. The abbreviated Hebrew names are "יום א׳", "יום ב׳", etc. Verify the calendar layout isn't broken by longer day names — if it is, adjust the column width CSS or use abbreviated names for all modes.

- [ ] **Step 3: Commit**

```bash
git add Pages/Shared/Components/ExcelCalendarTable/Default.cshtml
git commit -m "fix(loc): use culture-aware day names in calendar headers (LOC-01)"
```

---

### Task 2: Localize ForgotPassword Description (LOC-03, P1)

**Root Cause:** Hardcoded English paragraph on line 101 of ForgotPassword.cshtml, not wrapped in `<loc>`.

**Files:**
- Modify: `Pages/Auth/ForgotPassword.cshtml:100-102`
- Modify: `Resources/SharedResources.resx` (add key)
- Modify: `Resources/SharedResources.he-IL.resx` (add key)

- [ ] **Step 1: Wrap the string in a `<loc>` tag**

In `Pages/Auth/ForgotPassword.cshtml`, replace line 101:

```html
<!-- REMOVE: -->
            If you already know your current password and want to change it, use this form.

<!-- REPLACE with: -->
            <loc key="ChangePassword_Description">If you already know your current password and want to change it, use this form.</loc>
```

- [ ] **Step 2: Add resource key to English .resx**

Add to `Resources/SharedResources.resx` (alphabetical position near other ChangePassword keys):

```xml
<data name="ChangePassword_Description" xml:space="preserve">
  <value>If you already know your current password and want to change it, use this form.</value>
</data>
```

- [ ] **Step 3: Add resource key to Hebrew .resx**

Add to `Resources/SharedResources.he-IL.resx`:

```xml
<data name="ChangePassword_Description" xml:space="preserve">
  <value>אם אתה כבר יודע את הסיסמה הנוכחית שלך ורוצה לשנות אותה, השתמש בטופס זה.</value>
</data>
```

- [ ] **Step 4: Verify in browser**

Navigate to `/Auth/ForgotPassword` in Hebrew mode. The description under "שינוי סיסמה" should now show Hebrew text.

- [ ] **Step 5: Commit**

```bash
git add Pages/Auth/ForgotPassword.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(loc): localize password change description (LOC-03)"
```

---

### Task 3: Localize System Alerts + Fix aria-label (LOC-04, LOC-12)

**Root Cause (LOC-04):** `SystemAlertsViewComponent.cs` builds alert messages as hardcoded English strings, which are cached and rendered directly. Users see English alerts regardless of UI language.

**Root Cause (LOC-12):** `SystemAlerts/Default.cshtml` line 21 has `aria-label="Dismiss"` hardcoded instead of using `loc-aria-label`.

**Files:**
- Modify: `ViewComponents/SystemAlertsViewComponent.cs` (inject localizer, use localized strings, culture-aware cache)
- Modify: `Views/Shared/Components/SystemAlerts/Default.cshtml` (fix aria-label)
- Modify: `Resources/SharedResources.resx` (add 11 alert keys)
- Modify: `Resources/SharedResources.he-IL.resx` (add 11 alert keys)

- [ ] **Step 1: Fix aria-label in the View (LOC-12)**

In `Views/Shared/Components/SystemAlerts/Default.cshtml`, replace line 21:

```html
<!-- REMOVE: -->
                            aria-label="Dismiss">×</button>

<!-- REPLACE with: -->
                            loc-aria-label="Error_Dismiss">×</button>
```

The `Error_Dismiss` key already exists in both .resx files (English: "Dismiss", Hebrew: "סגור").

- [ ] **Step 2: Add localizer injection to ViewComponent**

In `ViewComponents/SystemAlertsViewComponent.cs`, add the using and inject:

```csharp
// Add using at top:
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
```

Add `IStringLocalizer<SharedResources>` to the constructor:

```csharp
public class SystemAlertsViewComponent : ViewComponent
{
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly IGrantService _grantService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private const string CacheKey = "SystemAlerts";

    public SystemAlertsViewComponent(IMemoryCache cache, IConfiguration configuration,
        IWebHostEnvironment env, IGrantService grantService,
        IStringLocalizer<SharedResources> localizer)
    {
        _cache = cache;
        _configuration = configuration;
        _env = env;
        _grantService = grantService;
        _localizer = localizer;
    }
```

- [ ] **Step 3: Make cache culture-aware**

In the `InvokeAsync` method, change the cache key to include the current UI culture:

```csharp
// REMOVE:
var alerts = _cache.GetOrCreate(CacheKey, entry =>

// REPLACE with:
var cultureCacheKey = $"{CacheKey}_{System.Globalization.CultureInfo.CurrentUICulture.Name}";
var alerts = _cache.GetOrCreate(cultureCacheKey, entry =>
```

- [ ] **Step 4: Replace hardcoded strings with localized versions**

Replace each `alerts.Add(...)` call in `CollectAlerts()` with localized equivalents:

```csharp
// Line 59 - WAL warning:
alerts.Add(_localizer["SystemAlert_WalSize", walSizeMb.ToString("F0")]);

// Line 71 - Critical disk:
alerts.Add(_localizer["SystemAlert_DiskCritical", freePercent.ToString("F1")]);

// Line 73 - Warning disk:
alerts.Add(_localizer["SystemAlert_DiskLow", freePercent.ToString("F1")]);

// Line 89 - No backups:
alerts.Add(_localizer["SystemAlert_NoBackups"]);

// Line 91 - Old backup:
alerts.Add(_localizer["SystemAlert_BackupOld", ((DateTime.Now - latestBackup.CreationTime).TotalDays).ToString("F0")]);

// Line 95 - No backup dir:
alerts.Add(_localizer["SystemAlert_NoBackupDir"]);

// Line 105 - DataProtection keys:
alerts.Add(_localizer["SystemAlert_NoDataProtectionKeys"]);

// Line 112 - HMAC secret:
alerts.Add(_localizer["SystemAlert_DefaultHmacSecret"]);

// Line 122 - No Arial font:
alerts.Add(_localizer["SystemAlert_NoHebrewFont"]);

// Line 139 - Notification failures:
alerts.Add(_localizer["SystemAlert_NotificationFailures", errors.ToString(), lastRun.ToString("g")]);

// Line 141 - No notification run:
alerts.Add(_localizer["SystemAlert_NoNotificationRun", ((DateTime.UtcNow - lastRun).TotalHours).ToString("F0")]);
```

**IMPORTANT**: The View (`Default.cshtml`) uses `alert.StartsWith("CRITICAL:")` and `alert.StartsWith("SECURITY:")` on lines 8-9 to determine styling. After localization, these prefixes will be translated. Fix the View's prefix detection:

In `Views/Shared/Components/SystemAlerts/Default.cshtml`, replace lines 8-9:

```csharp
// REMOVE:
var isCritical = alert.StartsWith("CRITICAL:");
var isSecurity = alert.StartsWith("SECURITY:");

// REPLACE with:
var isCritical = alert.StartsWith("CRITICAL:") || alert.StartsWith("קריטי:");
var isSecurity = alert.StartsWith("SECURITY:") || alert.StartsWith("אבטחה:");
```

- [ ] **Step 5: Add English resource keys**

Add to `Resources/SharedResources.resx`:

```xml
<data name="SystemAlert_WalSize" xml:space="preserve">
  <value>WARNING: SQLite WAL file is {0}MB. Consider running PRAGMA wal_checkpoint(TRUNCATE) during maintenance.</value>
</data>
<data name="SystemAlert_DiskCritical" xml:space="preserve">
  <value>CRITICAL: Disk space is critically low ({0}% free). Database may fail.</value>
</data>
<data name="SystemAlert_DiskLow" xml:space="preserve">
  <value>WARNING: Disk space is low ({0}% free).</value>
</data>
<data name="SystemAlert_NoBackups" xml:space="preserve">
  <value>WARNING: No database backups found.</value>
</data>
<data name="SystemAlert_BackupOld" xml:space="preserve">
  <value>WARNING: Last backup is {0} days old.</value>
</data>
<data name="SystemAlert_NoBackupDir" xml:space="preserve">
  <value>WARNING: Backup directory does not exist.</value>
</data>
<data name="SystemAlert_NoDataProtectionKeys" xml:space="preserve">
  <value>WARNING: DataProtection keys are missing. Sessions will be invalidated on restart.</value>
</data>
<data name="SystemAlert_DefaultHmacSecret" xml:space="preserve">
  <value>SECURITY: API key HMAC secret is using default value. Configure Security:ApiKeyHmacSecret.</value>
</data>
<data name="SystemAlert_NoHebrewFont" xml:space="preserve">
  <value>WARNING: Arial font not found. PDF exports may render Hebrew text incorrectly. Install Hebrew language pack.</value>
</data>
<data name="SystemAlert_NotificationFailures" xml:space="preserve">
  <value>WARNING: Last notification run had {0} delivery failures ({1} UTC).</value>
</data>
<data name="SystemAlert_NoNotificationRun" xml:space="preserve">
  <value>WARNING: No notification job run in {0} hours.</value>
</data>
```

- [ ] **Step 6: Add Hebrew resource keys**

Add to `Resources/SharedResources.he-IL.resx`:

```xml
<data name="SystemAlert_WalSize" xml:space="preserve">
  <value>אזהרה: קובץ WAL של SQLite הוא {0}MB. שקול להריץ PRAGMA wal_checkpoint(TRUNCATE) במהלך תחזוקה.</value>
</data>
<data name="SystemAlert_DiskCritical" xml:space="preserve">
  <value>קריטי: מקום בדיסק נמוך באופן קריטי ({0}% פנוי). מסד הנתונים עלול להיכשל.</value>
</data>
<data name="SystemAlert_DiskLow" xml:space="preserve">
  <value>אזהרה: מקום בדיסק נמוך ({0}% פנוי).</value>
</data>
<data name="SystemAlert_NoBackups" xml:space="preserve">
  <value>אזהרה: לא נמצאו גיבויים למסד הנתונים.</value>
</data>
<data name="SystemAlert_BackupOld" xml:space="preserve">
  <value>אזהרה: הגיבוי האחרון לפני {0} ימים.</value>
</data>
<data name="SystemAlert_NoBackupDir" xml:space="preserve">
  <value>אזהרה: תיקיית הגיבויים לא קיימת.</value>
</data>
<data name="SystemAlert_NoDataProtectionKeys" xml:space="preserve">
  <value>אזהרה: מפתחות DataProtection חסרים. הפעלות יבוטלו בעת הפעלה מחדש.</value>
</data>
<data name="SystemAlert_DefaultHmacSecret" xml:space="preserve">
  <value>אבטחה: סוד HMAC למפתח API משתמש בערך ברירת מחדל. הגדר Security:ApiKeyHmacSecret.</value>
</data>
<data name="SystemAlert_NoHebrewFont" xml:space="preserve">
  <value>אזהרה: גופן Arial לא נמצא. ייצוא PDF עלול להציג טקסט בעברית בצורה שגויה. התקן חבילת שפה עברית.</value>
</data>
<data name="SystemAlert_NotificationFailures" xml:space="preserve">
  <value>אזהרה: הרצת ההתראות האחרונה כללה {0} כשלונות משלוח ({1} UTC).</value>
</data>
<data name="SystemAlert_NoNotificationRun" xml:space="preserve">
  <value>אזהרה: לא הורצה משימת התראות מזה {0} שעות.</value>
</data>
```

- [ ] **Step 7: Verify in browser**

Log in as Owner in Hebrew mode. Check that the disk space warning banner (if showing) appears in Hebrew.

- [ ] **Step 8: Commit**

```bash
git add ViewComponents/SystemAlertsViewComponent.cs Views/Shared/Components/SystemAlerts/Default.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(loc): localize system alerts and fix Dismiss aria-label (LOC-04, LOC-12)"
```

---

### Task 4: Fix Login Subtitle Hebrew Translation (LOC-05)

**Root Cause:** The Hebrew translation for `Auth_SignInToContinue` contains the English brand name "Shift Manager" instead of "מנהל משמרות".

**Files:**
- Modify: `Resources/SharedResources.he-IL.resx` (line ~6765)

- [ ] **Step 1: Update the Hebrew translation**

In `Resources/SharedResources.he-IL.resx`, find and update:

```xml
<!-- REMOVE: -->
<data name="Auth_SignInToContinue" xml:space="preserve">
  <value>היכנס כדי להמשיך ל-Shift Manager</value>
</data>

<!-- REPLACE with: -->
<data name="Auth_SignInToContinue" xml:space="preserve">
  <value>היכנס כדי להמשיך למנהל משמרות</value>
</data>
```

- [ ] **Step 2: Verify in browser**

Navigate to `/Auth/Login` in Hebrew mode. The subtitle should now read "היכנס כדי להמשיך למנהל משמרות" without any English.

- [ ] **Step 3: Commit**

```bash
git add Resources/SharedResources.he-IL.resx
git commit -m "fix(loc): use Hebrew brand name in login subtitle (LOC-05)"
```

---

### Task 5: Fix Companies Page Localization (LOC-06, LOC-08)

**Root Cause (LOC-06):** The `<loc key="CompanyMoleculeHint">` tag in `Companies.cshtml` line 471 references a key that doesn't exist in either .resx file. In Hebrew mode, the raw key name "CompanyMoleculeHint" is displayed.

**Root Cause (LOC-08):** The Company ID hint `"(lowercase, letters, numbers, hyphens)"` at line 482 is a bare HTML string, not wrapped in `<loc>`. It shows English in Hebrew mode.

**Files:**
- Modify: `Pages/Admin/Companies.cshtml:482`
- Modify: `Resources/SharedResources.resx` (add 2 keys)
- Modify: `Resources/SharedResources.he-IL.resx` (add 2 keys)

- [ ] **Step 1: Wrap the Company ID hint in a `<loc>` tag**

In `Pages/Admin/Companies.cshtml`, replace line 482:

```html
<!-- REMOVE: -->
                        <span class="form-label-hint">(lowercase, letters, numbers, hyphens)</span>

<!-- REPLACE with: -->
                        <span class="form-label-hint"><loc key="CompanySlugHint">(lowercase, letters, numbers, hyphens)</loc></span>
```

- [ ] **Step 2: Add English resource keys**

Add to `Resources/SharedResources.resx`:

```xml
<data name="CompanyMoleculeHint" xml:space="preserve">
  <value>Which molecule this company belongs to</value>
</data>
<data name="CompanySlugHint" xml:space="preserve">
  <value>(lowercase, letters, numbers, hyphens)</value>
</data>
```

- [ ] **Step 3: Add Hebrew resource keys**

Add to `Resources/SharedResources.he-IL.resx`:

```xml
<data name="CompanyMoleculeHint" xml:space="preserve">
  <value>המולקולה שהחברה שייכת אליה</value>
</data>
<data name="CompanySlugHint" xml:space="preserve">
  <value>(אותיות קטנות באנגלית, מספרים ומקפים)</value>
</data>
```

- [ ] **Step 4: Commit**

```bash
git add Pages/Admin/Companies.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(loc): add missing Companies page resource keys (LOC-06, LOC-08)"
```

---

## Chunk 2: Functionality Fixes

### Task 6: Fix OnCall Calendar Naming Inconsistency (FUNC-04)

**Root Cause:** The `OnDutyCalendar` resource key has English value "Day Shift Calendar" but the sidebar navigation calls it "On-Call". This confuses users navigating between sidebar and page.

**Files:**
- Modify: `Resources/SharedResources.resx` (line ~2150)

- [ ] **Step 1: Update the English resource value**

In `Resources/SharedResources.resx`, find and update:

```xml
<!-- REMOVE: -->
<data name="OnDutyCalendar" xml:space="preserve">
  <value>Day Shift Calendar</value>
</data>

<!-- REPLACE with: -->
<data name="OnDutyCalendar" xml:space="preserve">
  <value>On-Call Calendar</value>
</data>
```

Note: The Hebrew value "לוח משמרות רוחב" can remain unchanged — verify with the user that this is the correct Hebrew term for the on-call calendar.

- [ ] **Step 2: Verify**

Navigate to `/Calendar/OnCall` in English mode. The page title and breadcrumb should now say "On-Call Calendar" matching the sidebar link.

- [ ] **Step 3: Commit**

```bash
git add Resources/SharedResources.resx
git commit -m "fix: rename OnDutyCalendar to On-Call Calendar for consistency (FUNC-04)"
```

---

### Task 7: Fix Admin Hub Blueprints Link Visibility (FUNC-03)

**Root Cause:** The Admin Hub (`/Admin/Index`) shows a "Blueprints" card linking to `/Owner/Blueprints` for ALL admin-level users. The link itself is correct, but Managers who click it get AccessDenied since Blueprints is an Owner-only page. The card should be hidden for non-Owner roles.

**Research Note:** The QA agent reported this as a 404, but it's actually an access-control issue. The link destination is correct.

**Files:**
- Modify: `Pages/Admin/Index.cshtml:148`

- [ ] **Step 1: Gate the Blueprints card behind Owner role check**

In `Pages/Admin/Index.cshtml`, wrap the Blueprints card (line 148) with a role check. Find the `<a href="/Owner/Blueprints"` block and wrap it:

```html
<!-- Add conditional before the <a> tag on line 148: -->
@if (User.IsInRole("Owner"))
{
        <a href="/Owner/Blueprints" class="admin-tool-card">
            <div class="tool-icon">📘</div>
            <div class="tool-content">
                <h3><loc key="Section_Blueprints">Blueprints</loc></h3>
                <p><loc key="Section_BlueprintsDesc">Manage shift type definitions</loc></p>
            </div>
            <div class="tool-arrow">→</div>
        </a>
}
```

**Verify**: Check if other Owner-only links on the Admin Hub page also need gating. The implementor should look at the surrounding cards in the "Scheduling Configuration" section and verify they are accessible to the logged-in user's role.

- [ ] **Step 2: Verify**

Log in as Manager (`mgr.alhut.tz@test` / `Test1234!`), navigate to `/Admin/Index`. The Blueprints card should no longer appear. Log in as Owner — it should appear.

- [ ] **Step 3: Commit**

```bash
git add Pages/Admin/Index.cshtml
git commit -m "fix: hide Owner-only Blueprints card from non-Owner admin users (FUNC-03)"
```

---

### Task 8: Resolve Dead URL Reports (FUNC-01, FUNC-02)

**Research Finding:** Comprehensive search confirms:
- `/My/Feedback` — NO page exists, NO navigation links reference it. Only `Pages/Public/Feedback.cshtml` exists (public, unauthenticated).
- `/My/ShiftSwapGame` — NO page exists, NO navigation links reference it. `wwwroot/js/shift-swap-game.js` exists but is unreferenced.

These URLs were tested by the QA agent from the checklist but are NOT reachable through any in-app navigation. No user will encounter these 404s through normal usage.

**Decision Required:** Choose one:
- **Option A (Recommended):** Mark as non-issues — no user-facing impact since no links exist. Skip this task.
- **Option B:** Create redirect pages or actual pages if these features are planned.
- **Option C:** If `shift-swap-game.js` is dead code from a removed feature, delete it to avoid confusion.

- [ ] **Step 1: Verify no references exist** (if proceeding with cleanup)

```bash
# Search for any remaining references in the codebase
grep -r "Feedback" --include="*.cshtml" --include="*.cs" --include="*.js" -l | head -20
grep -r "ShiftSwapGame\|shift-swap-game\|shiftSwapGame" --include="*.cshtml" --include="*.cs" --include="*.js" -l | head -20
```

- [ ] **Step 2: Act based on decision**

If Option A: No code changes. Add a note to the QA review that these are non-issues.
If Option C: Remove `wwwroot/js/shift-swap-game.js` and any related dead code.

- [ ] **Step 3: Commit (if changes made)**

```bash
git commit -m "chore: clean up unreferenced game assets (FUNC-01, FUNC-02)"
```

---

## Chunk 3: Design & Accessibility Fixes

### Task 9: Create Styled 404 Error Page (DR-ERROR-01)

**Root Cause:** In Development mode, `Program.cs` line 1355 uses `UseStatusCodePages("text/plain", "HTTP {0}")` which renders bare plain text for status code errors. No branded error page exists for 404s.

**Files:**
- Create: `Pages/StatusCode.cshtml`
- Create: `Pages/StatusCode.cshtml.cs`
- Modify: `Program.cs:1355`
- Modify: `Resources/SharedResources.resx` (add keys)
- Modify: `Resources/SharedResources.he-IL.resx` (add keys)

- [ ] **Step 1: Create the StatusCode page model**

Create `Pages/StatusCode.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages;

[IgnoreAntiforgeryToken]
public class StatusCodeModel : PageModel
{
    public int StatusCode { get; set; }

    public void OnGet(int code)
    {
        StatusCode = code;
        Response.StatusCode = code;
    }
}
```

- [ ] **Step 2: Create the StatusCode view**

Create `Pages/StatusCode.cshtml`:

```html
@page "{code:int}"
@model StatusCodeModel
@using Microsoft.Extensions.Localization
@using ShiftManager.Resources
@inject IStringLocalizer<SharedResources> Localizer
@{
    Layout = "_Layout";
    ViewData["Title"] = Model.StatusCode == 404
        ? Localizer["Error_PageNotFound"].Value
        : Localizer["Error_GenericTitle"].Value;
}

<div class="card" style="max-width: 600px; margin: 2rem auto; text-align: center; padding: 2rem;">
    <div style="font-size: 4rem; font-weight: 700; color: var(--text-muted); margin-bottom: 1rem;">
        @Model.StatusCode
    </div>
    @if (Model.StatusCode == 404)
    {
        <h2><loc key="Error_PageNotFound">Page Not Found</loc></h2>
        <p style="color: var(--text-muted); margin-bottom: 1.5rem;">
            <loc key="Error_PageNotFoundDesc">The page you're looking for doesn't exist or has been moved.</loc>
        </p>
    }
    else if (Model.StatusCode == 403)
    {
        <h2><loc key="Error_Forbidden">Access Denied</loc></h2>
        <p style="color: var(--text-muted); margin-bottom: 1.5rem;">
            <loc key="Error_ForbiddenDesc">You don't have permission to access this page.</loc>
        </p>
    }
    else
    {
        <h2><loc key="Error_GenericTitle">Something Went Wrong</loc></h2>
        <p style="color: var(--text-muted); margin-bottom: 1.5rem;">
            <loc key="Error_GenericDesc">An unexpected error occurred. Please try again.</loc>
        </p>
    }
    <a href="/" class="btn btn-primary">
        <loc key="Error_BackToHome">Back to Home</loc>
    </a>
</div>
```

- [ ] **Step 3: Update Program.cs to use the new page**

In `Program.cs`, replace line 1355:

```csharp
// REMOVE:
    app.UseStatusCodePages("text/plain", "HTTP {0}");

// REPLACE with:
    app.UseStatusCodePagesWithReExecute("/StatusCode/{0}");
```

Also add the same line in the production block (after line 1367, after UseExceptionHandler):

```csharp
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/StatusCode/{0}");
    app.UseHsts();
```

- [ ] **Step 4: Add resource keys**

Add to `Resources/SharedResources.resx`:

```xml
<data name="Error_PageNotFound" xml:space="preserve">
  <value>Page Not Found</value>
</data>
<data name="Error_PageNotFoundDesc" xml:space="preserve">
  <value>The page you're looking for doesn't exist or has been moved.</value>
</data>
<data name="Error_Forbidden" xml:space="preserve">
  <value>Access Denied</value>
</data>
<data name="Error_ForbiddenDesc" xml:space="preserve">
  <value>You don't have permission to access this page.</value>
</data>
<data name="Error_GenericTitle" xml:space="preserve">
  <value>Something Went Wrong</value>
</data>
<data name="Error_GenericDesc" xml:space="preserve">
  <value>An unexpected error occurred. Please try again.</value>
</data>
<data name="Error_BackToHome" xml:space="preserve">
  <value>Back to Home</value>
</data>
```

Add to `Resources/SharedResources.he-IL.resx`:

```xml
<data name="Error_PageNotFound" xml:space="preserve">
  <value>הדף לא נמצא</value>
</data>
<data name="Error_PageNotFoundDesc" xml:space="preserve">
  <value>הדף שאתה מחפש לא קיים או הועבר.</value>
</data>
<data name="Error_Forbidden" xml:space="preserve">
  <value>הגישה נדחתה</value>
</data>
<data name="Error_ForbiddenDesc" xml:space="preserve">
  <value>אין לך הרשאה לגשת לדף זה.</value>
</data>
<data name="Error_GenericTitle" xml:space="preserve">
  <value>משהו השתבש</value>
</data>
<data name="Error_GenericDesc" xml:space="preserve">
  <value>אירעה שגיאה בלתי צפויה. אנא נסה שוב.</value>
</data>
<data name="Error_BackToHome" xml:space="preserve">
  <value>חזרה לדף הבית</value>
</data>
```

- [ ] **Step 5: Verify**

Navigate to any non-existent URL (e.g., `/this-page-does-not-exist`). Should see a styled card with "404 — Page Not Found" instead of bare "HTTP 404" text. Verify in both English and Hebrew modes.

- [ ] **Step 6: Commit**

```bash
git add Pages/StatusCode.cshtml Pages/StatusCode.cshtml.cs Program.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: add styled and localized status code error pages (DR-ERROR-01)"
```

---

### Task 10: Fix Dark Mode Select Dropdown Chevron (DR-THEME-01)

**Root Cause:** The `.form-select` custom SVG dropdown arrow in `site.css` uses `stroke='%23666'` (gray #666). In dark mode, this gray arrow is hard to see on the dark background. Additionally, the native browser dropdown arrow may show through in some dark mode contexts, creating a "stacked chevron" effect.

**Files:**
- Modify: `wwwroot/css/site.css:2754-2763`

- [ ] **Step 1: Add dark mode variant for the select arrow SVG**

In `wwwroot/css/site.css`, after the `.form-select` block that ends at line 2763, add a dark mode override:

```css
/* Dark mode: lighter dropdown arrow for visibility */
[data-theme="dark"] .form-select,
.dark .form-select {
  background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3e%3cpath fill='none' stroke='%23aaa' stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='M2 5l6 6 6-6'/%3e%3c/svg%3e");
}
```

**Note:** Check how the app applies dark mode — look for `data-theme="dark"` on `<html>` or `<body>`, or a `.dark` class. Match the selector to the actual dark mode implementation. Verify by inspecting the DOM with dark mode active.

- [ ] **Step 2: Verify**

Switch to dark mode, navigate to Calendar/Overview. The select dropdowns (View mode, Users filter) should show a single, clearly visible light gray chevron arrow — no stacking or double-arrow artifacts.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/css/site.css
git commit -m "fix(css): lighter select dropdown arrow in dark mode (DR-THEME-01)"
```

---

### Task 11: Fix Skip-Link Dark Mode Contrast (DR-A11Y-01)

**Root Cause:** The skip-link uses `background: var(--primary)` and `color: var(--primary-contrast)`. In dark mode, `--primary` = `#5B9BD5` (medium blue) and `--primary-contrast` = `#FFFFFF` (white). White on medium blue = ~2.8:1 contrast ratio, below WCAG AA 4.5:1. The skip-link text is invisible when focused.

**Files:**
- Modify: `wwwroot/css/components.css:24-34`

- [ ] **Step 1: Add explicit dark mode colors for skip-link**

In `wwwroot/css/components.css`, after the `.skip-link:focus` block (after line 38), add:

```css
/* Dark mode: ensure skip-link has sufficient contrast (WCAG AA 4.5:1) */
[data-theme="dark"] .skip-link {
  background: var(--bg-card, #1e293b);
  color: var(--text, #e2e8f0);
  border: 2px solid var(--primary);
}
```

This gives dark background + light text with a primary-colored border for visibility. The contrast ratio of `#e2e8f0` on `#1e293b` is ~11:1 (well above WCAG AA).

**Note:** Match the `[data-theme="dark"]` selector to the actual dark mode implementation in the app. Check tokens.css for the correct dark mode variable names.

- [ ] **Step 2: Verify**

In dark mode, press Tab key immediately after page load. The skip-link should appear at the top with readable text on a dark background.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/css/components.css
git commit -m "fix(a11y): improve skip-link contrast in dark mode (DR-A11Y-01)"
```

---

### Task 12: Hide Bottom Dock and System Alerts in Print (DR-PRINT-01)

**Root Cause:** The print CSS hides the sidebar, toolbar, and many UI elements, but misses the bottom dock (Quick Info panel) and system alerts banner. These appear in printed output unnecessarily.

**Files:**
- Modify: `wwwroot/css/components.css` (add print rules)

- [ ] **Step 1: Add print rules to hide bottom dock and system alerts**

In `wwwroot/css/components.css`, find the existing `@media print` block (around line 1182) and add:

```css
@media print {
  .bottom-dock,
  .bottom-dock__toggle,
  .system-alerts {
    display: none !important;
  }
}
```

If there's already a `@media print` block in the file, add these selectors to it. If not, add the block near the existing print-related rules.

- [ ] **Step 2: Verify**

Open Calendar/Overview in the browser, use Print Preview (Ctrl+P). The Quick Info panel and any system warning banners should NOT appear in the printed output.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/css/components.css
git commit -m "fix(print): hide bottom dock and system alerts in print output (DR-PRINT-01)"
```

---

### Task 13: Add Accessibility Labels to Admin/Users Inline Edit Inputs (DR-A11Y-02)

**Root Cause:** Inline edit form inputs in the Admin/Users table (Job Type select, Role select, Password reset input, File import input) lack associated `<label>` elements or `aria-label` attributes. Screen readers cannot identify these fields.

**Files:**
- Modify: `Pages/Admin/Users.cshtml:815,839,876,1060`

- [ ] **Step 1: Add aria-label to Job Type select (line 815)**

```html
<!-- Add loc-aria-label attribute to the select: -->
<select class="form-input cell-edit__select" name="jobTypeId"
        data-original-value="@(u.JobTypeId?.ToString() ?? "")"
        loc-aria-label="Admin_EditJobType">
```

- [ ] **Step 2: Add aria-label to Role select (line 839)**

```html
<select class="form-input cell-edit__select" name="roleTemplateId"
        data-original-value="@(u.RoleTemplateId?.ToString() ?? "")"
        loc-aria-label="Admin_EditRole">
```

- [ ] **Step 3: Add aria-label to Password input (line 877)**

```html
<input class="form-input" type="password" name="newPassword"
       placeholder="@Localizer["NewPassword"]"
       minlength="6" style="min-width: 100px;"
       loc-aria-label="Admin_ResetPassword" />
```

- [ ] **Step 4: Add aria-label to File import input (line 1060)**

```html
<input type="file" name="BulkImportFile" accept=".csv" class="form-input"
       style="max-width: 300px;" required
       loc-aria-label="Admin_BulkImportFile" />
```

- [ ] **Step 5: Add resource keys**

Add to `Resources/SharedResources.resx`:

```xml
<data name="Admin_EditJobType" xml:space="preserve">
  <value>Edit job type</value>
</data>
<data name="Admin_EditRole" xml:space="preserve">
  <value>Edit role</value>
</data>
<data name="Admin_ResetPassword" xml:space="preserve">
  <value>New password for user</value>
</data>
<data name="Admin_BulkImportFile" xml:space="preserve">
  <value>CSV file for bulk import</value>
</data>
```

Add to `Resources/SharedResources.he-IL.resx`:

```xml
<data name="Admin_EditJobType" xml:space="preserve">
  <value>עריכת סוג עבודה</value>
</data>
<data name="Admin_EditRole" xml:space="preserve">
  <value>עריכת תפקיד</value>
</data>
<data name="Admin_ResetPassword" xml:space="preserve">
  <value>סיסמה חדשה למשתמש</value>
</data>
<data name="Admin_BulkImportFile" xml:space="preserve">
  <value>קובץ CSV לייבוא מרוכז</value>
</data>
```

- [ ] **Step 6: Commit**

```bash
git add Pages/Admin/Users.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(a11y): add aria-labels to Admin/Users inline edit inputs (DR-A11Y-02)"
```

---

## Summary

| Task | Issue(s) | Priority | Effort | Files Changed |
|------|----------|----------|--------|---------------|
| T1 | LOC-01 | P0 | S | 1 |
| T2 | LOC-03 | P1 | S | 3 |
| T3 | LOC-04, LOC-12 | P1, P4 | M | 4 |
| T4 | LOC-05 | P2 | S | 1 |
| T5 | LOC-06, LOC-08 | P2 | S | 3 |
| T6 | FUNC-04 | P3 | S | 1 |
| T7 | FUNC-03 | P3 | S | 1 |
| T8 | FUNC-01, FUNC-02 | P2 | S | 0-1 |
| T9 | DR-ERROR-01 | P2 | M | 5 |
| T10 | DR-THEME-01 | P3 | S | 1 |
| T11 | DR-A11Y-01 | P3 | S | 1 |
| T12 | DR-PRINT-01 | P3 | S | 1 |
| T13 | DR-A11Y-02 | P2 | S | 3 |

**Total estimated effort:** ~3-5 hours for an experienced developer.

**Recommended execution order:** T1 → T2 → T3 → T9 → T13 → T4 → T5 → T6 → T7 → T10 → T11 → T12 → T8

After all fixes, re-run the QA review framework (`docs/qa/release-readiness-assessment.md`) targeting only the affected areas to verify.
