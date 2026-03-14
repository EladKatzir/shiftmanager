# Tech Molecule Convergence: Shikma Departments → Companies

**Date:** 2026-03-14
**Status:** Design approved
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

Shift eligibility rules are explicit, data-driven fields on `ShiftType`.

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

These use `AreaId = 190` (same area as all molecules) and `MoleculeId = shikma.Id` for molecule-specific scoping.

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
    Users WHERE Company.MoleculeId = X (no JobTypeId filter)
else:
    existing behavior (MoleculeId + JobTypeId)
```

#### 2b. Assignment Dropdown — Eligibility Filtering

When assigning a user to a shift, the dropdown shows only eligible users:

- Parse `ShiftType.EligibleCompanyIds` → filter users to those companies
- Check `ShiftType.RequiresOfficerRank` → filter to `user.Rank.IsOfficer()`
- For Workforce shift types (both fields null/default), no change

This requires either a new `GetEligibleUsersForShiftTypeAsync` method or an overload of `GetUsersForCalendarAsync` that accepts eligibility constraints.

#### 2c. GetShiftsData API

The `GetShiftsData` API (used for real-time refresh via SignalR) needs the same Tech-aware query logic.

#### 2d. Calendar/Table Page

Same changes as Calendar/Shifts — Tech molecules show all shift types without JobType filter. The `IsMoleculeMode` logic needs to handle the case where `JobTypeId` is null for Tech.

### 3. Signup Flow Changes

#### 3a. Form Behavior

Remove the Tech-specific JS branch that hides JobType and shows Departments:
- Tech molecules now load Companies (same as Workforce)
- JobType dropdown shows Shikma-specific options (ProjectManager, Hakam)
- The DepartmentId hidden field is no longer populated for new signups

#### 3b. Server-Side Handler

Remove the Tech-specific block in `Signup.cshtml.cs` (lines 217-244) that:
- Requires DepartmentId
- Auto-assigns HQ company
- Clears `JobTypeId = 0`

Tech users now follow the standard Workforce path: select Company, optionally select JobType.

#### 3c. Role Template Filtering

Keep existing filtering: Tech molecules exclude `CompanyJobType` (Lead) and `MoleculeJobType` (Director) scoped roles. The `DepartmentLead` template is no longer applicable — hide from signup for Tech molecules (its `ScopeLevel.Department` scope won't match any molecules once Departments are unused).

### 4. Admin & Profile Pages

#### 4a. Admin/Users
- Tech users show Company (not Department) in the user list
- Department column/filter hidden or deprecated for Tech molecules

#### 4b. EditProfile
- Shows Company dropdown (not Department) for Tech users
- JobType dropdown shows ProjectManager/Hakam

#### 4c. Admin/Organization/Departments
- Page becomes effectively empty for Shikma once migration completes
- Keep functional for backward compat but no active data

### 5. Grant & Role System

No changes needed. The existing Company-scoped grant system works for both Workforce and Tech companies:
- `BRDirector` template (`ScopeLevel = Company`) works for Shikma company managers
- Signup filtering already excludes job-type-scoped roles for Tech
- `HasGrantWithScopeAsync` uses `companyId` which both molecule types now have

### 6. Seed Data Changes

#### 6a. ShiftyOrganizationSeed
- Replace `shikmaDepartments` list with `shikmaCompanies` list (same names, Company model)
- Add 2 molecule-scoped JobTypes (ProjectManager, Hakam)
- Keep HQ company for Shikma

#### 6b. TechShiftTypeSeed
- Add `EligibleCompanyIds` and `RequiresOfficerRank` values to each Tech shift type
- Shift types keep `JobTypeId = null` and `TechShiftType` field for categorization

#### 6c. QaTestUserSeed
- Convert `AddDeptUser` calls to `AddUser` with corresponding Company
- Assign ProjectManager or Hakam job type to test users

### 7. Validation & Assignment Enforcement

#### 7a. ShiftAssignmentService.ValidateShiftAssignmentAsync

Add two new validation checks:

```
if ShiftType.EligibleCompanyIds is not null:
    parse JSON array → if user.CompanyId not in list
    → Hard Error: "User's company is not eligible for this shift type"

if ShiftType.RequiresOfficerRank:
    if !user.Rank.IsOfficer()
    → Hard Error: "This shift type requires officer rank"
```

These are hard errors (not overrideable), since they represent structural ineligibility.

#### 7b. Visibility vs Assignment

All users in the molecule can **view** all shifts. Eligibility rules only restrict **assignment**:
- Calendar rendering shows all shift types (no eligibility filtering)
- Assignment dropdown filters by eligibility (per shift type)
- Bottom sheet needs to be shift-type-aware when showing eligible users

### 8. Migration Strategy

#### 8a. EF Migration (Schema)
1. Add `EligibleCompanyIds` (string, nullable) to `ShiftType`
2. Add `RequiresOfficerRank` (bool, default false) to `ShiftType`

#### 8b. Seed Data Updates
1. Add 6 Shikma Companies (replacing Department seeding)
2. Add 2 Shikma JobTypes (ProjectManager, Hakam)
3. Update Tech shift types with eligibility rules

#### 8c. Data Migration (Existing Records)
1. Create 6 Company records matching existing Department names
2. For each user with `DepartmentId` pointing to a Shikma department:
   - Set `CompanyId` = corresponding new Company
   - Clear `DepartmentId`
3. Update existing Tech ShiftType records with `EligibleCompanyIds` and `RequiresOfficerRank`

#### 8d. Order of Operations
1. EF migration (schema changes)
2. Seed data updates (new Companies, JobTypes)
3. Data migration (user records, shift types)
4. Code changes (Signup, Calendar, Admin pages)
5. Validation changes (ShiftAssignmentService)

---

## What Does NOT Change

- `MoleculeType.Tech` enum value — kept as behavioral flag
- `MoleculeType` enum itself — no new values
- Workforce molecule behavior — completely unchanged
- On-call/Chores system — unaffected (area-wide Hakam already works)
- Department model/table — not deleted (backward compat), just unused for Shikma
- `AppUser.DepartmentId` column — not removed, cleared for migrated users
- Helper/System molecule behavior — unchanged
- Grant system — no changes needed

## Out of Scope

- Deprecating/removing the Department model entirely
- Adding shift eligibility rules for Workforce molecules
- Changing how Helper or System molecules work
- Vacation approval chains for Tech users
- Tech-specific reporting or analytics
