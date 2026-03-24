# Tech Molecule Convergence: Shikma Departments → Companies

**Date:** 2026-03-14
**Status:** Design approved (rev 3 — all review issues resolved)
**Scope:** Convert Shikma from Department-based Tech molecule to Company-based with shift eligibility rules

---

## Problem Statement

Shikma (שקמה) is the only Tech molecule. It currently uses Departments instead of Companies and has no JobType assignment for users. This causes:

1. **Calendar/Shifts shows nothing** for Tech users — the page requires `JobTypeId` which Tech users don't have
2. **Users don't appear in assignment dropdowns** — `GetUsersForCalendarAsync` filters by `JobTypeId`
3. **Departments can't participate in the shift system** — ShiftType, ShiftAssignment, grants, and tenant isolation are all Company-scoped

The root cause: Departments are a lightweight organizational tag that exists outside the operational data flow. The shift system, calendar, grants, and tenant isolation are all built around Companies.

## Decision

Convert Shikma's Departments to Companies. Keep `MoleculeType.Tech` as a behavioral flag that drives:
- Single calendar view (no JobType filter)
- Company+Rank-based shift eligibility (instead of JobType-based)
- No job-type-scoped roles (Lead, Director)

Shift eligibility rules are explicit, data-driven fields on `ShiftType`, **replacing** the existing grant-based `ITechShiftService`.

---

## Design

### 1. Data Model Changes

#### 1a. Departments → Companies

The 6 Shikma Departments become real Company records under the Shikma molecule:

| Current Department | New Company | DisplayName |
|---|---|---|
| Yekev | Company | יקב |
| Snir | Company | שניר |
| Arbel | Company | ארבל |
| Pie | Company | פאי |
| Samapkamia | Company | סמפקמיה |
| Tao | Company | טאו |

The existing HQ company (`hq-shikma`) remains for molecule-level admin users (MoleculeAdmin, AreaAdmin).

#### 1b. Job Types for Shikma

Two new molecule-scoped job types (organizational only, no shift eligibility implications):

- `ProjectManager` — DisplayName: "מנהל פרוייקט", `MoleculeId = shikma.Id`
- `Hakam` — DisplayName: "חק\"ם", `MoleculeId = shikma.Id`

These use the area resolved via `molecule.AreaId` (area Name is "190" but the ID is auto-generated — always look up by name, not hardcode an ID).

#### 1c. Shift Eligibility Rules on ShiftType

New fields on `ShiftType`:

```csharp
/// <summary>
/// JSON array of CompanyIds whose users can be assigned this shift type.
/// Null = all companies in the molecule are eligible (default workforce behavior).
/// </summary>
public string? EligibleCompanyIds { get; set; }

/// <summary>
/// If true, only users with officer rank (>= SegenMishne) can be assigned.
/// Follows existing pattern from OnDutyTypeConfig.RequiresOfficerRank.
/// </summary>
public bool RequiresOfficerRank { get; set; } = false;
```

Eligibility rules for Shikma's shift types:

| Shift Type | Key | EligibleCompanyIds | RequiresOfficerRank |
|---|---|---|---|
| הנבה (Hanava) | HANAVA | [Tao, Pie, Samapkamia] | false |
| דלתא (Delta) | DELTA | [Tao, Pie, Samapkamia] | false |
| יקב (Yekev) | YEKEV | [Yekev] | false |
| מובילט (Movilat) | MOVILTECH | null (all companies) | true |

Workforce shift types keep defaults (`EligibleCompanyIds = null`, `RequiresOfficerRank = false`) — zero behavioral change.

**Note on Snir and Arbel:** These companies do not participate in regular shifts. Their users only perform area-wide Hakam on-call duties (already in the on-call system). They can still **view** all shifts in the calendar.

#### 1d. ShiftCapacityOverride.JobTypeId → nullable

`Models/ShiftCapacityOverride.cs` currently has `public int JobTypeId { get; set; }` (non-nullable). Tech shift types have `JobTypeId = null`, so the capacity system cannot function for them.

**Change:** Make `JobTypeId` nullable:
```csharp
public int? JobTypeId { get; set; }
```

Also update the `JobType` navigation property to nullable: `public JobType? JobType { get; set; }`.

All capacity-related methods in `IShiftCalendarService` that take `int jobTypeId` must accept `int? jobTypeId` (see Section 2e).

#### 1e. Tech ShiftType CompanyId — Use HQ Company

`ShiftType` implements `IBelongsToCompany`, so every ShiftType record needs a `CompanyId` for EF query filters. Tech shift types should use the **HQ company** (`hq-shikma`) as their `CompanyId`.

This is safe because Calendar pages already use `IgnoreQueryFilters()` when loading shift types (scoped by MoleculeId instead). However, any codepath that loads ShiftTypes WITHOUT `IgnoreQueryFilters()` will only see shift types belonging to the user's own company — this is correct for Workforce but would hide Tech shift types from non-HQ users.

**Audit required during implementation:** Verify that all codepaths loading Tech shift types use `IgnoreQueryFilters()` or molecule-scoped queries.

### 2. Calendar/Shifts Page Changes

#### 2a. Single Calendar View for Tech Molecules

When `SelectedMolecule.Type == MoleculeType.Tech`:

- **Hide the JobType dropdown** — irrelevant for Tech molecules
- **Query shift types by `MoleculeId` only** (no `JobTypeId` filter)
- **Show all shift types** in one grid: Hanava, Delta, Yekev, Movilat
- **All users in the molecule can VIEW** the calendar (visibility is molecule-wide)

`BuildShiftBasedCalendarAsync` and `BuildUserBasedCalendarAsync` get a Tech-aware path:
```
if Tech molecule:
    ShiftTypes WHERE MoleculeId = X (no JobTypeId filter)
    Users WHERE Company.MoleculeId = X (no JobTypeId filter, but filtered by ShiftType eligibility for assignment)
else:
    existing behavior (MoleculeId + JobTypeId)
```

#### 2b. Guard Conditions That Need Updating

The following guards currently block rendering when `JobTypeId` is null. For Tech molecules, these must allow rendering with `JobTypeId = null`:

1. **`Shifts.cshtml.cs:177-187`** — `if (MoleculeId.HasValue && JobTypeId.HasValue)` guards both calendar builders
2. **`Shifts.cshtml.cs:190-193`** — `if (MoleculeId.HasValue && JobTypeId.HasValue)` guards user list loading
3. **`Table.cshtml.cs:90`** — `public bool IsMoleculeMode => MoleculeId.HasValue && JobTypeId.HasValue;` — Tech molecules need molecule mode even without JobTypeId

**Fix pattern:** For Tech molecules, check `MoleculeId.HasValue && (JobTypeId.HasValue || IsTechMolecule)` or refactor the guard to branch on molecule type.

#### 2c. Assignment Dropdown — Eligibility Filtering

When assigning a user to a shift, the dropdown shows only **eligible** users based on the shift type's rules:

- Parse `ShiftType.EligibleCompanyIds` → filter users to those companies
- Check `ShiftType.RequiresOfficerRank` → filter to `user.Rank.IsOfficer()`
- For Workforce shift types (both fields null/default), no change

**New service method:** `GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId)` in `IShiftCalendarService` — returns users filtered by the shift type's eligibility rules. Used by the bottom sheet for Tech molecules. For Workforce, the existing `GetUsersForCalendarAsync(moleculeId, jobTypeId)` continues to work.

#### 2d. GetShiftsData API

`Pages/Api/Calendar/GetShiftsData.cshtml.cs` line 66: `if (moleculeId <= 0 || jobTypeId <= 0)` returns 400. For Tech molecules, `jobTypeId` will be 0 or absent.

**Fix:** Accept `jobTypeId = 0` for Tech molecules:
```csharp
if (moleculeId <= 0)
    return BadRequest(...);

// For Tech molecules, jobTypeId may be 0 (all shift types shown together)
if (jobTypeId <= 0)
{
    var molecule = await _db.Molecules.FindAsync(moleculeId);
    if (molecule?.Type != MoleculeType.Tech)
        return BadRequest(...);
}
```

The downstream query logic also needs the same Tech-aware branching (query by MoleculeId only when Tech).

#### 2e. IShiftCalendarService Interface Changes

All methods currently take `int jobTypeId` (non-nullable). For Tech support:

```csharp
// Change signatures to accept nullable jobTypeId:
Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int? jobTypeId);
Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);
Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);
Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date);
Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date, int capacity, int userId);
Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date);
Task<Dictionary<(int ShiftTypeId, DateOnly Date), int>> GetCapacitiesBatchAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);

// New method for Tech shift eligibility:
Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId);
```

When `jobTypeId` is null, implementations query by `MoleculeId` only (no JobTypeId filter). All callers updated accordingly.

#### 2f. SignalR Group Pattern

Current pattern: `shifts-{moleculeId}-{jobTypeId}` — breaks when `JobTypeId` is null.

**Fix:** For Tech molecules, use `shifts-{moleculeId}-0` as a sentinel:
```csharp
// CalendarHub.cs CalendarGroups
public static string Shifts(int moleculeId, int? jobTypeId)
    => $"shifts-{moleculeId}-{jobTypeId ?? 0}";
```

Hub validation (`CalendarHub.cs:148-150`) updated to accept `0` as a valid jobTypeId segment for Tech molecules.

Client-side `calendar-realtime.js:73` updated:
```javascript
return `shifts-${scope.moleculeId}-${scope.jobTypeId ?? 0}`;
```

All 10 notification callsites in `Table.cshtml.cs` that guard on `shiftType?.JobTypeId != null` must be updated to also fire for Tech shift types (where `JobTypeId` is null but `MoleculeId` is not):
```csharp
if (shiftType?.MoleculeId != null)
{
    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
    // ... send notification
}
```

### 3. Signup Flow Changes

#### 3a. Form Behavior

Remove the Tech-specific JS branch that hides JobType and shows Departments:
- Tech molecules now load Companies (same as Workforce) via `OnGetCompaniesAsync`
- JobType dropdown shows Shikma-specific options (ProjectManager, Hakam) via `OnGetJobTypesAsync` — molecule-scoped job types are already filtered by `GetJobTypesForMoleculeAsync`
- The DepartmentId hidden field is no longer populated for new signups

#### 3b. Server-Side Handler

Remove the Tech-specific block in `Signup.cshtml.cs` (lines 217-244) that:
- Requires DepartmentId
- Auto-assigns HQ company
- Clears `JobTypeId = 0`

Tech users now follow the standard Workforce path: select Company, optionally select JobType.

#### 3c. Role Template Filtering

Keep existing filtering: Tech molecules exclude `CompanyJobType` (Lead) and `MoleculeJobType` (Director) scoped roles.

The `DepartmentLead` template (Id=9, `ScopeLevel = RoleScopeLevel.Department`) becomes orphaned. Set `IsVisibleInSignup = false` for it. Existing users with `DepartmentLead` template must be migrated to a Company-scoped role (see Section 5b).

#### 3d. GetSignupOptions API — Department Endpoint

`Pages/Api/Signup/GetSignupOptions.cshtml.cs` has `OnGetDepartmentsAsync(int moleculeId)`. This endpoint is no longer called by the updated Signup form for Shikma. Keep it functional (may be needed if other Tech molecules are added in the future) but the JS no longer invokes it for Shikma.

### 4. Admin & Profile Pages

#### 4a. Admin/Users
- Tech molecule users now show Company (not Department) in the user list
- Department column/filter hidden or deprecated for Tech molecules

#### 4b. EditProfile
- Shows Company dropdown (not Department) for Tech users
- JobType dropdown shows ProjectManager/Hakam

#### 4c. Admin/Organization/Departments
- Page becomes effectively empty for Shikma once migration completes
- Keep functional for backward compat but no active data

### 5. Grant & Role System

#### 5a. Company-Scoped Grants (No Changes)

The existing Company-scoped grant system works for both Workforce and Tech companies:
- `BRDirector` template (`ScopeLevel = Company`) works for Shikma company managers
- Signup filtering already excludes job-type-scoped roles for Tech
- `HasGrantWithScopeAsync` uses `companyId` which both molecule types now have

#### 5b. DepartmentLead Migration

Users currently with the `DepartmentLead` role template have grants scoped to `DepartmentId`. After migration, their `DepartmentId` is cleared, making these grants non-functional.

**Migration:** Convert `DepartmentLead` users to `BRDirector` template (Company-scoped). This gives them equivalent company-level management permissions. Steps:
1. Find users with `RoleTemplateId = 9` (DepartmentLead)
2. Revoke DepartmentLead grants
3. Assign BRDirector template with Company scope
4. Update `RoleTemplateId` to BRDirector's ID

#### 5c. TechShiftService Deprecation

The existing `ITechShiftService` / `TechShiftService` uses a **grant-based** eligibility system with grants like `CanBeAssignedHanava`, `CanBeAssignedDelta`, etc. This is **replaced** by the data-driven `EligibleCompanyIds` + `RequiresOfficerRank` approach on `ShiftType`.

**Deprecation plan:**
1. Remove `ITechShiftService` and `TechShiftService` files
2. Remove DI registration in `Program.cs`
3. Update `ShiftAssignmentService.ValidateShiftAssignmentAsync` to use new ShiftType fields instead of `ITechShiftService.IsUserEligibleForTechShiftAsync`
4. Deprecate `Pages/Api/TechShift/Eligible.cshtml.cs` — its functionality is replaced by the new `GetEligibleUsersForShiftTypeAsync` method in `IShiftCalendarService`
5. The `CanBeAssigned*` grant types (CanBeAssignedHanava, CanBeAssignedDelta, etc.) become unused — keep in GrantTypeSeed for now but don't assign to new users

### 6. Seed Data Changes

#### 6a. ShiftyOrganizationSeed
- Replace `shikmaDepartments` list with `shikmaCompanies` list (same names, Company model)
- Add 2 molecule-scoped JobTypes (ProjectManager, Hakam) — resolve `AreaId` from `molecule.AreaId`, never hardcode
- Keep HQ company for Shikma
- Remove Department seeding for Shikma

#### 6b. TechShiftTypeSeed
- Add `EligibleCompanyIds` and `RequiresOfficerRank` values to each Tech shift type
- Shift types keep `JobTypeId = null` and `TechShiftType` field for categorization
- Tech shift types use HQ company for `CompanyId` (satisfies `IBelongsToCompany`)

#### 6c. QaTestUserSeed
- Convert `AddDeptUser` calls to `AddUser` with corresponding Company
- Assign ProjectManager or Hakam job type to test users
- Update DepartmentLead test user to use BRDirector template with Company scope

#### 6d. Unused Tech Shift Type Keys
`Models/ShiftType.cs` defines `TECH_SUPPORT` and `TECH_ONCALL` keys that are not seeded and not in the eligibility table. These are unused — remove them during cleanup.

### 7. Validation & Assignment Enforcement

#### 7a. ShiftAssignmentService.ValidateShiftAssignmentAsync

Replace the existing `TECH_SHIFT_INELIGIBLE` checks with new validation using ShiftType fields. There are **two callsites** that must both be updated:

1. **`ValidateShiftAssignmentAsync`** (lines 272-284) — single assignment validation
2. **`ValidateBatchAsync`** (lines 903-911) — batch/FillRange validation

Per MEMORY.md: "`ValidateBatchAsync` (FillRange) must stay consistent with single validation defaults and algorithms."

New validation logic (replaces both callsites):

```
if ShiftType.EligibleCompanyIds is not null:
    parse JSON array → if user.CompanyId not in list
    → Hard Error: "User's company is not eligible for this shift type"

if ShiftType.RequiresOfficerRank:
    if !user.Rank.IsOfficer()
    → Hard Error: "This shift type requires officer rank"
```

These are hard errors (not overrideable), since they represent structural ineligibility.

**Behavioral change note:** This upgrades the existing `TECH_SHIFT_INELIGIBLE` warning (overrideable via HMAC token) to a hard error (non-overrideable). This is intentional because the new eligibility model is deterministic — company membership and military rank are structural facts, not situational constraints that a manager should override.

#### 7b. Visibility vs Assignment

All users in the molecule can **view** all shifts. Eligibility rules only restrict **assignment**:
- Calendar rendering shows all shift types (no eligibility filtering)
- Assignment dropdown filters by eligibility (per shift type)
- Bottom sheet needs to be shift-type-aware when showing eligible users

### 8. Migration Strategy

#### 8a. EF Migration (Schema)
1. Add `EligibleCompanyIds` (string, nullable) to `ShiftType`
2. Add `RequiresOfficerRank` (bool, default false) to `ShiftType`
3. Make `ShiftCapacityOverride.JobTypeId` nullable (`int?`)
4. Update `ShiftCapacityOverride.JobType` navigation to nullable

#### 8b. Seed Data Updates
1. Add 6 Shikma Companies (replacing Department seeding)
2. Add 2 Shikma JobTypes (ProjectManager, Hakam)
3. Update Tech shift types with eligibility rules and HQ CompanyId
4. Set `DepartmentLead.IsVisibleInSignup = false`

#### 8c. Data Migration (Existing Records)
1. Create 6 Company records matching existing Department names
2. For each user with `DepartmentId` pointing to a Shikma department:
   - Set `CompanyId` = corresponding new Company
   - Clear `DepartmentId`
3. Migrate `DepartmentLead` users to `BRDirector` template
4. Update existing Tech ShiftType records with `EligibleCompanyIds` and `RequiresOfficerRank`

#### 8d. Order of Operations
1. EF migration (schema changes)
2. Seed data updates (new Companies, JobTypes, shift type eligibility)
3. Data migration (user records, role template migration)
4. Service changes (IShiftCalendarService signatures, TechShiftService deprecation)
5. Code changes (Signup, Calendar, Admin pages, SignalR)
6. Validation changes (ShiftAssignmentService)

---

## Complete File Change Inventory

### Models
- `Models/ShiftType.cs` — Add `EligibleCompanyIds`, `RequiresOfficerRank`; remove unused `TECH_SUPPORT`/`TECH_ONCALL` constants
- `Models/ShiftCapacityOverride.cs` — Make `JobTypeId` nullable, `JobType` navigation nullable
- `Models/Molecule.cs` — Update `Departments` navigation comment (no longer primary for Shikma)

### Services
- `Services/IShiftCalendarService.cs` — Change `int jobTypeId` → `int? jobTypeId` on all methods; add `GetEligibleUsersForShiftTypeAsync`
- `Services/ShiftCalendarService.cs` — Implement nullable jobTypeId logic; implement new eligibility method
- `Services/ShiftAssignmentService.cs` — Replace `TECH_SHIFT_INELIGIBLE` check with EligibleCompanyIds/RequiresOfficerRank
- `Services/ITechShiftService.cs` — **Delete** (replaced)
- `Services/TechShiftService.cs` — **Delete** (replaced)
- `Services/ShiftTypeCacheService.cs` — `InvalidateMoleculeCache` and `GetShiftTypesForMoleculeAsync` need `int? jobTypeId`; when null, invalidate all entries for the given moleculeId (no job-type scoping for Tech)
- `Program.cs` — Remove `ITechShiftService` DI registration

### Calendar Pages
- `Pages/Calendar/Shifts.cshtml.cs` — Tech-aware guards, single view mode, nullable JobTypeId
- `Pages/Calendar/Shifts.cshtml` — Hide JobType dropdown for Tech, update JS scope
- `Pages/Calendar/Table.cshtml.cs` — `IsMoleculeMode` fix, 10 SignalR notification callsites, nullable JobTypeId
- `Pages/Calendar/Table.cshtml` — JS scope initialization for Tech
- `Pages/Api/Calendar/GetShiftsData.cshtml.cs` — Accept jobTypeId=0 for Tech, Tech-aware query

### SignalR
- `Hubs/CalendarHub.cs` — `CalendarGroups.Shifts()` accepts nullable jobTypeId, validation update
- `wwwroot/js/calendar-realtime.js` — Group name with `?? 0` fallback

### Signup
- `Pages/Auth/Signup.cshtml` — Remove Tech-specific JS branch (hide JobType, show Departments)
- `Pages/Auth/Signup.cshtml.cs` — Remove Tech-specific server block (lines 217-244)

### Admin
- `Pages/Admin/Users.cshtml.cs` — Tech users show Company not Department
- `Pages/Admin/EditProfile.cshtml.cs` — Company dropdown for Tech users

### API
- `Pages/Api/TechShift/Eligible.cshtml.cs` — Deprecate (replaced by new eligibility)

### Seed Data
- `Data/SeedData/ShiftyOrganizationSeed.cs` — Shikma Companies instead of Departments, new JobTypes
- `Data/SeedData/TechShiftTypeSeed.cs` — Eligibility rules on shift types
- `Data/SeedData/QaTestUserSeed.cs` — Convert AddDeptUser to AddUser
- `Data/SeedData/RoleTemplateSeed.cs` — DepartmentLead `IsVisibleInSignup = false`

---

## What Does NOT Change

- `MoleculeType.Tech` enum value — kept as behavioral flag
- `MoleculeType` enum itself — no new values
- Workforce molecule behavior — completely unchanged
- On-call/Chores system — unaffected (area-wide Hakam already works)
- Department model/table — not deleted (backward compat), just unused for Shikma
- `AppUser.DepartmentId` column — not removed, cleared for migrated users
- Helper/System molecule behavior — unchanged
- `GrantScopeLevel.Department` enum value — kept (may be needed in future)
- `CanBeAssigned*` grant types — kept in seed but not assigned to new users

## Out of Scope

- Deprecating/removing the Department model entirely
- Adding shift eligibility rules for Workforce molecules
- Changing how Helper or System molecules work
- Vacation approval chains for Tech users
- Tech-specific reporting or analytics
- Removing `CanBeAssigned*` grant types from GrantTypeSeed
