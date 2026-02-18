# Feature Completion Design Document

**Date:** 2026-02-18
**Status:** In Progress — All 12 sections designed and Opus-reviewed. Ready for implementation.
**Goal:** Wire all 12 disconnected services, no-op handlers, and dead UI entry points into working end-to-end features before release.

**Context:** A comprehensive audit found 7 fully dead services (registered in DI, never injected), 2 no-op POST handlers, and 3 partially dead services. This document designs the integration for each, ordered by dependency.

**Tech Stack:** ASP.NET Core 8.0, EF Core + SQLite, Razor Pages, IMemoryCache, air-gapped Windows/IIS deployment

---

## Execution Order (Dependency-Ordered Verticals)

| # | Section | Services/Features | Dependencies |
|---|---------|-------------------|-------------|
| 1 | IFeatureFlagService | Unified feature flags, seed expansion, sync helper, consumer migration, UI rewrite | None |
| 2 | IShiftAssignmentService + IConcurrencyService | Validation refactor, Calendar/Table wiring, override tokens, batch validation | Section 1 (feature flags) |
| 3 | IFriendshipService | Nav link, calendar friend highlighting, API endpoint | Section 1 (feature flag gating) |
| 4 | IDutyRotationService | Admin CRUD page, Calendar integration | Sections 1-2 |
| 5 | IVacationApprovalService | Replace My/Requests logic, approval chains | Sections 1-2 |
| 6 | IWidgetService | Refactor OnCallWidgetViewComponent, friends-on-call | Sections 1, 3 |
| 7 | ITechShiftService | Wire into Calendar user picker, grant-based eligibility | Sections 1-2 |
| 8 | Hierarchy Reorder | **ALREADY IMPLEMENTED** — verify only | None |
| 9 | ISetupTaskService | Auto-generation hooks, nav link, task completion | Section 1 |
| 10 | IClientTelemetryService | Owner dashboard page | Section 1 |
| 11 | FeatureFlags UI | Rewrite /Owner/FeatureFlags to use IFeatureFlagService | Section 1 |
| 12 | Appsettings Hardening | Cleanup, bug fixes, deployment docs | Sections 1, 11 |

---

## Section 1: IFeatureFlagService (APPROVED)

### Problem
Two disconnected feature flag systems:
- **System 1 (DB-backed):** `FeatureFlagService` with full CRUD + 1-min IMemoryCache. Zero runtime callers.
- **System 2 (appsettings):** 12+ `Features:*` keys read by 9 API controllers, `_Layout.cshtml`, `CompanyIdInterceptor`, and more. Owner UI is a no-op.

### Design

#### 1A. Synchronous Cache-Only IsEnabled()
Add to `IFeatureFlagService`:
```csharp
bool IsEnabled(string flagName, int? userId = null, int? companyId = null);
```
Cache-only path — returns `false` if not in cache (never hits DB synchronously). This is safe for use in Razor views, interceptors, and synchronous code paths.

#### 1B. Startup Cache Warming
In `Program.cs`, after `app.Build()`:
```csharp
using (var scope = app.Services.CreateScope())
{
    var flagService = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
    await flagService.WarmCacheAsync();
}
```
`WarmCacheAsync()` loads all flags from DB into cache at startup. Combined with the sync `IsEnabled()`, this means flag checks never block on DB queries during request handling.

#### 1C. Seed Expansion
Expand `FeatureFlagSeed.cs` from 9 to ~40 flags covering:
- All operational flags (FF_ENFORCE_COMPANY_SCOPE, FF_ALLOW_PUBLIC_SIGNUP, FF_ENABLE_DAILY_NOTIFICATIONS, etc.)
- All API endpoint flags (FF_API_USERS_LIST, FF_API_SHIFTS_GET, etc.)
- All UI flags (FF_NEW_NAV_ENABLED, FF_WIDGETS_ENABLED, etc.)
- New feature flags (FF_FRIENDSHIPS_ENABLED, FF_DUTY_ROTATION_ENABLED, etc.)

Default values: `false` in seed (convention). Production values documented in `FeatureFlagDefaults` section of `appsettings.Production.json`.

#### 1D. CompanyIdInterceptor Exception
`CompanyIdInterceptor` is a Singleton that runs inside `SaveChanges`. Cannot inject Scoped `IFeatureFlagService`. **Keep on `IConfiguration`** for `Features:EnforceCompanyScope` only. All other consumers migrate to `IFeatureFlagService`.

#### 1E. Consumer Migration
Replace every `Configuration.GetValue<bool>("Features:...")` with `IFeatureFlagService.IsEnabled("FF_...")`:
- `_Layout.cshtml`: Use sync `IsEnabled()` (cache-only, safe in Razor)
- 9 API controllers: Use async `IsEnabledAsync()`
- `DailyNotificationJob`, `DutyRotationService`, `OnDutyService`, `Signup`, `SystemHealth`, `Program.cs` middleware: Use async

Total: ~66 `IConfiguration` reads across ~15 files → `IFeatureFlagService` calls.

#### 1F. Keep Features in appsettings
`appsettings.json` retains `Features:*` section as a documented fallback for air-gapped deployment scenarios where DB is unavailable. `IFeatureFlagService` is the authoritative source at runtime.

### Files Changed
| File | Change |
|------|--------|
| `Services/IFeatureFlagService.cs` | Add `IsEnabled()` sync, `WarmCacheAsync()` |
| `Services/FeatureFlagService.cs` | Implement sync cache-only path, warming |
| `Data/SeedData/FeatureFlagSeed.cs` | Expand from 9 to ~40 flags |
| `Program.cs` | Add startup cache warming call |
| `_Layout.cshtml` | Migrate 6 flag reads to sync `IsEnabled()` |
| 9 API controllers | Migrate to async `IsEnabledAsync()` |
| ~6 other services/pages | Migrate to async `IsEnabledAsync()` |

### Opus Review Resolution
- **CRITICAL: Singleton/Scoped conflict** → Keep interceptor on IConfiguration (1D)
- **CRITICAL: Sync DB query risks** → Cache-only sync path with startup warming (1A, 1B)
- **IMPORTANT: EnableDutyRotation is dead code** → Don't seed until a real consumer exists

---

## Section 2: IShiftAssignmentService + IConcurrencyService (APPROVED)

### Problem
Calendar/Table has 13+ POST handlers doing direct DB operations with zero business validation. `ShiftAssignmentService` implements 4 critical validations (job type match, shift grouping, weekly hours cap, rest hours) but is never called. `IConcurrencyService` wraps `DbUpdateConcurrencyException` but SQLite doesn't support row versioning.

### Design

#### 2A. Refactored Validation Model
Replace flat `ShiftAssignmentValidation` record with structured model:
```csharp
public enum ValidationSeverity { Error, Warning }
public enum ValidationCategory { JobType, ShiftGrouping, WeeklyHours, RestHours, Trainee, Concurrency }

public record ValidationIssue(
    string Key,              // e.g. "JOB_TYPE_MISMATCH"
    string Message,          // localized display message
    ValidationSeverity Severity,
    ValidationCategory Category);

public record ShiftAssignmentValidation(
    bool CanAssign,                              // true if no Errors
    IReadOnlyList<ValidationIssue> Errors,       // hard blocks
    IReadOnlyList<ValidationIssue> Warnings);    // overrideable with token
```

**Classification:**
- **Errors (hard block):** User doesn't exist, shift doesn't exist, user already assigned
- **Warnings (overrideable):** Job type mismatch, not in shift grouping, weekly hours cap exceeded, rest hours violation, trainee self-training

#### 2B. Secure Override Token
Replace `overrideWarnings: true` boolean (bypassable) with signed time-limited token:
1. Client calls `ValidateAssignment` → gets warnings + overrideToken
2. User sees warnings, clicks "Assign Anyway"
3. Client sends `AssignEmployee` + overrideToken
4. Server validates token (HMAC-SHA256, 5-minute expiry, scoped to exact params)

Token structure: `HMAC-SHA256(shiftId|userId|warningKeys|expiry, ApiKeyHmacSecret)`

New service methods:
```csharp
string GenerateOverrideToken(int shiftId, int userId, IReadOnlyList<string> warningKeys);
bool ValidateOverrideToken(string token, int shiftId, int userId);
```

#### 2C. Batch Validation for FillRange
```csharp
Task<Dictionary<(int shiftId, int userId), ShiftAssignmentValidation>>
    ValidateBatchAsync(IEnumerable<(int shiftId, int userId)> assignments, int moleculeId);
```
Pre-loads all shifts, users, and existing assignments in 3-4 queries instead of ~600 individual lookups per FillRange.

#### 2D. Configurable Rest Hours
Replace hardcoded `11` in `ShiftAssignmentService` with `effectiveSettings.RestHours` from molecule/company settings chain via existing `EffectiveSettingsService`.

#### 2E. Trainee Validation
- `TraineeUserId` must exist and belong to same company
- Prevent self-training: `TraineeUserId != UserId`
- Trainee must have role `Trainee` (UserRole.Trainee = 4)

#### 2F. Audit Logging for Overrides
Use existing `AuditLog` entity + direct `AppDbContext` writes (same pattern as other POST handlers). No separate `IAuditLogService` needed.

#### 2G. IConcurrencyService
Keep as documented backup per user decision. Add XML doc comment explaining SQLite limitation. Use opportunistically in batch operations (FillRange) where concurrent edits are most likely.

#### 2H. Calendar/Table Wiring
All 13+ POST handlers updated:
- **OnPostAssignEmployeeAsync:** Validate → return result (with override token if warnings) → proceed if valid token
- **OnPostChangeUserAsync:** Same flow (currently has zero checks — most dangerous handler)
- **OnPostFillRangeAsync:** Batch validate → collect warnings → return summary → proceed with valid token
- **OnPostUnassignEmployeeAsync:** No validation needed
- **Other handlers:** Appropriate validation subsets

UI: Warning modal extends existing Calendar/Table modal pattern.

### Files Changed
| File | Change |
|------|--------|
| `Services/IShiftAssignmentService.cs` | Refactored validation model, override token methods, batch validation |
| `Services/ShiftAssignmentService.cs` | Implement all changes, configurable rest hours |
| `Pages/Calendar/Table.cshtml.cs` | Wire all POST handlers through validation |
| `Pages/Calendar/Table.cshtml` | Warning modal UI |
| `wwwroot/js/calendar-*.js` | Override token flow in assignment JS |

### Opus Review Resolution
- **CRITICAL: No Warnings/Errors distinction** → Refactored model (2A)
- **CRITICAL: Boolean override bypass** → Signed tokens (2B)
- **IMPORTANT: Batch validation** → New method for FillRange (2C)
- **IMPORTANT: IAuditLogService doesn't exist** → Direct AuditLog writes (2F)
- **IMPORTANT: Hardcoded rest hours** → Configurable via EffectiveSettingsService (2D)
- **IMPORTANT: Trainee validation** → Added checks (2E)

---

## Section 3: IFriendshipService (APPROVED)

### Problem
Service is fully implemented (10 methods, 14 tests, complete `/Friends` page). But: no nav link (page is undiscoverable), `GetFriendIdsAsync()` exists but no calendar uses it.

### Design

#### 3A. Nav Link
Add to `_Layout.cshtml`, gated by `Configuration.GetValue<bool>("Features:FriendshipsEnabled", false)` (consistent with current layout pattern — IFeatureFlagService migration happens in Section 1):

**Admin nav** — before Management section divider:
```html
@if (friendshipsEnabled) {
<a href="/Friends" class="app-sidebar-nav-item @(currentPath.StartsWith("/Friends") ? "active" : "")">
    <span class="app-sidebar-nav-icon">🤝</span>
    <span><loc key="MyFriends" /></span>
</a>
}
```

**Employee nav** — after My Team, before divider. Same markup.

No `<require-grant>` wrapper — intentional. Friendships are available to all authenticated users.

Add `"FriendshipsEnabled": true` to `Features` in `appsettings.json`.

#### 3B. Calendar Filter (AJAX approach)
**New API endpoint:** `Pages/Api/Friends/Ids.cshtml.cs` — returns `HashSet<int>` of friend IDs as JSON. Called only when user activates the "Show Friends" toggle (zero impact on initial page load).

**ExcelCalendarAssignment model change:** Add `public int? UserId { get; set; }`. Render `data-user-id="@assignment.UserId"` on assignment elements in `_CalendarRow.cshtml`.

**Toggle button** on all 3 calendar toolbars (Shifts, OnCall, Chores):
```html
<button id="friendsToggle" class="btn btn-ghost btn-sm" onclick="toggleFriendsHighlight()" style="display:none;">
    <span>🤝</span> <loc key="ShowFriends" />
</button>
```

**JS logic** (~30 lines in `friends-highlight.js`): On toggle, fetch `/Api/Friends/Ids` (cached after first call), then toggle `.is-friend` class on matching `[data-user-id]` elements.

**Matching strategies by calendar mode:**
- **User rows** (Chores, Shifts user-mode): Match via `data-row-id="user-{userId}"` → highlight full row
- **Entity rows** (OnCall, Shifts shift-mode): Match individual assignment spans via `data-user-id`

**CSS:** `box-shadow: inset 3px 0 0 var(--info)` — layers over existing borders without conflict.

#### 3C. Feature Flag Seed
Add `FF_FRIENDSHIPS_ENABLED` to `FeatureFlagSeed` (IsEnabled: false — convention). Add constant to `Flags` class.

### Files Changed
| File | Change |
|------|--------|
| `_Layout.cshtml` | Add friendshipsEnabled config read + nav link (both sections) |
| `appsettings.json` | Add `Features:FriendshipsEnabled: true` |
| `Pages/Api/Friends/Ids.cshtml.cs` + `.cshtml` | New — friend IDs API endpoint |
| `ViewComponents/ExcelCalendarTableViewComponent.cs` | Add UserId to ExcelCalendarAssignment |
| `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml` | Add data-user-id attribute |
| `Calendar/Shifts.cshtml`, `OnCall.cshtml`, `Chores.cshtml` | Add "Show Friends" toggle button |
| `wwwroot/js/friends-highlight.js` | New — toggle logic |
| `wwwroot/css/site.css` | Add .is-friend style |
| `FeatureFlagSeed.cs` | Add flag + constant |
| `ApiAuthenticationMiddleware.cs` | Register /Api/Friends/Ids |

### Opus Review Resolution
- **CRITICAL: Feature flag mechanism mismatch** → Use IConfiguration for now (consistent with layout), migrate in Section 1
- **CRITICAL: ExcelCalendarAssignment lacks UserId** → Add property + data-user-id attribute
- **IMPORTANT: DB roundtrip on every calendar load** → AJAX approach instead (only on toggle activation)
- **IMPORTANT: Different row structures per calendar** → Specified matching strategies per mode

---

## Section 4: IDutyRotationService (APPROVED)

### Problem
`DutyRotationService` implements full rotation management: CRUD for rotations, queue management (add/remove/reorder users), assignment generation (AssignNextAsync, GenerateAssignmentsAsync), preview, and logging. Registered in DI but never injected. No admin UI page exists. On-duty calendars assign manually — rotation automation is dead code.

### Design

#### 4A. Admin CRUD Page
New page at `Pages/Admin/DutyRotation/Index.cshtml` (styled like Blueprints/Programs/MasterPrograms pattern):
- **Authorization:** `[Authorize(Policy = "Grant:ManageOnDuty")]` (NOT ManagerHomeAccess — this is on-duty specific)
- **OnGet:** `GetAllRotationsAsync()` → table with Name, DutyType, Frequency, IsActive, QueueSize
- **OnPostCreate:** Create rotation with name, dutyType, frequency, includeWeekends, maxConsecutive
- **OnPostUpdate/Delete:** Standard CRUD
- **Queue management tab:** Drag-drop user reordering via `ReorderQueueAsync()`, add/remove users

#### 4B. Calendar Integration
Wire `GenerateAssignmentsAsync` into Calendar/OnCall page:
- Add "Auto-Generate" button on OnCall calendar toolbar (visible when rotation exists for area)
- Button triggers: select date range → call `PreviewRotationAsync` → show preview table → confirm → `GenerateAssignmentsAsync`
- Generated assignments create standard `OnDuty` entries (same entity used by manual assignment)

#### 4C. Tenant Isolation (Already Done)
`DutyRotation` already implements `IBelongsToCompany` (verified in `Models/DutyRotation.cs`). `DutyRotationEntry` (queue entries) inherit isolation through FK to `DutyRotation`.

**Note:** OnDuty queries use `IgnoreQueryFilters()` (cross-company by design), but DutyRotation is tenant-scoped (company's rotation config). These are correctly different scopes — rotations belong to companies, on-duty assignments are global.

#### 4D. Feature Flag + Nav Link
- `Features:EnableDutyRotation: true` already exists in appsettings.json (line 35)
- Nav link in admin sidebar under "Management" section, gated by `IFeatureFlagService.IsEnabled("FF_DUTY_ROTATION_ENABLED")` + `<require-grant key="ManageOnDuty">`
- Use `IFeatureFlagService` (consistent with existing layout migration), NOT `IConfiguration`

### Files Changed
| File | Change |
|------|--------|
| `Pages/Admin/DutyRotation/Index.cshtml` + `.cs` | New — Admin CRUD page |
| `Pages/Calendar/OnCall.cshtml` | Add auto-generate button |
| `_Layout.cshtml` | Nav link gated by IFeatureFlagService + grant |

### Opus Review Resolution
- **CRITICAL: Wrong authorization policy** → Use ManageOnDuty, not ManagerHomeAccess (4A)
- **CRITICAL: OnDuty global vs DutyRotation tenant-scoped** → Correctly different scopes (4C)
- **CRITICAL: DutyRotationEntry lacks tenant isolation** → Inherits through FK to DutyRotation (4C)
- **IMPORTANT: Nav link should use IFeatureFlagService** → Fixed, consistent with existing layout code (4D)

---

## Section 5: IVacationApprovalService (APPROVED)

### Problem
`VacationApprovalService` implements full approval pipeline: route resolution (GetApprovalRouteAsync), submission, approval/decline with notes, pending approvals for user, rule CRUD, cancel, orphaned rule detection, and pipeline status. Registered in DI but never injected. `My/Requests.OnPostTimeOffAsync` creates time-off requests via direct DB insert with no approval logic.

### Design

#### 5A. Wire into Request Creation
In `My/Requests.cshtml.cs`, `OnPostTimeOffAsync`:
1. Create `TimeOffRequest` with `Status = RequestStatus.Pending` (existing behavior)
2. Call `_vacationApprovalService.SubmitForApprovalAsync(requestId, userId)`
3. `SubmitForApprovalAsync` returns `(bool Success, string Message)`:
   - If auto-approved (no rules match): calls `ProcessApprovedTimeOffAsync` helper → sets Approved + removes overlapping shifts + sends notification
   - If routed: sets status to Pending, identifies approver, creates notification for approver

#### 5B. RequestStatus Enum Extension
Add `Canceled = 3` to `RequestStatus` enum to disambiguate self-cancellation from manager decline:
- `Declined` = manager rejected
- `Canceled` = employee self-canceled before approval

#### 5C. Self-Cancel Flow
Wire `CancelRequestAsync(requestId, userId)` into My/Requests page:
- Only allowed when `Status == Pending` and `request.UserId == userId`
- Sets `Status = RequestStatus.Canceled`
- Sends cancellation notification to pending approver (if any)

#### 5D. Admin Rule Management
New page at `Pages/Admin/Settings/ApprovalRules.cshtml`:
- **Authorization:** `[Authorize(Policy = "Grant:SystemConfiguration")]` (existing grant — `ManageCompanySettings` does not exist in GrantTypeSeed)
- List rules for company via `GetRulesForCompanyAsync(companyId)`
- CRUD operations via `CreateRuleAsync`, `UpdateRuleAsync`, `DeleteRuleAsync`
- Orphaned rule detection via `DetectOrphanedRulesAsync` — show warning badge

#### 5E. ProcessApprovedTimeOffAsync Helper
Extracted helper method handles post-approval logic in one transaction:
1. Set `Status = Approved`
2. Query overlapping shifts for the user in the date range
3. Unassign from those shifts (set `UserId = null`)
4. Send approval notification to requesting user
5. Send coverage alert to managers for affected shifts

#### 5F. Feature Flag
Gate the entire approval pipeline behind `Features:VacationApprovalEnabled`. When disabled, time-off requests work as before (direct creation, no approval routing).

### Files Changed
| File | Change |
|------|--------|
| `Pages/My/Requests.cshtml.cs` | Wire SubmitForApprovalAsync in OnPostTimeOffAsync, add Cancel handler |
| `Pages/My/Requests.cshtml` | Cancel button on pending requests |
| `Models/Support/RequestStatus.cs` (or Enums.cs) | Add `Canceled = 3` |
| `Services/VacationApprovalService.cs` | Update CancelRequestAsync to use new Canceled status (currently uses Declined as workaround) |
| `Pages/Admin/Settings/ApprovalRules.cshtml` + `.cs` | New — rule management |
| `_Layout.cshtml` | Nav link to ApprovalRules (under Settings) |
| `appsettings.json` | Add `Features:VacationApprovalEnabled` |

### Opus Review Resolution
- **CRITICAL: Missing creation path** → Wire into OnPostTimeOffAsync (5A)
- **CRITICAL: Self-cancel indistinguishable from decline** → New Canceled status (5B, 5C)
- **CRITICAL: Auto-approve skips shift removal/notifications** → ProcessApprovedTimeOffAsync helper (5E)
- **CRITICAL: Grant:ManageCompanySettings doesn't exist** → Use Grant:SystemConfiguration (5D)
- **CRITICAL: VacationApprovalService.CancelRequestAsync not updated** → Must use new Canceled enum value
- **IMPORTANT: Post-approval side effects location** → ProcessApprovedTimeOffAsync called from OnPostTimeOffAsync after SubmitForApprovalAsync returns auto-approved, AND from ApproveAsync for manual approvals

---

## Section 6: IWidgetService (APPROVED)

### Problem
`WidgetService` implements grant-based widget building with additive content merging: `BuildOnCallWidgetAsync` merges on-call contacts from all user's granted contexts, `GetCurrentHakamAsync` gets the current commander, company-specific contacts, and widget preferences. Registered in DI but never injected. `OnCallWidgetViewComponent` uses direct DB queries instead.

### Design

#### 6A. Refactor OnCallWidgetViewComponent
Replace direct DB queries in `OnCallWidgetViewComponent` with `IWidgetService` calls:
```csharp
// Before: direct DB queries (~40 lines of query logic)
// After:
var widgetData = await _widgetService.BuildOnCallWidgetAsync(userId);
```

#### 6B. IgnoreQueryFilters Fix
`WidgetService` methods must use `IgnoreQueryFilters()` for on-call data queries — on-call contacts are cross-company by design (same pattern as OnDuty pages). Add security audit comments.

#### 6C. NotMapped Property Fix
`WidgetService` queries `ShiftType.Name` (a `[NotMapped]` computed property) in 3 LINQ-to-SQL queries — these will throw EF Core translation exceptions at runtime. Replace with `ShiftType.CustomName` and `ShiftType.Key` pattern (matching the already-corrected `OnCallWidgetViewComponent` at lines 121-123: `st.CustomName.Contains("Hakam") || st.Key.Contains("Hakam")`).

#### 6D. ManagerHomeAccess Fallback
`OnCallWidgetViewComponent` currently shows for users with `ManagerHomeAccess` grant. `WidgetService.BuildOnCallWidgetAsync` must respect this — if user has `ManagerHomeAccess`, show all company on-call contacts. If only employee, show on-call contacts for their department/molecule scope.

#### 6E. Friends-On-Call Integration
Wire `GetFriendsOnCallAsync` (from IWidgetService) to show which friends are currently on-call. Depends on Section 3 (IFriendshipService) for `GetFriendIdsAsync`.

### Files Changed
| File | Change |
|------|--------|
| `ViewComponents/OnCallWidgetViewComponent.cs` | Replace DB queries with IWidgetService calls |
| `Services/WidgetService.cs` | Add IgnoreQueryFilters, fix NotMapped query, add fallback logic |

### Opus Review Resolution
- **CRITICAL: Missing IgnoreQueryFilters** → Added to all on-call queries (6B)
- **CRITICAL: Queries NotMapped ShiftType.Name** → Use CustomName/Key pattern matching OnCallWidgetViewComponent (6C)
- **CRITICAL: ManagerHomeAccess fallback missing** → Added scope-based rendering (6D)
- **IMPORTANT: GetFriendsOnCallAsync also lacks IgnoreQueryFilters** → Add to UserFriendships and ShiftAssignments queries in that method too (6B)

---

## Section 7: ITechShiftService (APPROVED)

### Problem
`TechShiftService` implements grant-based tech shift eligibility: `GetEligibleUsersForTechShiftAsync` queries which users have the corresponding grant (e.g., HANAVA→CanBeAssignedHanava), `IsUserEligibleForTechShiftAsync` checks a single user, `GetTechShiftTypesAsync` returns all known types. Registered in DI but never injected. Calendar/Table assigns users to tech shifts with zero eligibility checking.

### Design

#### 7A. AJAX-Based Eligibility Filtering
**Critical insight:** `TechShiftType` varies per shift slot (not per page). A single Calendar/Table page may show HANAVA, DELTA, and regular shifts together. Eligibility filtering must happen at **assignment time** (when user clicks a specific slot), not at page load.

**New API endpoint:** `Pages/Api/TechShift/Eligible.cshtml.cs`
- **GET** `/Api/TechShift/Eligible?type=HANAVA&companyId=5`
- Returns `List<{Id, DisplayName}>` — **must project to DTO, NOT return raw AppUser** (which exposes PasswordHash, PasswordSalt, Phone, etc.)
- Uses `ITechShiftService.GetEligibleUsersForTechShiftAsync(type, companyId)` with `.Select(u => new { u.Id, u.DisplayName })`
- **Authorization:** `[Authorize(Policy = "Grant:ManageShifts")]`
- Register in `ApiAuthenticationMiddleware.cs` `IsInternalWebUiEndpoint()` whitelist

#### 7B. Calendar/Table Assignment Flow
When user clicks to assign on a shift slot:
1. JS reads `data-tech-shift-type` attribute from the shift row (populated from `ShiftType.TechShiftType`)
2. If `TechShiftType` is non-null, fetch eligible users from `/Api/TechShift/Eligible`
3. Filter the employee dropdown to show only eligible users (gray out ineligible with tooltip)
4. If no eligible users, show informational message

#### 7C. ShiftAssignmentService Integration
Add `TechShift` to `ValidationCategory` enum (Section 2). In `ValidateAssignmentAsync`:
- If shift has `TechShiftType` set, call `IsUserEligibleForTechShiftAsync`
- Ineligible user → **Warning** (overrideable, not hard block — managers may need to override in emergencies)

#### 7D. Data Attributes
In `Calendar/Table.cshtml`, add `data-tech-shift-type="@shiftType.TechShiftType"` to shift row elements where `TechShiftType` is non-null.

### Files Changed
| File | Change |
|------|--------|
| `Pages/Api/TechShift/Eligible.cshtml` + `.cs` | New — eligibility API endpoint |
| `Pages/Calendar/Table.cshtml` | Add data-tech-shift-type attributes |
| `Pages/Calendar/Table.cshtml.cs` | Inject ITechShiftService |
| `wwwroot/js/calendar-inline-edit.js` | AJAX eligibility check before assignment |
| `Services/ShiftAssignmentService.cs` | Add TechShift validation category |
| `ApiAuthenticationMiddleware.cs` | Register /Api/TechShift/Eligible in IsInternalWebUiEndpoint whitelist |

### Opus Review Resolution
- **IMPORTANT: Raw AppUser exposure** → Project to DTO with Id + DisplayName only (7A)
- **IMPORTANT: ApiAuthenticationMiddleware registration** → Added to IsInternalWebUiEndpoint whitelist (7A)

---

## Section 8: Hierarchy Reorder (ALREADY IMPLEMENTED — VERIFY ONLY)

### Status: Complete
Investigation reveals this feature is **already fully implemented**:

1. **Models:** `Area.SortOrder`, `Molecule.SortOrder`, `Company.SortOrder`, `Department.SortOrder` — all exist as `int` properties
2. **API:** `Pages/Api/Hierarchy/Reorder.cshtml.cs` — full implementation with IgnoreQueryFilters, parent validation, audit logging, SaveChangesAsync
3. **Frontend:** `Pages/Shared/Components/HierarchyTree/Default.cshtml` line 655 — `saveReorder()` function calls `/Api/Hierarchy/Reorder` via fetch POST
4. **Display:** `Hierarchy/Index.cshtml.cs` orders by `.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)` for areas, molecules, companies
5. **Authorization:** `Grant:ReorderHierarchy` policy enforced

### Verification Checklist
- [ ] Drag-drop reorder works end-to-end in browser
- [ ] SortOrder persists after page refresh
- [ ] Department reordering works (verify Department SortOrder ordering in Hierarchy/Index)
- [ ] Audit log entry created on reorder

### Note
`docs/DESIGNED_NOT_IMPLEMENTED.md` lists this as a "stub" — this is stale documentation. The feature was fully implemented. Update DESIGNED_NOT_IMPLEMENTED.md to reflect completion.

---

## Section 9: ISetupTaskService (APPROVED)

### Problem
`SetupTaskService` generates onboarding task checklists when new molecules/companies are created (assign admins, setup shift groupings, assign directors/leads, setup blueprints, assign assigners). Service is fully implemented. Admin page at `/Admin/SetupTasks/Index` exists and is fully wired (view pending tasks, complete, skip, progress tracking). **Two gaps:**
1. `GenerateTasksForMoleculeAsync` and `GenerateTasksForCompanyAsync` are never called — tasks are never auto-generated
2. No nav link — page is undiscoverable

### Design

#### 9A. Hook into Entity Creation
**Molecule creation** — in `Pages/Admin/Organization/Molecules/Index.cshtml.cs`, `OnPostCreateAsync` method (NOT a separate Create page), after successful molecule creation:
```csharp
await _setupTaskService.GenerateTasksForMoleculeAsync(molecule.Id, currentUserId);
```

**Company creation** — in the HierarchyTree component's create handler (company creation happens through the hierarchy tree, not a dedicated page). Identify the exact POST handler that creates companies.
```csharp
await _setupTaskService.GenerateTasksForCompanyAsync(company.Id, currentUserId);
```

Guard against duplicate generation: check `GetTasksForMoleculeAsync(moleculeId)` for existing tasks before generating. The current `GenerateTasksForMoleculeAsync` does not guard against duplicates — must add this check during implementation.

#### 9B. Nav Link
Add to `_Layout.cshtml` admin sidebar, gated by `<require-grant key="SystemConfiguration">`:
```html
<a href="/Admin/SetupTasks" class="app-sidebar-nav-item">
    <span class="app-sidebar-nav-icon"><i data-lucide="list-checks"></i></span>
    <span><loc key="SetupTasks" /></span>
</a>
```
Place after "Settings" section in admin nav.

#### 9C. Feature Flag
Gate generation behind `Features:SetupTasksEnabled`. Page remains accessible (to view/manage existing tasks), but auto-generation only triggers when flag is enabled.

### Files Changed
| File | Change |
|------|--------|
| `Pages/Admin/Organization/Molecules/Index.cshtml.cs` | Hook GenerateTasksForMoleculeAsync in OnPostCreateAsync |
| HierarchyTree company creation handler | Hook GenerateTasksForCompanyAsync |
| `_Layout.cshtml` | Nav link with SystemConfiguration grant gate |
| `appsettings.json` | Add `Features:SetupTasksEnabled` |

---

## Section 10: IClientTelemetryService (APPROVED)

### Problem
Full telemetry collection pipeline is wired and working:
- **JS clients:** `telemetry.js`, `error-boundary.js` → post events/errors/metrics to `/Api/Telemetry`
- **API endpoint:** `Pages/Api/Telemetry.cshtml.cs` → rate-limited, field-truncated, PII-hashed
- **Service:** `ClientTelemetryService` → defensive logging, Web Vitals rating, PII scrubbing
- **DB:** `ClientAnalyticsEvents`, `ClientErrors`, `PerformanceMetrics` tables

**Gap:** Data is collected and stored but never surfaced. No dashboard exists to VIEW telemetry data. The existing `Admin/Analytics` page uses `IAnalyticsService` (shift/team analytics — completely different domain).

### Design

#### 10A. Owner Telemetry Dashboard
New page at `Pages/Owner/Telemetry.cshtml`:
- **Authorization:** `[Authorize(Policy = "Grant:SystemConfiguration")]`
- **Tabs:** Errors | Performance | Events | Cleanup

**Errors tab:**
- Recent errors table via `GetRecentErrorsAsync(50)`
- Error counts by type (bar chart) via `GetErrorCountsByTypeAsync`
- Date range filter

**Performance tab:**
- Web Vitals summary cards (LCP, FID, INP, CLS, TTFB) via `GetWebVitalsSummaryAsync`
- Color-coded: green/yellow/red based on good/needs-improvement/poor counts
- Per-page breakdown via `GetWebVitalsPercentilesByPageAsync`

**Events tab:**
- Event counts by type via `GetEventCountsByTypeAsync`
- Recent events table via `GetRecentEventsAsync(50)`

**Cleanup tab:**
- Button to trigger `CleanupOldDataAsync(retentionDays)` with configurable retention
- Shows last cleanup timestamp

#### 10B. Nav Link
Add to Owner section in `_Layout.cshtml`:
```html
<a href="/Owner/Telemetry" class="app-sidebar-nav-item">
    <span class="app-sidebar-nav-icon"><i data-lucide="activity"></i></span>
    <span><loc key="Telemetry" /></span>
</a>
```

#### 10C. Scheduled Cleanup
Add `CleanupOldDataAsync(30)` call to existing daily job infrastructure (same pattern as backup scheduling). Prevents unbounded DB growth in air-gapped environments.

### Files Changed
| File | Change |
|------|--------|
| `Pages/Owner/Telemetry.cshtml` + `.cs` | New — telemetry dashboard |
| `_Layout.cshtml` | Nav link in Owner section |

---

## Section 11: FeatureFlags UI (APPROVED)

### Problem
`Pages/Owner/FeatureFlags.cshtml.cs` is a **complete no-op**:
- `OnGet()` reads ~30 flag values from `IConfiguration` (the wrong system)
- `OnPost()` logs the submitted values but **never saves anything** (comments say "In a real implementation, you would...")
- Meanwhile, `IFeatureFlagService` has full CRUD: `GetAllFlagsAsync`, `SetFlagAsync`, `DeleteFlagAsync`, `InvalidateCache`

### Design

#### 11A. Rewrite Page Model
Replace `IConfiguration` injection with `IFeatureFlagService`:

**OnGet:**
```csharp
var allFlags = await _featureFlagService.GetAllFlagsAsync();
// Group by category prefix: "FF_API_*" → API, "FF_UI_*" → UI, etc.
FlagsByCategory = allFlags.GroupBy(f => GetCategory(f.Name)).ToDictionary(...);
```

**OnPost:**
```csharp
foreach (var flag in SubmittedFlags)
{
    await _featureFlagService.SetFlagAsync(flag.Name, flag.IsEnabled);
}
// Cache invalidated immediately by SetFlagAsync (per-flag + warm cache)
Message = "Feature flags saved successfully. Changes take effect immediately.";
```

#### 11B. Remove Restart Message
Current page shows "changes require restart" (`Info_FeatureFlagChangesRequireRestart` localization key) — this is wrong for DB-backed flags. `SetFlagAsync` calls `InvalidateCache()` immediately, which removes both the per-flag cache entry and the warm cache key. Changes are effectively instant for async callers and within one warm-cache refresh cycle for sync callers. Remove the dead `Info_FeatureFlagChangesRequireRestart` localization key from both `SharedResources.resx` and `SharedResources.he-IL.resx`.

#### 11C. Flag Categories UI
Group flags by prefix for organized display:
- **Core:** FF_ENFORCE_COMPANY_SCOPE, FF_ALLOW_PUBLIC_SIGNUP, FF_ENABLE_DIRECTOR_ROLE
- **API:** FF_API_USERS_LIST, FF_API_SHIFTS_GET, etc.
- **Features:** FF_FRIENDSHIPS_ENABLED, FF_DUTY_ROTATION_ENABLED, FF_VACATION_APPROVAL_ENABLED
- **UI:** FF_WIDGETS_ENABLED, FF_EXCEL_CALENDARS

Each category renders as a collapsible card with toggle switches.

### Files Changed
| File | Change |
|------|--------|
| `Pages/Owner/FeatureFlags.cshtml.cs` | Rewrite to use IFeatureFlagService |
| `Pages/Owner/FeatureFlags.cshtml` | Update UI for category-grouped toggles |

### Files Changed (additional)
| File | Change |
|------|--------|
| `Resources/SharedResources.resx` | Remove dead `Info_FeatureFlagChangesRequireRestart` key |
| `Resources/SharedResources.he-IL.resx` | Remove dead `Info_FeatureFlagChangesRequireRestart` key |

### Dependencies
Depends on Section 1 (seed expansion) for flag names to exist in DB.

### Opus Review Resolution
- **IMPORTANT: Dead localization key** → Remove `Info_FeatureFlagChangesRequireRestart` from both .resx files (11B)
- **IMPORTANT: Cache invalidation timing** → Changes are immediate (SetFlagAsync calls InvalidateCache), not "within 1 minute" (11B)

---

## Section 12: Appsettings Hardening (APPROVED)

### Problem
After Section 1 migrates all `Features:*` consumers to `IFeatureFlagService`:
1. ~50+ `IConfiguration.GetValue<bool>("Features:...")` calls become dead code
2. No startup validation for critical config values (app silently fails with wrong connection string)
3. `appsettings.json` has grown organically with no documentation for air-gapped deployment
4. Some config values have misleading defaults

### Design

#### 12A. Dead IConfiguration Cleanup
After Section 1 migration, remove all `IConfiguration.GetValue<bool>("Features:...")` calls except:
- `CompanyIdInterceptor` (Singleton — must stay on IConfiguration per Section 1D)

This is a **verification step** — the actual migration happens in Section 1E. Section 12 verifies no stale reads remain.

#### 12B. Startup Validation
Add to `Program.cs` after configuration loading:
```csharp
// Validate critical configuration
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrEmpty(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required");

var ownerEmail = builder.Configuration["Seeding:Owner:Email"];
if (string.IsNullOrEmpty(ownerEmail))
    throw new InvalidOperationException("Seeding:Owner:Email is required");
```

Fail-fast at startup instead of cryptic runtime errors.

#### 12C. Appsettings Documentation
JSON does not support inline comments. Use the existing `"_*Comment"` key pattern (already used at `"_SeedingComment"` line 112):
- `"_FeaturesComment": "Fallback values; authoritative source is IFeatureFlagService DB after Section 1 migration"`
- `"_GriffinComment": "Military SSO integration; set Enabled=true and configure URLs for production"`
- `"_EmailComment": "SMTP-compatible email API; disabled by default for air-gapped environments"`
- `"_BackupComment": "Automated SQLite backup schedule"`

#### 12D. Bug Fixes
- `Features:ExcelCalendars` vs `Features:ExcelCalendarShifts` — `_Layout.cshtml` reads both independently but they control overlapping functionality. Consolidate: `ExcelCalendars` is the master toggle, individual calendar flags only matter when master is `true`.
- `Features:EnableDutyRotation` is read by `DutyRotationService` internally but the service is never called. After Section 4 wires the service, this flag becomes meaningful.

### Files Changed
| File | Change |
|------|--------|
| `Program.cs` | Startup validation for critical config |
| `appsettings.json` | Add `_*Comment` documentation keys, fix misleading defaults |
| Verify all files from Section 1E | Confirm no stale IConfiguration reads remain |

### Opus Review Resolution
- **IMPORTANT: JSON doesn't support inline comments** → Use existing `_*Comment` key pattern (12C)
- **IMPORTANT: ExcelCalendars redirect middleware inconsistency** → Redirect middleware in Program.cs uses IConfiguration while _Layout uses IFeatureFlagService; both should use same source after Section 1 migration (12D)
