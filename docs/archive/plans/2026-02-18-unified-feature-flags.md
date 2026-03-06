# Unified Feature Flags Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Unify the two disconnected feature flag systems (DB-backed `IFeatureFlagService` and appsettings `Features:*`) into a single DB-backed system with a working Owner UI, and harden appsettings for air-gapped IIS deployment.

**Architecture:** All runtime feature flag checks will read from the DB via `IFeatureFlagService.IsEnabledAsync()`. The existing `FeatureFlag` model, `FeatureFlagService`, and DB table are fully implemented and ready — we just need to: (1) seed the missing flags, (2) wire consumers to use the service instead of `IConfiguration`, (3) make the Owner UI read/write to DB, and (4) harden appsettings for clean deployment. `appsettings.json` retains infrastructure config (connection strings, logging, email, Griffin) but feature flags move entirely to DB.

**Tech Stack:** ASP.NET Core 8.0, EF Core + SQLite, Razor Pages, `IMemoryCache` (1-min TTL already configured in `FeatureFlagService`)

---

## Pre-Implementation: The Two Systems Today

### System 1 — DB-backed (implemented, never called)
- Model: `Models/FeatureFlag.cs` — `(Name, IsEnabled, CompanyId?, UserId?)`
- Service: `Services/FeatureFlagService.cs` — full CRUD + 1-min cache
- Seed: 9 `FF_*` flags in `FeatureFlagSeed.cs` (all disabled)
- **Zero runtime callers** — `IsEnabledAsync()` is never invoked

### System 2 — appsettings (active, no persistence UI)
- 12+ `Features:*` keys in `appsettings.json`
- Read at runtime by 9 API controllers, `_Layout.cshtml`, `CompanyIdInterceptor`, `DailyNotificationJob`, `DutyRotationService`, `OnDutyService`, `Signup`, `SystemHealth`, `Program.cs` middleware
- Owner UI at `/Owner/FeatureFlags` is a **no-op** — OnPost discards form data

### Migration Strategy
- Add all System 2 flags to `FeatureFlagSeed.cs` so they exist in DB
- Replace every `IConfiguration.GetValue<bool>("Features:...")` with `IFeatureFlagService.IsEnabledAsync("FF_...")`
- Rewrite `/Owner/FeatureFlags` to use `IFeatureFlagService` for read AND write
- Seed initial DB values from current appsettings values (one-time migration)
- Remove `Features:*` section from appsettings entirely

---

## Task 1: Expand FeatureFlagSeed with All Missing Flags

**Files:**
- Modify: `Data/SeedData/FeatureFlagSeed.cs`

**Why:** The DB currently has 9 `FF_*` flags (UI cosmetics). We need to add the 12 operational flags that today live only in appsettings. After this, every flag the system needs exists in the DB.

**Step 1: Add new flag constants to `Flags` class**

Add these constants after the existing Excel Calendar block in `FeatureFlagSeed.Flags`:

```csharp
// Operational flags (migrated from appsettings Features:*)
public const string EnforceCompanyScope = "FF_ENFORCE_COMPANY_SCOPE";
public const string EnableDirectorRole = "FF_ENABLE_DIRECTOR_ROLE";
public const string AllowPublicSignup = "FF_ALLOW_PUBLIC_SIGNUP";
public const string EnableApiKeyManagement = "FF_ENABLE_API_KEY_MANAGEMENT";
public const string EnableDailyNotifications = "FF_ENABLE_DAILY_NOTIFICATIONS";
public const string EnforceRankEligibility = "FF_ENFORCE_RANK_ELIGIBILITY";
public const string EnableDutyRotation = "FF_ENABLE_DUTY_ROTATION";
public const string EnableCompanySwitcher = "FF_ENABLE_COMPANY_SWITCHER";

// API endpoint flags
public const string ApiUsersListEnabled = "FF_API_USERS_LIST";
public const string ApiUsersGetEnabled = "FF_API_USERS_GET";
public const string ApiUsersCreateEnabled = "FF_API_USERS_CREATE";
public const string ApiUsersUpdateEnabled = "FF_API_USERS_UPDATE";
public const string ApiShiftsListEnabled = "FF_API_SHIFTS_LIST";
public const string ApiShiftsGetEnabled = "FF_API_SHIFTS_GET";
public const string ApiTimeOffListEnabled = "FF_API_TIMEOFF_LIST";
public const string ApiTimeOffGetEnabled = "FF_API_TIMEOFF_GET";
public const string ApiTimeOffCreateEnabled = "FF_API_TIMEOFF_CREATE";
public const string ApiTimeOffApproveEnabled = "FF_API_TIMEOFF_APPROVE";
public const string ApiTimeOffDeclineEnabled = "FF_API_TIMEOFF_DECLINE";
public const string ApiNotificationsListEnabled = "FF_API_NOTIFICATIONS_LIST";
public const string ApiNotificationsGetEnabled = "FF_API_NOTIFICATIONS_GET";
public const string ApiNotificationsMarkReadEnabled = "FF_API_NOTIFICATIONS_MARKREAD";
public const string ApiNotificationsMarkAllReadEnabled = "FF_API_NOTIFICATIONS_MARKALLREAD";
public const string ApiAnalyticsSummaryEnabled = "FF_API_ANALYTICS_SUMMARY";
public const string ApiAuditLogsListEnabled = "FF_API_AUDITLOGS_LIST";
public const string ApiSwapRequestsListEnabled = "FF_API_SWAPREQUESTS_LIST";
public const string ApiSwapRequestsGetEnabled = "FF_API_SWAPREQUESTS_GET";
public const string ApiSwapRequestsCreateEnabled = "FF_API_SWAPREQUESTS_CREATE";
public const string ApiSwapRequestsApproveEnabled = "FF_API_SWAPREQUESTS_APPROVE";
public const string ApiSwapRequestsDeclineEnabled = "FF_API_SWAPREQUESTS_DECLINE";
public const string ApiSwapRequestsDeleteEnabled = "FF_API_SWAPREQUESTS_DELETE";
public const string ApiChoresListEnabled = "FF_API_CHORES_LIST";
public const string ApiChoresGetEnabled = "FF_API_CHORES_GET";
public const string ApiChoresCreateEnabled = "FF_API_CHORES_CREATE";
public const string ApiChoresUpdateEnabled = "FF_API_CHORES_UPDATE";
public const string ApiChoresDeleteEnabled = "FF_API_CHORES_DELETE";
public const string ApiOnDutyListEnabled = "FF_API_ONDUTY_LIST";
public const string ApiOnDutyGetEnabled = "FF_API_ONDUTY_GET";
public const string ApiOnDutyCreateEnabled = "FF_API_ONDUTY_CREATE";
public const string ApiOnDutyUpdateEnabled = "FF_API_ONDUTY_UPDATE";
public const string ApiOnDutyDeleteEnabled = "FF_API_ONDUTY_DELETE";
public const string ApiFeedbackListEnabled = "FF_API_FEEDBACK_LIST";
public const string ApiFeedbackGetEnabled = "FF_API_FEEDBACK_GET";
public const string ApiFeedbackCreateEnabled = "FF_API_FEEDBACK_CREATE";
public const string ApiFeedbackUpdateStatusEnabled = "FF_API_FEEDBACK_UPDATESTATUS";
public const string ApiFeedbackDeleteEnabled = "FF_API_FEEDBACK_DELETE";
```

**Step 2: Add corresponding `FeatureFlag` objects to `GetFeatureFlags()`**

Add after the existing 9 flags. All new operational flags default to **enabled** (`IsEnabled = true`) because that matches current production behavior. All new API flags also default to **enabled**.

```csharp
// Operational flags — default to enabled (matches current appsettings behavior)
new FeatureFlag { Name = Flags.EnforceCompanyScope, IsEnabled = true, Description = "Enforce tenant isolation via CompanyId query filters on SaveChanges", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.EnableDirectorRole, IsEnabled = true, Description = "Enable Director role functionality and Director test user seeding", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.AllowPublicSignup, IsEnabled = true, Description = "Allow new users to self-register via /Auth/Signup", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.EnableApiKeyManagement, IsEnabled = true, Description = "Enable API key management features in Admin panel", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.EnableDailyNotifications, IsEnabled = true, Description = "Enable background job for daily email notifications", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.EnforceRankEligibility, IsEnabled = true, Description = "Enforce military rank eligibility checks during duty rotation", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.EnableDutyRotation, IsEnabled = true, Description = "Enable automatic duty rotation scheduler", CreatedAt = now, UpdatedAt = now },
new FeatureFlag { Name = Flags.EnableCompanySwitcher, IsEnabled = false, Description = "Enable company switcher in navigation layout", CreatedAt = now, UpdatedAt = now },
// API endpoint flags — all default to enabled
new FeatureFlag { Name = Flags.ApiUsersListEnabled, IsEnabled = true, Description = "API: GET /api/v1/users", CreatedAt = now, UpdatedAt = now },
// ... (one per API flag constant, all IsEnabled = true)
```

NOTE: For brevity, the full list of API flag objects follows the same pattern. Each maps 1:1 to its `Flags.*` constant.

**Step 3: Verify seeding runs on next startup**

The existing upsert logic in `Program.cs` (lines 969-980) inserts flags where `!existingNames.Contains(f.Name)`. New flags will be auto-inserted. Existing 9 flags remain untouched.

Run: `dotnet build --no-restore && dotnet run` — check startup logs for "Seeded N new feature flags".

**Step 4: Commit**

```
feat: expand FeatureFlagSeed with all operational + API flags
```

---

## Task 2: Add IFeatureFlagService Helper for Synchronous Contexts

**Files:**
- Modify: `Services/IFeatureFlagService.cs`
- Modify: `Services/FeatureFlagService.cs`

**Why:** Several consumers (e.g., `_Layout.cshtml`, `CompanyIdInterceptor`) run in synchronous contexts where `await` is awkward or impossible. We need a sync-safe method. Also, many consumers don't need per-user/per-company scoping — they just want the global value.

**Step 1: Add `IsEnabled(string flagName)` sync method to interface**

```csharp
/// <summary>
/// Synchronous global-scope check. Checks cache first; if miss, queries DB synchronously.
/// Use this in Razor views and synchronous interceptors.
/// </summary>
bool IsEnabled(string flagName);
```

**Step 2: Implement in FeatureFlagService**

```csharp
public bool IsEnabled(string flagName)
{
    var cacheKey = BuildCacheKey(flagName, null, null);
    if (_cache.TryGetValue(cacheKey, out bool cachedValue))
        return cachedValue;

    // Synchronous DB query — safe because this is a simple indexed lookup
    var globalFlag = _context.FeatureFlags
        .IgnoreQueryFilters()
        .FirstOrDefault(f => f.Name == flagName && f.CompanyId == null && f.UserId == null);

    bool isEnabled = globalFlag?.IsEnabled ?? false;
    _cache.Set(cacheKey, isEnabled, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = CacheExpiration
    });

    return isEnabled;
}
```

**Step 3: Build and run tests**

Run: `dotnet build --no-restore` — expect 0W/0E.
Run: `dotnet test --no-build --verbosity quiet` — expect 236 pass.

**Step 4: Commit**

```
feat: add synchronous IsEnabled() to IFeatureFlagService for views/interceptors
```

---

## Task 3: Wire _Layout.cshtml to IFeatureFlagService

**Files:**
- Modify: `Pages/Shared/_Layout.cshtml` (lines 8, 24, 28-38)

**Why:** `_Layout.cshtml` currently injects `IConfiguration` and reads 6 feature flag keys directly. This is the most visible consumer — every page render hits it.

**Step 1: Replace IConfiguration injection with IFeatureFlagService**

Change line 8 from:
```cshtml
@inject Microsoft.Extensions.Configuration.IConfiguration Configuration
```
To:
```cshtml
@inject ShiftManager.Services.IFeatureFlagService FeatureFlags
```

**Step 2: Replace all flag reads**

Replace lines 24-38:

```cshtml
var enableCompanySwitcher = FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.EnableCompanySwitcher);
// ...
var excelCalendarsEnabled = FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.ExcelCalendars);
var shiftsCalendarUrl = excelCalendarsEnabled && FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.ExcelCalendarShifts)
    ? "/Calendar/Shifts" : "/Calendar/Month";
var shiftsTableUrl = excelCalendarsEnabled && FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.ExcelCalendarShifts)
    ? "/Calendar/Shifts" : "/Calendar/Table";
var choresCalendarUrl = excelCalendarsEnabled && FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.ExcelCalendarChores)
    ? "/Calendar/Chores" : "/Public/Chores";
var onCallCalendarUrl = excelCalendarsEnabled && FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.ExcelCalendarOnCall)
    ? "/Calendar/OnCall" : "/Public/OnDuty";
var overviewCalendarUrl = excelCalendarsEnabled && FeatureFlags.IsEnabled(ShiftManager.Data.SeedData.FeatureFlagSeed.Flags.ExcelCalendarOverview)
    ? "/Calendar/Overview" : "/Calendar/Month";
```

NOTE: If `_Layout.cshtml` also uses `Configuration` for non-feature-flag purposes elsewhere in the file, keep both injections. Only remove the `IConfiguration` injection if it's exclusively used for feature flags.

**Step 3: Build, run app, click through nav**

Run: `dotnet build --no-restore` — 0W/0E.
Manual: Open app → verify all nav links still point to correct pages.

**Step 4: Commit**

```
refactor: wire _Layout.cshtml to IFeatureFlagService (DB-backed)
```

---

## Task 4: Wire CompanyIdInterceptor to IFeatureFlagService

**Files:**
- Modify: `Data/CompanyIdInterceptor.cs` (line ~55)

**Why:** This is the tenant isolation gate. It currently reads `Features:EnforceCompanyScope` from `IConfiguration` with a default of `false` — which is a dangerous default. After migration, the DB flag `FF_ENFORCE_COMPANY_SCOPE` defaults to `true` (from seed).

**Step 1: Add IFeatureFlagService to constructor injection**

The interceptor likely already has `IConfiguration` injected. Add `IFeatureFlagService` to the constructor. If the interceptor is created via DI (registered in `Program.cs`), this should work. If it's created manually, we may need to resolve from `IServiceProvider`.

**IMPORTANT:** Check how `CompanyIdInterceptor` is instantiated. If it's `new CompanyIdInterceptor(config)`, we can't inject easily — use `IServiceProvider.GetRequiredService<IFeatureFlagService>()` from the `DbContext` service provider instead.

**Step 2: Replace config read**

Replace:
```csharp
var enforceScope = _configuration.GetValue<bool>("Features:EnforceCompanyScope", false);
```
With:
```csharp
var enforceScope = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.EnforceCompanyScope);
```

**Step 3: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 4: Commit**

```
refactor: wire CompanyIdInterceptor to DB-backed feature flags
```

---

## Task 5: Wire Remaining Service-Layer Consumers

**Files:**
- Modify: `Services/DailyNotificationJob.cs` (lines 39, 53) — `Features:EnableDailyNotifications`
- Modify: `Services/DutyRotationService.cs` (lines 247, 397) — `Features:EnforceRankEligibility`
- Modify: `Services/OnDutyService.cs` (line 257) — `Features:EnforceRankEligibility`

**Why:** These three services read feature flags at runtime per-request or per-tick. They all already have DI constructors so adding `IFeatureFlagService` is straightforward.

**Step 1: For each service, add `IFeatureFlagService` to constructor**

```csharp
private readonly IFeatureFlagService _featureFlagService;
// Add to constructor params and assign
```

**Step 2: Replace each config read**

| File | Old | New |
|------|-----|-----|
| `DailyNotificationJob.cs:39` | `_configuration.GetValue<bool>("Features:EnableDailyNotifications", true)` | `await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnableDailyNotifications)` |
| `DailyNotificationJob.cs:53` | same | same |
| `DutyRotationService.cs:247` | `_configuration.GetValue<bool>("Features:EnforceRankEligibility", false)` | `await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnforceRankEligibility)` |
| `DutyRotationService.cs:397` | same | same |
| `OnDutyService.cs:257` | `_configuration.GetValue<bool>("Features:EnforceRankEligibility", false)` | `await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnforceRankEligibility)` |

**Step 3: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 4: Commit**

```
refactor: wire DailyNotificationJob, DutyRotationService, OnDutyService to DB flags
```

---

## Task 6: Wire Page-Level Consumers (Signup, SystemHealth)

**Files:**
- Modify: `Pages/Auth/Signup.cshtml.cs` (lines 77, 115) — `Features:AllowPublicSignup`
- Modify: `Pages/Api/Signup/GetSignupOptions.cshtml.cs` (line 228) — `Features:AllowPublicSignup`
- Modify: `Pages/Owner/SystemHealth.cshtml.cs` (lines 206, 289) — `Features:EnableDailyNotifications`, `Features:AllowPublicSignup`

**Step 1: Add `IFeatureFlagService` to each page's constructor**

**Step 2: Replace config reads with service calls**

All of these are in async methods, so use `IsEnabledAsync`:
```csharp
var allowSignup = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.AllowPublicSignup);
```

**Step 3: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

Manual: Visit `/Auth/Signup` — should still work. Visit `/Owner/SystemHealth` — flags section should render.

**Step 4: Commit**

```
refactor: wire Signup and SystemHealth pages to DB flags
```

---

## Task 7: Wire API Controllers

**Files:**
- Modify: All 9 API V1 controllers in `Controllers/Api/V1/`

**Why:** Each controller reads `Features:Api:{Category}:{Operation}Enabled` at the top of each action. We replace with `IFeatureFlagService`.

**Step 1: For each controller, add `IFeatureFlagService` to constructor**

**Step 2: Replace each config read**

Pattern — replace:
```csharp
if (!_configuration.GetValue<bool>("Features:Api:Users:ListEnabled", true))
    return StatusCode(503, ...);
```
With:
```csharp
if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiUsersListEnabled))
    return StatusCode(503, ...);
```

Apply to all 37 API endpoint flag checks across the 9 controllers.

**Step 3: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 4: Commit**

```
refactor: wire all API V1 controllers to DB flags
```

---

## Task 8: Wire Program.cs Middleware and Startup

**Files:**
- Modify: `Program.cs` (lines ~857, 1210-1264, 1373-1378, 1399-1400, 1527-1536)

**Why:** Program.cs has both startup-time flag reads (seeding, banner) and runtime middleware (Excel calendar redirect). The middleware block needs `IFeatureFlagService` resolved per-request. Startup-time reads (banner, warnings) can read from DB directly since seeding has already run by that point.

**Step 1: Replace Excel Calendar middleware flag reads (lines 1210-1264)**

The inline `app.Use(...)` middleware currently resolves `IConfiguration` from `context.RequestServices`. Change to resolve `IFeatureFlagService`:

```csharp
var featureFlags = context.RequestServices.GetRequiredService<IFeatureFlagService>();
var excelEnabled = featureFlags.IsEnabled(FeatureFlagSeed.Flags.ExcelCalendars);
// ... same pattern for sub-flags
```

**Step 2: Replace startup banner and warning flag reads**

For `DisplayStartupBanner` and the `AllowPublicSignup` warning, these run once at startup. Resolve `IFeatureFlagService` from the app's service provider:

```csharp
using (var scope = app.Services.CreateScope())
{
    var featureFlags = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
    var publicSignup = await featureFlags.IsEnabledAsync(FeatureFlagSeed.Flags.AllowPublicSignup);
    // ...
}
```

**Step 3: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 4: Commit**

```
refactor: wire Program.cs middleware and startup to DB flags
```

---

## Task 9: Rewrite Owner/FeatureFlags UI to Use IFeatureFlagService

**Files:**
- Modify: `Pages/Owner/FeatureFlags.cshtml.cs` — complete rewrite
- Modify: `Pages/Owner/FeatureFlags.cshtml` — update to use DB model

**Why:** This is the payoff — the UI becomes functional. It reads from DB on GET and writes to DB on POST. Changes take effect within 1 minute (cache TTL) without restart.

**Step 1: Rewrite FeatureFlags.cshtml.cs**

Replace the entire page model. New design:
- `OnGet`: calls `_featureFlagService.GetAllFlagsAsync()` → populates a `List<FeatureFlagViewModel>` with `Name`, `IsEnabled`, `Description`, `Category` (computed from name prefix)
- `OnPost`: receives form data as `Dictionary<string, bool>`, calls `_featureFlagService.SetFlagAsync()` for each changed flag
- Group flags by category for display: "Core", "Excel Calendars", "API Endpoints", "UI Features"

```csharp
public class FeatureFlagsModel : LocalizedPageModel
{
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeatureFlagsModel> _logger;

    public List<FeatureFlagViewModel> Flags { get; set; } = new();
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        var allFlags = await _featureFlagService.GetAllFlagsAsync();
        Flags = allFlags
            .Where(f => f.CompanyId == null && f.UserId == null) // Global flags only
            .Select(f => new FeatureFlagViewModel
            {
                Name = f.Name,
                IsEnabled = f.IsEnabled,
                Description = f.Description ?? "",
                Category = CategorizeFlag(f.Name)
            })
            .OrderBy(f => f.Category)
            .ThenBy(f => f.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var allFlags = (await _featureFlagService.GetAllFlagsAsync())
            .Where(f => f.CompanyId == null && f.UserId == null)
            .ToList();

        int changedCount = 0;
        foreach (var flag in allFlags)
        {
            // Form checkbox: present = true, absent = false
            var formKey = $"flag_{flag.Name}";
            var newValue = Request.Form.ContainsKey(formKey);

            if (flag.IsEnabled != newValue)
            {
                await _featureFlagService.SetFlagAsync(flag.Name, newValue);
                _logger.LogInformation("Feature flag {Name} changed: {Old} → {New}", flag.Name, flag.IsEnabled, newValue);
                changedCount++;
            }
        }

        SuccessMessage = changedCount > 0
            ? $"Updated {changedCount} flag(s). Changes take effect within 1 minute."
            : "No changes detected.";

        // Reload for display
        await OnGetAsync();
        return Page();
    }

    private static string CategorizeFlag(string name) => name switch
    {
        _ when name.StartsWith("FF_API_") => "API Endpoints",
        _ when name.StartsWith("FF_EXCEL_") => "Excel Calendars",
        _ when name.StartsWith("FF_NEW_") || name.StartsWith("FF_WIDGETS") || name.StartsWith("FF_SCOPE") => "UI Features",
        _ => "Core"
    };
}

public class FeatureFlagViewModel
{
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
}
```

**Step 2: Update FeatureFlags.cshtml**

Replace form contents — iterate `Model.Flags` grouped by `Category`, render toggle switch per flag with `name="flag_{Name}"`. Keep existing CSS. Add success/error message display.

**Step 3: Build, run, test UI**

Run: `dotnet build --no-restore`
Manual: Visit `/Owner/FeatureFlags` → verify all flags displayed → toggle one → save → verify it persisted (refresh shows new value).

**Step 4: Commit**

```
feat: rewrite FeatureFlags UI to read/write from DB via IFeatureFlagService
```

---

## Task 10: One-Time Migration — Seed DB from Current appsettings Values

**Files:**
- Modify: `Program.cs` — add migration block after existing flag seeding

**Why:** Existing deployments have the 9 original FF_* flags in DB (all `IsEnabled = false`) and the new operational flags will be seeded as `true`. But the original 9 FF_* flags need their values updated to match what appsettings currently has (all `true` in current config). This one-time migration reads appsettings values and updates DB flags to match.

**Step 1: Add migration block in Program.cs after feature flag seeding**

```csharp
// ONE-TIME MIGRATION: Sync DB flag values from appsettings (for flags that existed before migration)
// This ensures the DB reflects the intended state from appsettings before we remove the appsettings keys
{
    var configMigrationMap = new Dictionary<string, string>
    {
        ["Features:ExcelCalendars"] = FeatureFlagSeed.Flags.ExcelCalendars,
        ["Features:ExcelCalendarShifts"] = FeatureFlagSeed.Flags.ExcelCalendarShifts,
        ["Features:ExcelCalendarChores"] = FeatureFlagSeed.Flags.ExcelCalendarChores,
        ["Features:ExcelCalendarOnCall"] = FeatureFlagSeed.Flags.ExcelCalendarOnCall,
        ["Features:ExcelCalendarOverview"] = FeatureFlagSeed.Flags.ExcelCalendarOverview,
        ["Features:EnforceCompanyScope"] = FeatureFlagSeed.Flags.EnforceCompanyScope,
        ["Features:EnableDirectorRole"] = FeatureFlagSeed.Flags.EnableDirectorRole,
        ["Features:AllowPublicSignup"] = FeatureFlagSeed.Flags.AllowPublicSignup,
        ["Features:EnableDailyNotifications"] = FeatureFlagSeed.Flags.EnableDailyNotifications,
        ["Features:EnforceRankEligibility"] = FeatureFlagSeed.Flags.EnforceRankEligibility,
        ["Features:EnableDutyRotation"] = FeatureFlagSeed.Flags.EnableDutyRotation,
    };

    var migrationNeeded = false;
    foreach (var (configKey, flagName) in configMigrationMap)
    {
        var configValue = builder.Configuration.GetValue<bool?>(configKey);
        if (!configValue.HasValue) continue; // Key not in appsettings, skip

        var dbFlag = await db.FeatureFlags.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Name == flagName && f.CompanyId == null && f.UserId == null);
        if (dbFlag != null && dbFlag.IsEnabled != configValue.Value)
        {
            dbFlag.IsEnabled = configValue.Value;
            dbFlag.UpdatedAt = DateTime.UtcNow;
            migrationNeeded = true;
            logger.LogInformation("Flag migration: {FlagName} set to {Value} (from {ConfigKey})", flagName, configValue.Value, configKey);
        }
    }
    if (migrationNeeded)
        await db.SaveChangesAsync();
}
```

**Step 2: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 3: Commit**

```
feat: one-time migration to sync DB flags from appsettings values
```

---

## Task 11: Remove Features Section from appsettings

**Files:**
- Modify: `appsettings.json` — remove `Features` block entirely
- Modify: `appsettings.Development.json` — remove `Features` overrides
- Modify: `appsettings.Production.json` — remove `Features` overrides and dead `FeatureFlagDefaults`

**Why:** All consumers now read from DB. The appsettings keys are dead code. Removing them eliminates confusion and prevents the "two sources of truth" problem.

**Step 1: Remove `"Features": { ... }` block from appsettings.json (lines 22-94)**

**Step 2: Remove `"Features": { ... }` block from appsettings.Development.json (lines 11-20)**

After cleanup, Dev file should only contain:
```json
{
  "Logging": { ... }
}
```

**Step 3: Remove from appsettings.Production.json**

Remove `"Features"` block (lines 24-28) and `"FeatureFlagDefaults"` block (lines 29-40).

**Step 4: Build and test — CRITICAL**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

Manual smoke test:
- Visit `/Auth/Signup` — should work (AllowPublicSignup flag in DB is true)
- Visit `/Calendar/Shifts` — nav links should work (Excel flags in DB)
- Visit `/Owner/FeatureFlags` — all flags visible with correct values
- Toggle a flag → save → verify it sticks

**Step 5: Commit**

```
refactor: remove Features section from all appsettings files (now DB-backed)
```

---

## Task 12: Fix SystemAlertsViewComponent Config Path Bug

**Files:**
- Modify: `ViewComponents/SystemAlertsViewComponent.cs` (line 110)

**Why:** Bug found during audit — reads `Security:ApiKeyHmacSecret` but `Program.cs` and `appsettings.Production.json` set `ApiKeyHmacSecret` (root level). The alert always fires even when the secret IS configured.

**Step 1: Fix the config path**

Change line 110 from:
```csharp
var hmacSecret = _configuration.GetValue<string>("Security:ApiKeyHmacSecret");
```
To:
```csharp
var hmacSecret = _configuration.GetValue<string>("ApiKeyHmacSecret");
```

**Step 2: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 3: Commit**

```
fix: SystemAlertsViewComponent reads wrong config path for ApiKeyHmacSecret
```

---

## Task 13: Clean Up appsettings for Air-Gapped Deployment

**Files:**
- Modify: `appsettings.json` — remove placeholder secrets, add comments
- Modify: `appsettings.Production.json` — remove stale overrides

**Why:** Hardening for release. The base config should be safe to deploy without edits. Sensitive values go in Production override only.

**Step 1: In appsettings.json**

- Remove `_SeedingComment` key (JSON noise)
- Remove empty `Email.ApiKey` value (move to Production only)
- Replace placeholder `Email.ApiUrl` with empty string
- Replace placeholder `Griffin.BaseUrl` and `Griffin.TokenConsumerUrl` with empty strings

**Step 2: In appsettings.Production.json**

- Remove stale `EnableDutyRotation: false` override (no longer exists in base)
- Remove dead `FeatureFlagDefaults` section (already done in Task 11)
- Remove redundant `EnableApiKeyManagement: true` (matches base)

**Step 3: Build and test**

Run: `dotnet build --no-restore && dotnet test --no-build --verbosity quiet`

**Step 4: Commit**

```
chore: harden appsettings — remove placeholder secrets and stale overrides
```

---

## Task 14: Final Validation

**Step 1: Full build and test**

Run: `dotnet build --no-restore` — expect 0W/0E
Run: `dotnet test --no-build --verbosity quiet` — expect 236 pass

**Step 2: Startup smoke test**

Run the app. Check startup logs for:
- "Seeded N new feature flags" (new operational + API flags)
- "Flag migration: ..." (one-time sync from appsettings)
- No errors about missing config keys

**Step 3: Manual UI validation**

| Page | Action | Expected |
|------|--------|----------|
| `/Auth/Signup` | Load page | Signup form renders (AllowPublicSignup = true in DB) |
| `/Calendar/Shifts` | Load page | Calendar renders |
| `/Owner/FeatureFlags` | Load page | All flags grouped by category, correct values |
| `/Owner/FeatureFlags` | Toggle a flag, save | Success message, value persists on refresh |
| `/Owner/FeatureFlags` | Toggle back, save | Reverts correctly |
| `/Owner/SystemHealth` | Load page | No false HMAC warning (if secret configured) |
| Any API endpoint | Call `GET /api/v1/users` | Returns data (API flag enabled in DB) |

**Step 4: Commit**

```
chore: final validation — unified feature flags complete
```

---

## Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| DB flag seeding fails → flags missing → features disabled | HIGH | Seed defaults to `true` for operational flags; startup logs seed counts; catch-up seeding is idempotent |
| Cache stale for up to 1 min after toggle | LOW | Acceptable for admin-toggled flags. Document in UI |
| Sync `IsEnabled()` blocks thread on cache miss | LOW | Single indexed query on small table; 1-min cache means rare misses |
| Existing tests mock `IConfiguration` for flags | MEDIUM | Must update test mocks to mock `IFeatureFlagService` instead — check test files |
| Air-gapped deploy: old appsettings.json on target still has `Features:*` | LOW | One-time migration reads from appsettings if present; safe to leave stale keys (unused) |

## Deployment Note for Air-Gapped Environment

After this change, your deployment process improves:

**Before:** Edit `appsettings.json` on target → override Griffin/Email settings → risk losing overrides on next deploy.

**After:**
1. Create `appsettings.Production.json` on target machine ONCE with Griffin/Email/HMAC settings
2. Every deploy: stop IIS → replace `FinalProductPublish` folder → start IIS
3. `appsettings.Production.json` is NOT in the publish output, so it survives the replace
4. Feature flags are in the DB (`app.db`), which also survives the replace
5. No manual edits needed on deploy
