# Release Audit Fixes — Design Spec

**Date**: 2026-03-27
**Source**: Forensic release-readiness audit with multi-team agent system
**Scope**: 9 fixes triaged from 18 findings (6 trivial, 1 small, 1 medium, 1 large)
**Reviewed by**: code-reviewer agent (corrections applied)

---

## Fix 1: Phantom Grant Keys (3 endpoints + ViewComponent) + Delete Deprecated Endpoint

### Problem
Three API endpoints reference grant keys that don't exist in `GrantTypeSeed.cs`, making them completely inaccessible. A fourth endpoint (`TechShift/Eligible`) is deprecated dead code with a phantom grant — it should be deleted.

### Changes — Hierarchy API endpoints

| File | Line | Current | New |
|------|------|---------|-----|
| `Pages/Api/Hierarchy/Rename.cshtml.cs` | 18 | `Grant:EditHierarchy` | `Grant:ManageHierarchy` |
| `Pages/Api/Hierarchy/Create.cshtml.cs` | 21 | `Grant:CreateHierarchy` | `Grant:ManageHierarchy` |
| `Pages/Api/Hierarchy/Delete.cshtml.cs` | 18 | `Grant:DeleteHierarchy` | `Grant:ManageHierarchy` |

### Changes — HierarchyTreeViewComponent

| File | Line | Current | New |
|------|------|---------|-----|
| `ViewComponents/HierarchyTreeViewComponent.cs` | 61 | `"EditHierarchy"` | `"ManageHierarchy"` |
| `ViewComponents/HierarchyTreeViewComponent.cs` | 63 | `"DeleteHierarchy"` | `"ManageHierarchy"` |
| `ViewComponents/HierarchyTreeViewComponent.cs` | 64 | `"CreateHierarchy"` | `"ManageHierarchy"` |

**Note**: Line 62 (`"ReorderHierarchy"`) is a VALID grant (ID 123) — do NOT change it.

### Changes — Delete deprecated endpoint

Delete both files:
- `Pages/Api/TechShift/Eligible.cshtml`
- `Pages/Api/TechShift/Eligible.cshtml.cs`

**Rationale**: This endpoint is deprecated — returns `{ users: [], deprecated: true }`. Tech shift eligibility is now handled by `ShiftType.EligibleCompanyIds` + `RequiresOfficerRank` enforced in `ShiftAssignmentService.ValidateShiftAssignmentAsync` (lines 273-292). No code calls this endpoint (verified by codebase-wide search).

### Changes — Update stale SECURITY-AUDITED comments

The SECURITY-AUDITED comments in `Rename.cshtml.cs:16`, `Create.cshtml.cs:19-20`, and `Delete.cshtml.cs:16` reference the old phantom grant names. Update them to reference `ManageHierarchy`.

### Trade-off acknowledged
Collapsing `EditHierarchy`, `CreateHierarchy`, and `DeleteHierarchy` into a single `ManageHierarchy` grant loses granular permission control (e.g., rename-but-not-delete). This is acceptable because the phantom grants never worked — no user has ever had separate edit/create/delete hierarchy permissions.

### Verification
- Login as Owner → `/Admin/Organization/Hierarchy` — edit/delete/add buttons should now appear
- Call `/Api/Hierarchy/Create` with valid payload — should succeed (not 403)
- `/Api/TechShift/Eligible` — should return 404 (deleted)

---

## ~~Fix 2: Add SECURITY-AUDITED Annotation~~ — REMOVED

**Reason**: Code review verified that `CalendarTextEntryService.cs` already has SECURITY-AUDITED annotations at class level (lines 7-13) and method level (lines 29, 59, 84, 106). The original audit finding was incorrect — this service is properly annotated.

---

## Fix 3: Comprehensive Test Coverage for Untested Services

### Problem
Core operational services have zero test coverage. Test suite is 362/363 passing but critical scheduling services are untested.

### Services to Test

**Priority 1 — Core scheduling (highest risk):**
- `ShiftCalendarService` — shift calendar rendering and queries
- `ChoreService` — chore CRUD operations
- `OnDutyService` — on-duty assignment CRUD (eligibility already tested)
- `RoleService` — role template operations and grant propagation

**Priority 2 — Supporting services:**
- `ShiftGroupingService` — shift group management
- `ShiftProgramService` — shift program templates
- `ProfileService` — user profile management
- `ApiKeyService` — API key lifecycle

**Priority 3 — New features and utilities:**
- `WidgetService` — widget rendering
- `StoreService` — store management
- `CalendarTextEntryService` — text entries
- `QuickInfoConfigService` — widget config
- `HomeTypeService` — home type management
- `MasterProgramService` — master schedule templates
- `ScheduleExportService` — PDF/Excel/CSV generation

**Priority 4 — Infrastructure:**
- `MailService` — email delivery
- `PurgeService` — data purge
- `UserDataExportService` — GDPR export
- `GriffinService` — SSO integration
- `TraineeService` — trainee role management

### Test Pattern
Follow existing test conventions in `ShiftManager.Tests/`:
- xUnit with `IClassFixture` / `IAsyncLifetime`
- In-memory SQLite `AppDbContext` for service tests
- Moq for external dependencies
- Async test methods
- Test file naming: `{ServiceName}Tests.cs`
- Location: `ShiftManager.Tests/UnitTests/Services/`

### Minimum test cases per service
Each service should have at minimum:
- Happy path for each public method
- Validation / edge cases (null inputs, invalid IDs)
- Authorization-relevant scenarios (e.g., scope filtering)
- State transition correctness (for stateful entities)

### Verification
- `dotnet test` passes with 0 failures
- Each new test file covers at least the public methods of its service

---

## Fix 4: CalendarTextEntry AND UserDayNote FK Cascade → Restrict

### Problem
Both `CalendarTextEntry` and `UserDayNote` use `DeleteBehavior.Cascade` on CompanyId and UserId FKs, inconsistent with the project-wide `Restrict` convention for tenant-scoped entities.

### Changes

**File: `Data/AppDbContext.cs`** — CalendarTextEntry (lines 516-517):
```csharp
// Before
entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
// After
entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
```

**File: `Data/AppDbContext.cs`** — UserDayNote (lines 505-506):
```csharp
// Same change: Cascade → Restrict on both UserId and CompanyId FKs
```

**Migration**: Since the CalendarTextEntry migration (`20260326223240_AddCalendarTextEntry.cs`) is uncommitted, edit it directly:
- Line 35: `onDelete: ReferentialAction.Cascade` → `onDelete: ReferentialAction.Restrict`
- Line 47: `onDelete: ReferentialAction.Cascade` → `onDelete: ReferentialAction.Restrict`

For UserDayNote, generate a new migration to capture the FK behavior change.

Update `Migrations/AppDbContextModelSnapshot.cs` accordingly.

### Verification
- `dotnet ef migrations has-pending-model-changes` reports no issues
- `dotnet build` succeeds
- App boots and CalendarTextEntry + UserDayNote operations work

---

## Fix 5: Add Logging to SystemAlertsViewComponent Catch Blocks

### Problem
6 empty `catch { }` blocks in `ViewComponents/SystemAlertsViewComponent.cs` silently swallow health check failures.

### Changes

**Step 1**: Add `ILogger<SystemAlertsViewComponent>` to the constructor. It does NOT currently exist — must be added:
```csharp
private readonly ILogger<SystemAlertsViewComponent> _logger;

public SystemAlertsViewComponent(
    IMemoryCache cache,
    IConfiguration configuration,
    IWebHostEnvironment env,
    IGrantService grantService,
    IStringLocalizer<SharedResources> localizer,
    ILogger<SystemAlertsViewComponent> logger)  // ADD THIS
{
    // ... existing assignments ...
    _logger = logger;
}
```

**Step 2**: For each of the 6 catch blocks at lines 71, 83, 106, 115, 133, 152:
```csharp
// Before
catch { /* Ignore WAL check errors */ }

// After
catch (Exception ex)
{
    _logger.LogWarning(ex, "SystemAlerts: Failed to check {CheckName}");
}
```

Where `{CheckName}` matches the comment: "WAL size", "disk space", "backup status", "data protection", "font availability", "notification stats".

### Verification
- App boots, navigate to any page — warning banner still renders correctly
- If a check fails, log shows a Warning entry

---

## Fix 6: Update Stale Grant Count Test Assertion

### Problem
`GrantTypeSeed_Creates_ExpectedNumberOfGrants` asserts 125 but actual count is 127 (after `ManageStores` and other additions).

### Changes

**File: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs:892`**

```csharp
// Before
.HaveCount(125)
// After
.HaveCount(127)
```

Keep the hard-coded count — it's a deliberate regression guard. A dynamic count (`GrantTypeSeed.GetGrantTypes().Count()`) would defeat the purpose of detecting unintentional additions/removals.

**Note**: Verify the exact count at implementation time by running `GrantTypeSeed.GetGrantTypes().Count()`.

### Verification
- `dotnet test` — all tests pass (0 failures)

---

## Fix 7: Fix Health Endpoint URL in offline-handler.js

### Problem
`offline-handler.js:504` references `/api/health` but the actual endpoint is `/health`.

### Changes

**File: `wwwroot/js/offline-handler.js:504`**
```javascript
// Before
fetch('/api/health', ...)
// After
fetch('/health', ...)
```

### Verification
- Recovery ping hits `/health` and gets 200 (or 503 for low disk), not 404/401

---

## Fix 8: Switch Diagnostic Page to Grant-Based Auth

### Problem
`Diagnostic.cshtml.cs` uses legacy role-based auth that bypasses the grant system.

### Changes

**File: `Pages/Diagnostic.cshtml.cs:10`**
```csharp
// Before
[Authorize(Roles = nameof(ShiftManager.Models.Support.UserRole.Owner))]
// After
[Authorize(Policy = "Grant:AdminAccess")]
```

Also update the SECURITY-AUDITED comment at line 9 which references the old "restricted from AdminAccess to Owner" wording.

**Note**: `AdminAccess` grant (ID 57, Project scope) is only assigned to Owner role template in `RoleTemplateSeed` — this does not broaden access.

### Verification
- Login as Owner → `/Diagnostic` loads (200)
- Login as non-Owner → `/Diagnostic` returns 403 or redirect to AccessDenied

---

## Fix 9: Weekly Reaper Job for Stale Pending Requests

### Problem
TimeOffRequest, SwapRequest, UserJoinRequest, and ApiKeyRequest can accumulate in Pending state indefinitely.

### Design

**New file: `Services/StaleRequestReaperJob.cs`**

A `BackgroundService` that:
1. Runs once per week (configurable interval, default 7 days)
2. On each run, marks stale Pending records as expired/canceled:

| Entity | Filter Field | Age Check | New Status | Notes |
|--------|-------------|-----------|------------|-------|
| `TimeOffRequest` | `CreatedAt` | > 30 days | `RequestStatus.Canceled` | No `CanceledAt` field on this entity |
| `SwapRequest` | `CreatedAt` | > 30 days | `RequestStatus.Canceled` | No `CanceledAt` field on this entity |
| `UserJoinRequest` | `CreatedAt` | > 30 days | `JoinRequestStatus.Rejected` | |
| `ApiKeyRequest` | `RequestedAt` (NOT CreatedAt) | > 30 days | `ApiKeyRequestStatus.Expired` | Has purpose-built `Expired = 3` enum value |

3. Uses `IgnoreQueryFilters()` to process across all companies
   ```csharp
   // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — reaper processes all tenants
   // by design. Only modifies Status field on records matching Pending + age criteria.
   ```
4. Wraps each entity type in its own try/catch so one failure doesn't block others
5. Logs count of reaped records per entity type
6. Uses `IServiceScopeFactory` to create scoped `AppDbContext` (background services don't have HTTP context for tenant resolution)

**Registration:** Add to DI in `Program.cs`:
```csharp
builder.Services.AddHostedService<StaleRequestReaperJob>();
```

**Configuration (optional):**
```json
"Reaper": {
  "IntervalDays": 7,
  "MaxAgeDays": 30
}
```

### Why cancel/reject/expire instead of delete?
- Preserves audit trail — records show they existed and were auto-expired
- Prevents confusion if a user references a request that "disappeared"
- Uses semantically correct enum values per entity type

### Verification
- App boots, reaper registers as hosted service
- After 7 days (or force-trigger in dev), stale Pending records are cleaned
- Logs show reaper activity with per-entity counts
- Non-Pending records are untouched

---

## Fix 10: Add Missing Localization Key

### Problem
`Calendar_Empty_NoShiftsForSelection` is referenced in `Pages/Calendar/Shifts.cshtml:183` but missing from both .resx files.

### Changes

**File 1: `Resources/SharedResources.resx`**
```xml
<data name="Calendar_Empty_NoShiftsForSelection" xml:space="preserve">
  <value>No shifts found for this selection</value>
</data>
```

**File 2: `Resources/SharedResources.he-IL.resx`**
```xml
<data name="Calendar_Empty_NoShiftsForSelection" xml:space="preserve">
  <value>לא נמצאו משמרות עבור בחירה זו</value>
</data>
```

### Verification
- Navigate to Calendar/Shifts with a molecule that has no shifts
- English: "No shifts found for this selection"
- Hebrew: "לא נמצאו משמרות עבור בחירה זו"

---

## Implementation Order

1. **Trivial fixes first** (can be done in parallel, ~30 min total):
   - Fix 1: Phantom grant keys + delete deprecated endpoint
   - Fix 5: SystemAlerts logging (add ILogger + 6 log lines)
   - Fix 6: Grant count test (1 number)
   - Fix 7: Health URL (4 chars)
   - Fix 8: Diagnostic auth (1 line + comment)
   - Fix 10: Localization key (2 entries)

2. **Small fix** (~1 hour):
   - Fix 4: CalendarTextEntry + UserDayNote FK Cascade → Restrict

3. **Medium fix** (~2-3 hours):
   - Fix 9: Weekly reaper job

4. **Large fix** (days-weeks, can be phased):
   - Fix 3: Comprehensive test coverage — start with Priority 1 services
