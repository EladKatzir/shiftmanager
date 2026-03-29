# Release Audit Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 9 findings from the forensic release-readiness audit to bring the app to releasable state.

**Architecture:** Direct code fixes — no new architecture. 6 trivial string/config edits, 1 small FK migration change, 1 medium new BackgroundService, and 1 large phased test coverage effort.

**Tech Stack:** ASP.NET Core 8.0, Razor Pages, EF Core, xUnit, SQLite

**Spec:** `docs/superpowers/specs/2026-03-27-release-audit-fixes-design.md`

---

### Task 1: Fix Phantom Grant Keys in Hierarchy API Endpoints

**Files:**
- Modify: `Pages/Api/Hierarchy/Rename.cshtml.cs:16-18`
- Modify: `Pages/Api/Hierarchy/Create.cshtml.cs:19-21`
- Modify: `Pages/Api/Hierarchy/Delete.cshtml.cs:16-18`
- Modify: `ViewComponents/HierarchyTreeViewComponent.cs:61-64`

- [ ] **Step 1: Fix Rename.cshtml.cs**

Change line 16-18 from:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditHierarchy policy;
// hierarchy rename needs cross-company entity lookups
[Authorize(Policy = "Grant:EditHierarchy")]
```
to:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageHierarchy policy;
// hierarchy rename needs cross-company entity lookups
[Authorize(Policy = "Grant:ManageHierarchy")]
```

- [ ] **Step 2: Fix Create.cshtml.cs**

Change line 19-21 from:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:CreateHierarchy policy;
// hierarchy entity creation needs cross-company parent lookups
[Authorize(Policy = "Grant:CreateHierarchy")]
```
to:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageHierarchy policy;
// hierarchy entity creation needs cross-company parent lookups
[Authorize(Policy = "Grant:ManageHierarchy")]
```

- [ ] **Step 3: Fix Delete.cshtml.cs**

Change line 16-18 from:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:DeleteHierarchy policy;
// hierarchy deletion needs cross-company child existence checks
[Authorize(Policy = "Grant:DeleteHierarchy")]
```
to:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageHierarchy policy;
// hierarchy deletion needs cross-company child existence checks
[Authorize(Policy = "Grant:ManageHierarchy")]
```

- [ ] **Step 4: Fix HierarchyTreeViewComponent.cs**

Change lines 61, 63, 64 (do NOT change line 62 — `ReorderHierarchy` is valid):
```csharp
// Before (lines 60-64):
// Check grants for edit capabilities
canEdit = await _grantService.HasGrantAsync(userId, "EditHierarchy");
canReorder = await _grantService.HasGrantAsync(userId, "ReorderHierarchy");
canDelete = await _grantService.HasGrantAsync(userId, "DeleteHierarchy");
canAddChild = await _grantService.HasGrantAsync(userId, "CreateHierarchy");

// After:
// Check grants for edit capabilities
canEdit = await _grantService.HasGrantAsync(userId, "ManageHierarchy");
canReorder = await _grantService.HasGrantAsync(userId, "ReorderHierarchy");
canDelete = await _grantService.HasGrantAsync(userId, "ManageHierarchy");
canAddChild = await _grantService.HasGrantAsync(userId, "ManageHierarchy");
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build`
Expected: 0 errors, 0 warnings

- [ ] **Step 6: Commit**

```bash
git add Pages/Api/Hierarchy/Rename.cshtml.cs Pages/Api/Hierarchy/Create.cshtml.cs Pages/Api/Hierarchy/Delete.cshtml.cs ViewComponents/HierarchyTreeViewComponent.cs
git commit -m "fix: replace phantom grant keys with ManageHierarchy for hierarchy CRUD"
```

---

### Task 2: Delete Deprecated TechShift/Eligible Endpoint

**Files:**
- Delete: `Pages/Api/TechShift/Eligible.cshtml`
- Delete: `Pages/Api/TechShift/Eligible.cshtml.cs`

- [ ] **Step 1: Delete both files**

```bash
rm Pages/Api/TechShift/Eligible.cshtml Pages/Api/TechShift/Eligible.cshtml.cs
```

If the `Pages/Api/TechShift/` directory is now empty, delete it too:
```bash
rmdir Pages/Api/TechShift 2>/dev/null || true
```

- [ ] **Step 2: Check for any remaining references**

Search for `TechShift/Eligible` or `TechShift` in the codebase (excluding docs/tests):
```bash
grep -r "TechShift" Pages/ Services/ Controllers/ Middleware/ wwwroot/ --include="*.cs" --include="*.cshtml" --include="*.js" -l
```
Expected: No matches (the endpoint was verified as unused).

If `Middleware/ApiAuthenticationMiddleware.cs` has a whitelist entry for `/Api/TechShift`, remove it.

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add -A Pages/Api/TechShift/
git commit -m "chore: delete deprecated TechShift/Eligible endpoint (eligibility now in ShiftType model)"
```

---

### Task 3: Add Logging to SystemAlertsViewComponent

**Files:**
- Modify: `ViewComponents/SystemAlertsViewComponent.cs:24-31,71,83,106,115,133,152`

- [ ] **Step 1: Add ILogger to constructor**

In `ViewComponents/SystemAlertsViewComponent.cs`, add the logger field and constructor parameter.

Add field after line 21:
```csharp
private readonly IStringLocalizer<SharedResources> _localizer;
private readonly ILogger<SystemAlertsViewComponent> _logger;
```

Change constructor at line 24 from:
```csharp
public SystemAlertsViewComponent(IMemoryCache cache, IConfiguration configuration, IWebHostEnvironment env, IGrantService grantService, IStringLocalizer<SharedResources> localizer)
{
    _cache = cache;
    _configuration = configuration;
    _env = env;
    _grantService = grantService;
    _localizer = localizer;
}
```
to:
```csharp
public SystemAlertsViewComponent(IMemoryCache cache, IConfiguration configuration, IWebHostEnvironment env, IGrantService grantService, IStringLocalizer<SharedResources> localizer, ILogger<SystemAlertsViewComponent> logger)
{
    _cache = cache;
    _configuration = configuration;
    _env = env;
    _grantService = grantService;
    _localizer = localizer;
    _logger = logger;
}
```

- [ ] **Step 2: Replace 6 empty catch blocks with logging**

Replace each `catch { ... }` at lines 71, 83, 106, 115, 133, 152:

Line 71: `catch { /* Ignore WAL check errors */ }` →
```csharp
catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check WAL size"); }
```

Line 83: `catch { /* Ignore disk check errors */ }` →
```csharp
catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check disk space"); }
```

Line 106: `catch { /* Ignore backup check errors */ }` →
```csharp
catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check backup status"); }
```

Line 115: `catch { /* Ignore */ }` →
```csharp
catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check data protection"); }
```

Line 133: `catch { /* Ignore font check errors */ }` →
```csharp
catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check font availability"); }
```

Line 152: `catch { /* Ignore notification stats errors */ }` →
```csharp
catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check notification stats"); }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add ViewComponents/SystemAlertsViewComponent.cs
git commit -m "fix: add logging to SystemAlertsViewComponent catch blocks instead of silently swallowing"
```

---

### Task 4: Fix Grant Count Test Assertion

**Files:**
- Modify: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs:891-892`

- [ ] **Step 1: Update the assertion**

Change line 891-892 from:
```csharp
        // Assert - 125 grants: shift (71) + duty (10) + chore (14) + vacation (5) + swap (3) + user management (10) + grant management (4) + hierarchy (10) + settings (4) + analytics (3) + email (2) + system (4) + navigation (4) + join request (3) + helper molecules/molecules (7) + hierarchy reorder (1) + hierarchy manage (1) + home rotation (1)
        grantTypes.Should().HaveCount(125, "Should have exactly 125 grant types including all shift, duty, chore, vacation, swap, user management, grant management, hierarchy, settings, analytics, email, system, navigation, join request, and home rotation grants");
```
to:
```csharp
        // Assert - 127 grants (updated 2026-03-27: +ManageStores, +ManageHomeTypes)
        grantTypes.Should().HaveCount(127, "Should have exactly 127 grant types — if this fails, a grant was added or removed without updating this test");
```

- [ ] **Step 2: Run the test**

Run: `cd ShiftManager.Tests && dotnet test --filter "GrantTypeSeed_Creates_ExpectedNumberOfGrants" --verbosity normal`
Expected: 1 test PASSED

- [ ] **Step 3: Run full test suite**

Run: `cd ShiftManager.Tests && dotnet test --verbosity normal`
Expected: All tests pass (0 failures)

- [ ] **Step 4: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs
git commit -m "fix: update grant count test assertion from 125 to 127"
```

---

### Task 5: Fix Health Endpoint URL in offline-handler.js

**Files:**
- Modify: `wwwroot/js/offline-handler.js:504`

- [ ] **Step 1: Fix the URL**

Change line 504 from:
```javascript
            const response = await fetch('/api/health', {
```
to:
```javascript
            const response = await fetch('/health', {
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add wwwroot/js/offline-handler.js
git commit -m "fix: correct health endpoint URL in offline handler (/api/health -> /health)"
```

---

### Task 6: Switch Diagnostic Page to Grant-Based Auth

**Files:**
- Modify: `Pages/Diagnostic.cshtml.cs:9-10`

- [ ] **Step 1: Update auth attribute and comment**

Change lines 9-10 from:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner-only diagnostic page (B-09: restricted from AdminAccess to Owner)
[Authorize(Roles = nameof(ShiftManager.Models.Support.UserRole.Owner))]
```
to:
```csharp
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — AdminAccess-gated diagnostic page
[Authorize(Policy = "Grant:AdminAccess")]
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add Pages/Diagnostic.cshtml.cs
git commit -m "fix: switch Diagnostic page from legacy role auth to grant-based AdminAccess"
```

---

### Task 7: Add Missing Localization Key

**Files:**
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

- [ ] **Step 1: Add English key**

In `Resources/SharedResources.resx`, add (alphabetically near other `Calendar_Empty_` keys):
```xml
<data name="Calendar_Empty_NoShiftsForSelection" xml:space="preserve">
  <value>No shifts found for this selection</value>
</data>
```

- [ ] **Step 2: Add Hebrew key**

In `Resources/SharedResources.he-IL.resx`, add at the same position:
```xml
<data name="Calendar_Empty_NoShiftsForSelection" xml:space="preserve">
  <value>לא נמצאו משמרות עבור בחירה זו</value>
</data>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix: add missing Calendar_Empty_NoShiftsForSelection localization key"
```

---

### Task 8: Fix CalendarTextEntry and UserDayNote FK Cascade → Restrict

**Files:**
- Modify: `Data/AppDbContext.cs:505-506,516-517`
- Modify: `Migrations/20260326223240_AddCalendarTextEntry.cs:35,47`
- Modify: `Migrations/AppDbContextModelSnapshot.cs` (auto-updated)

- [ ] **Step 1: Fix AppDbContext — UserDayNote**

Change lines 505-506 from:
```csharp
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
```
to:
```csharp
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 2: Fix AppDbContext — CalendarTextEntry**

Change lines 516-517 from:
```csharp
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
```
to:
```csharp
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 3: Fix uncommitted CalendarTextEntry migration**

In `Migrations/20260326223240_AddCalendarTextEntry.cs`, change line 35:
```csharp
// Before
onDelete: ReferentialAction.Cascade);
// After
onDelete: ReferentialAction.Restrict);
```

And line 47:
```csharp
// Before
onDelete: ReferentialAction.Cascade);
// After
onDelete: ReferentialAction.Restrict);
```

- [ ] **Step 4: Generate migration for UserDayNote FK change**

Run: `dotnet ef migrations add FixUserDayNoteCascadeToRestrict`

This captures the UserDayNote FK behavior change. The CalendarTextEntry change is captured in the already-edited uncommitted migration.

- [ ] **Step 5: Verify model snapshot is consistent**

Run: `dotnet ef migrations has-pending-model-changes`
Expected: No pending changes

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add Data/AppDbContext.cs Migrations/20260326223240_AddCalendarTextEntry.cs Migrations/*FixUserDayNoteCascadeToRestrict* Migrations/AppDbContextModelSnapshot.cs
git commit -m "fix: change CalendarTextEntry and UserDayNote FK from Cascade to Restrict"
```

---

### Task 9: Implement Weekly Stale Request Reaper Job

**Files:**
- Create: `Services/StaleRequestReaperJob.cs`
- Modify: `Program.cs` (DI registration)

- [ ] **Step 1: Create the reaper service**

Create `Services/StaleRequestReaperJob.cs`:

```csharp
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
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design
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
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design
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
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design
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
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants by design
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
```

- [ ] **Step 2: Register in Program.cs**

Find the section where other hosted services are registered (search for `AddHostedService`). Add:

```csharp
builder.Services.AddHostedService<StaleRequestReaperJob>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add Services/StaleRequestReaperJob.cs Program.cs
git commit -m "feat: add weekly reaper job for stale Pending requests (>30 days)"
```

---

### Task 10: Comprehensive Test Coverage (Priority 1 — Core Services)

> This is the large phased effort. Start with Priority 1 services (ShiftCalendarService, ChoreService, OnDutyService, RoleService). Each service gets its own sub-task. Priority 2-4 services should follow in subsequent work sessions.

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/ShiftCalendarServiceTests.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/ChoreServiceTests.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/OnDutyServiceTests.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/RoleServiceTests.cs`

- [ ] **Step 1: Read each service's public interface**

Before writing tests, read the interface files to understand each service's public API:
```bash
cat Services/IShiftCalendarService.cs
cat Services/IChoreService.cs
cat Services/IOnDutyService.cs
cat Services/IRoleService.cs
```

List every public method. Each method needs at minimum 1 happy-path test and 1 edge-case test.

- [ ] **Step 2: Study existing test patterns**

Read an existing well-structured test file for conventions:
```bash
cat ShiftManager.Tests/UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs | head -80
```

Note: in-memory SQLite setup, service construction, Moq usage, assertion patterns.

- [ ] **Step 3: Write ShiftCalendarServiceTests**

Create `ShiftManager.Tests/UnitTests/Services/ShiftCalendarServiceTests.cs` following the pattern from step 2. Cover at minimum:
- GetShiftsForDateRange (happy path + empty result)
- GetShiftsForMolecule (valid molecule + invalid molecule)
- Company-scoped vs molecule-scoped shift type visibility
- Shift instance creation and retrieval

- [ ] **Step 4: Run ShiftCalendarServiceTests**

Run: `cd ShiftManager.Tests && dotnet test --filter "ShiftCalendarService" --verbosity normal`
Expected: All new tests pass

- [ ] **Step 5: Write ChoreServiceTests**

Create `ShiftManager.Tests/UnitTests/Services/ChoreServiceTests.cs`. Cover at minimum:
- CreateChore (happy path + validation errors)
- GetChoresForDateRange (with/without canceled filter)
- CancelChore (valid + already canceled)
- RestoreChore (valid + not canceled)

- [ ] **Step 6: Run ChoreServiceTests**

Run: `cd ShiftManager.Tests && dotnet test --filter "ChoreService" --verbosity normal`
Expected: All new tests pass

- [ ] **Step 7: Write OnDutyServiceTests (CRUD)**

Create `ShiftManager.Tests/UnitTests/Services/OnDutyServiceTests.cs`. Cover at minimum:
- CreateOnDuty (happy path + duplicate prevention)
- CancelOnDuty (valid + already canceled)
- GetOnDutyForDateRange (with/without canceled filter)
- Cross-tenant visibility (OnDuty is a global table)

- [ ] **Step 8: Run OnDutyServiceTests**

Run: `cd ShiftManager.Tests && dotnet test --filter "OnDutyService" --verbosity normal`
Expected: All new tests pass

- [ ] **Step 9: Write RoleServiceTests**

Create `ShiftManager.Tests/UnitTests/Services/RoleServiceTests.cs`. Cover at minimum:
- AssignRoleTemplate (happy path + invalid template)
- RevokeRoleTemplate (valid + not assigned)
- Auto-grant propagation on assignment
- Auto-grant cleanup on revocation

- [ ] **Step 10: Run RoleServiceTests**

Run: `cd ShiftManager.Tests && dotnet test --filter "RoleService" --verbosity normal`
Expected: All new tests pass

- [ ] **Step 11: Run full test suite**

Run: `cd ShiftManager.Tests && dotnet test --verbosity normal`
Expected: ALL tests pass (including existing 363 + new tests, 0 failures)

- [ ] **Step 12: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/ShiftCalendarServiceTests.cs ShiftManager.Tests/UnitTests/Services/ChoreServiceTests.cs ShiftManager.Tests/UnitTests/Services/OnDutyServiceTests.cs ShiftManager.Tests/UnitTests/Services/RoleServiceTests.cs
git commit -m "test: add comprehensive tests for ShiftCalendar, Chore, OnDuty, and Role services"
```

---

## Verification Checklist

After all tasks are complete:

- [ ] `dotnet build` — 0 errors, 0 warnings
- [ ] `dotnet test` — ALL tests pass (0 failures)
- [ ] Run app: `dotnet run --urls http://localhost:5000`
- [ ] Login as Owner → `/Admin/Organization/Hierarchy` — edit/delete/add buttons visible
- [ ] `/Api/TechShift/Eligible` → 404 (deleted)
- [ ] Navigate to Calendar/Shifts with empty molecule → shows translated empty message
- [ ] Check console for SystemAlerts warnings (if disk is low, WAL check etc. should log)
- [ ] `/Diagnostic` → loads for Owner (grant-based auth)
