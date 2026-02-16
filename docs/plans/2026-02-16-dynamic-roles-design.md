# Dynamic Role Templates Design

**Date:** 2026-02-16
**Branch:** merged-canonical
**Status:** Approved for implementation
**Approach:** Parallel Run (B) — build new alongside old, migrate fully before release

---

## 1. Goals

1. Enable Owners to create **custom roles** via UI (e.g., TempCommander, DeputyAreaAdmin)
2. Make `RoleTemplate` the **source of truth** for user roles (replacing hardcoded `UserRole` enum)
3. Migrate **all authorization checks** from role-based policies to grant-based (zero TODOs)
4. Keep `AppUser.Role` as an auto-derived cache for **business identity** checks (~20 locations)
5. Add `AreaAdmin` support throughout (already implemented in Phase 1)
6. Zero deferred items — everything ships together

---

## 2. Data Model Changes

### 2.1 RoleTemplate (existing — add fields)

| New Field | Type | Purpose |
|-----------|------|---------|
| `DerivedUserRole` | `UserRole?` | Maps template to business tier (Employee, Manager, Director, Trainee, Owner, Assigner, AreaAdmin). Used to auto-populate `AppUser.Role`. Custom roles MUST set this. |
| `DisplayNameEN` | `string?` | Free-text English display name (for custom roles; system roles use `NameKey` → resx) |
| `DisplayNameHE` | `string?` | Free-text Hebrew display name (for custom roles; system roles use `NameKey` → resx) |
| `CanBeAssignedByDefault` | `bool` | Default: true. Owner can restrict assignment of specific roles. |
| `IsVisibleInSignup` | `bool` | Default: true. Controls whether role appears in public signup dropdown. |

### 2.2 AppUser (existing — add field)

| New Field | Type | Purpose |
|-----------|------|---------|
| `RoleTemplateId` | `int?` (FK → RoleTemplate) | User's primary role template. When set, `AppUser.Role` is auto-derived from `RoleTemplate.DerivedUserRole`. |

Navigation property: `public RoleTemplate? RoleTemplate { get; set; }`

**Relationship to `UserRoleAssignment`:**
- `AppUser.RoleTemplateId` = user's **primary role**. Determines `AppUser.Role` and the user's base grant set.
- `UserRoleAssignment` = **supplementary scoped grants** added on top of the primary role. These are additive, not conflicting. No precedence rules needed — they stack.

### 2.3 RoleTemplateJobTypeLabel (NEW table)

```
Id (int, PK)
RoleTemplateId (int, FK → RoleTemplate)
JobTypeId (int, FK → JobType)
DisplayNameEN (string)
DisplayNameHE (string)
```

Purpose: Optional per-job-type display name overrides (e.g., Manager shows as "Mapotz" for Alhut).
Does NOT implement `IBelongsToCompany` — templates are cross-company.

### 2.4 UserJoinRequest (existing — add field)

| New Field | Type | Purpose |
|-----------|------|---------|
| `RequestedRoleTemplateId` | `int?` (FK → RoleTemplate) | The template the applicant selected during signup. |

### 2.5 GriffinConfig (existing — add field)

| New Field | Type | Purpose |
|-----------|------|---------|
| `DefaultProvisionedRoleTemplateId` | `int?` (FK → RoleTemplate) | Template for auto-provisioned SSO users (replaces `DefaultProvisionedRole` enum). |

### 2.6 RoleAssignmentAudit (existing — add fields)

| New Field | Type | Purpose |
|-----------|------|---------|
| `FromRoleTemplateId` | `int?` | Previous template (for audit trail). |
| `ToRoleTemplateId` | `int?` | New template (for audit trail). |

### 2.7 AppDbContext changes

- Add `DbSet<RoleTemplateJobTypeLabel>`
- Configure FKs: AppUser → RoleTemplate, UserJoinRequest → RoleTemplate, GriffinConfig → RoleTemplate, RoleAssignmentAudit → RoleTemplate (x2), RoleTemplateJobTypeLabel → RoleTemplate + JobType
- NO query filter on RoleTemplateJobTypeLabel (cross-company)
- Add index on `RoleTemplateJobTypeLabel(RoleTemplateId, JobTypeId)` for lookup performance

---

## 3. Seed Data Changes

### 3.1 New Trainee template (ID 12)

```csharp
new RoleTemplate
{
    Id = 12,
    Key = "Trainee",
    NameKey = "Role_Trainee",
    DescriptionKey = "Role_Trainee_Desc",
    ScopeLevel = RoleScopeLevel.Implicit,
    IsSystem = true,
    IsActive = true,
    SortOrder = 101,
    DerivedUserRole = UserRole.Trainee,
    IsVisibleInSignup = false,
    CanBeAssignedByDefault = true
}
```

Trainee grants (subset of Employee — explicitly NO `RequestSwap`):
- ViewShifts (1), ViewChores (17), ViewVacations (21), RequestVacation (22)

### 3.2 DerivedUserRole for all 12 system templates

| ID | Key | ScopeLevel | DerivedUserRole | IsVisibleInSignup |
|----|-----|-----------|----------------|-------------------|
| 1 | Employee | Implicit | Employee | true |
| 2 | BRDirector | Company | Manager | true |
| 3 | AlhutLead | CompanyJobType | Manager | true |
| 4 | TextLead | CompanyJobType | Manager | true |
| 5 | AlhutDirector | MoleculeJobType | Director | true |
| 6 | TextDirector | MoleculeJobType | Director | true |
| 7 | MoleculeAdmin | Molecule | Manager | true |
| 8 | Assigner | Molecule | Assigner | false |
| 9 | DepartmentLead | Department | Manager | true |
| 10 | AreaAdmin | Area | AreaAdmin | true |
| 11 | Owner | Project | Owner | false |
| 12 | Trainee | Implicit | Trainee | false |

**Note on naming:** `BRDirector` (ID 2) is actually a Manager-tier template for BR job type. The name comes from military terminology ("Kavar" = BR Director). It has `ManagerHomeAccess` grant but NOT `DirectorHubAccess`. `DerivedUserRole = Manager` is correct.

### 3.3 New grant types (add to GrantTypeSeed.cs)

Current max ID is 117. New grants start at 118:

| ID | Key | Category | DefaultScope | Purpose |
|----|-----|----------|-------------|---------|
| 118 | `ManageAnnouncements` | System | Company | Create/edit/delete announcements. **Live bug fix** — referenced in CanManageAnnouncements policy but never existed. |
| 119 | `ViewSystemAlerts` | System | Company | See system alert banners in header. |
| 120 | `ViewAllAreas` | System | Area | See all areas in OnCall calendar filter (not just own company's area). |
| 121 | `ManageOnDuty` | Duty | Area | Composite grant for on-duty management. Replaces complex OR logic. |

**Note:** `RequestSwap` (ID 26) already exists — no need to create "CreateSwapRequest".

### 3.4 New grant assignments to system templates

| Template | New Grants |
|----------|-----------|
| Owner (11) | ManageAnnouncements, ViewSystemAlerts, ViewAllAreas, ManageOnDuty |
| AlhutDirector (5) | ManageAnnouncements, ViewAllAreas |
| TextDirector (6) | ManageAnnouncements, ViewAllAreas |
| MoleculeAdmin (7) | ManageAnnouncements |
| BRDirector (2) | ViewSystemAlerts |
| AlhutLead (3) | ViewSystemAlerts |
| TextLead (4) | ViewSystemAlerts |
| DepartmentLead (9) | (none) |
| AreaAdmin (10) | ManageAnnouncements, ViewAllAreas, ManageOnDuty |
| Assigner (8) | (none — uses employee navigation) |
| Employee (1) | (none — already has RequestSwap) |
| Trainee (12) | (none — explicitly NO RequestSwap) |

---

## 4. Authorization Policy Migration

### 4.1 Remove role-based policies from Program.cs

8 policies exist. Migration plan for each:

| Policy | Current Logic | Action | Replacement |
|--------|--------------|--------|-------------|
| `IsManagerOrAdmin` | RequireRole(Manager, Owner, Director, AreaAdmin) | **Migrate** | `Grant:ManagerHomeAccess` |
| `IsAdmin` | RequireRole(Owner) | **Migrate** | `Grant:AdminAccess` |
| `IsDirector` | RequireRole(Owner, Director, AreaAdmin) | **Migrate** | `Grant:DirectorHubAccess` |
| `IsOwnerOrDirector` | RequireRole(Owner, Director, AreaAdmin) | **Remove** | Unused — 0 pages reference it |
| `CanViewChores` | RequireAuthenticatedUser | **Keep** | No role check — stays |
| `CanViewOnDuty` | RequireAuthenticatedUser | **Keep** | No role check — stays |
| `CanEditChores` | RequireRole(Manager, Owner, Director, Assigner, AreaAdmin) | **Migrate** | `Grant:AssignChores` |
| `CanEditOnDuty` | RequireRole(Manager, Owner, Director, AreaAdmin) | **Migrate** | `Grant:ManageOnDuty` |
| `CanManageAnnouncements` | IsInRole(Owner/Director/AreaAdmin) OR HasClaim | **Migrate** | `Grant:ManageAnnouncements` |

**Total: 6 policies migrated, 1 removed (unused), 2 kept (auth-only).**

### 4.2 Page attribute updates

Every page using a migrated policy gets a new attribute:

| Page(s) | Old | New |
|---------|-----|-----|
| Admin/Users, Admin/Config, Admin/AuditLog, Admin/Analytics, Admin/EditProfile, Admin/Index, Chores/Calendar, Requests/Index, Calendar/Table, Assignments/Manage | `[Authorize(Policy = "IsManagerOrAdmin")]` | `[Authorize(Policy = "Grant:ManagerHomeAccess")]` |
| Owner/Blueprints, Owner/Programs, Owner/MasterPrograms | `[Authorize(Policy = "IsManagerOrAdmin")]` | `[Authorize(Policy = "Grant:ManagerHomeAccess")]` |
| Director/Index, Director/ViewAsMode, Director/NotificationHub, Director/CompanyFilter | `[Authorize(Policy = "IsDirector")]` | `[Authorize(Policy = "Grant:DirectorHubAccess")]` |
| Api/Calendar/RestoreChore, Api/Calendar/DeleteChore, Api/Calendar/QuickAddChore | `[Authorize(Policy = "CanEditChores")]` | `[Authorize(Policy = "Grant:AssignChores")]` |
| Api/OnDuty/GetEligibleUsers, Api/Calendar/QuickAddOnDuty, Api/Calendar/DeleteOnDuty | `[Authorize(Policy = "CanEditOnDuty")]` | `[Authorize(Policy = "Grant:ManageOnDuty")]` |
| Admin/Announcements | `[Authorize(Policy = "CanManageAnnouncements")]` | `[Authorize(Policy = "Grant:ManageAnnouncements")]` |

The existing `GrantPolicyProvider` + `GrantAuthorizationHandler` pipeline already handles `Grant:` prefix policies automatically — no registration needed.

### 4.3 Inline `IsInRole` / role check replacements (6 locations)

| # | File | Line(s) | Current | Replacement |
|---|------|---------|---------|-------------|
| 1 | `ViewComponents/SystemAlertsViewComponent.cs` | 29-30 | `IsInRole(Owner)` \|\| `IsInRole(Manager)` | `HasGrantAsync("ViewSystemAlerts")` |
| 2 | `Services/OwnerCompanySelectorService.cs` | 33 | `IsInRole(Owner)` | `HasGrantAsync("AdminAccess")` |
| 3 | `Pages/Requests/Swaps/Create.cshtml.cs` | 46, 75 | `Role == UserRole.Trainee` → block | `!HasGrantAsync("RequestSwap")` |
| 4 | `Pages/Calendar/OnCall.cshtml.cs` | 205 | `Role >= UserRole.Director` | `HasGrantAsync("ViewAllAreas")` |
| 5 | `ViewComponents/OnCallWidgetViewComponent.cs` | 73 | `IsInRole(Manager/Director/Owner)` | `HasGrantAsync("ManagerHomeAccess")` |
| 6 | `Program.cs` | 189-191 | `IsInRole` inside CanManageAnnouncements | Removed (policy deleted) |

---

## 5. Role Sync & Login Changes

### 5.1 SyncUserRoleFromTemplate service method

```csharp
public static void SyncRoleFromTemplate(AppUser user, RoleTemplate template)
{
    if (template.DerivedUserRole.HasValue)
        user.Role = template.DerivedUserRole.Value;
}
```

Called at:
- User creation (Admin/Users OnPostAddAsync)
- Role change (Admin/Users OnPostRoleAsync)
- Join request approval (Admin/Users OnPostApproveAsync, OnPostBatchApproveAsync)
- Griffin auto-provisioning (GriffinService)
- Login-time safety net (Login.cshtml.cs)

### 5.2 Login claims

Add `RoleTemplateKey` claim alongside existing `JobTypeName` claim:

```csharp
// In Login.cshtml.cs and GriffinService.cs:
if (user.RoleTemplate != null)
{
    claims.Add(new Claim("RoleTemplateKey", user.RoleTemplate.Key));
}
```

### 5.3 Login-time backfill safety net

```csharp
if (user.RoleTemplateId == null)
{
    var templateKey = MapUserRoleToRoleTemplateKey(user.Role, user.JobType?.Name);
    var template = await _db.RoleTemplates.FirstOrDefaultAsync(rt => rt.Key == templateKey);
    if (template != null)
    {
        user.RoleTemplateId = template.Id;
        user.Role = template.DerivedUserRole ?? user.Role;
        await _db.SaveChangesAsync();
    }
}
```

---

## 6. UI Changes

### 6.1 Enum-to-dynamic dropdown locations (10 locations)

All hardcoded `UserRole` enum dropdowns must query `RoleTemplates` instead:

| # | File | Current | Replacement |
|---|------|---------|-------------|
| 1 | `Admin/Users.cshtml` (line ~513) | Join request filter: hardcoded `<option value="2">` | `@foreach (var rt in Model.AvailableRoleTemplates)` |
| 2 | `Admin/Users.cshtml` (line ~733) | User filter: hardcoded `<option value="2">` | Same dynamic pattern |
| 3 | `Admin/Users.cshtml` (line ~586) | Join request assignment: `Model.AssignableRoles` (enum) | `Model.AssignableRoleTemplates` (from DB) |
| 4 | `Admin/Users.cshtml` (line ~839) | Inline role editor: `Model.AssignableRoles` (enum) | `Model.AssignableRoleTemplates` |
| 5 | `Admin/Users.cshtml` (line ~981) | Add user form: `Model.AssignableRoles` (enum) | `Model.AssignableRoleTemplates` |
| 6 | `Admin/EditProfile.cshtml` (line ~116) | Hardcoded `<option value="@UserRole.Employee">` | Dynamic from templates |
| 7 | `Admin/Announcements.cshtml.cs` (line ~108) | `Enum.GetValues<UserRole>()` | `_db.RoleTemplates.Where(rt => rt.IsActive)` |
| 8 | `Owner/GriffinConfig.cshtml.cs` (line ~213) | `Enum.GetValues(typeof(UserRole))` | `_db.RoleTemplates.Where(rt => rt.IsActive)` |
| 9 | `Admin/Companies.cshtml.cs` (line ~265) | `Role = UserRole.Manager` (auto-create) | Assign template based on context |
| 10 | `Auth/Signup.cshtml` (line ~145) | Hardcoded role options with enum int values | Dynamic from templates with `IsVisibleInSignup` filter |

### 6.2 Signup page changes

- Role dropdown populated from `RoleTemplates.Where(rt => rt.IsActive && rt.IsVisibleInSignup)`
- Job-type-specific role labels via `roleLabelMap` JS (already implemented in Phase 1)
- `RequestedRoleTemplateId` sent instead of `RequestedRole` enum
- Director/AreaAdmin HQ auto-resolve: check `DerivedUserRole ∈ {Director, AreaAdmin}` instead of enum

### 6.3 RoleDisplayHelper update

```csharp
public static string GetRoleDisplayName(
    IStringLocalizer<SharedResources> localizer,
    RoleTemplate template,
    string? jobTypeKey,
    string? cultureName = null)
{
    // 1. Check job-type-specific label from RoleTemplateJobTypeLabel
    //    (passed as parameter or pre-loaded)

    // 2. Check custom role display name
    if (!string.IsNullOrEmpty(template.DisplayNameEN) || !string.IsNullOrEmpty(template.DisplayNameHE))
    {
        var isHebrew = cultureName?.StartsWith("he") == true;
        var displayName = isHebrew ? template.DisplayNameHE : template.DisplayNameEN;
        if (!string.IsNullOrEmpty(displayName)) return displayName;
    }

    // 3. Fall back to resx key (system templates)
    if (!string.IsNullOrEmpty(template.NameKey))
    {
        var localized = localizer[template.NameKey];
        if (!localized.ResourceNotFound) return localized.Value;
    }

    // 4. Ultimate fallback
    return template.Key;
}
```

Overload keeping backward compat for string-based callers (used by ~20 business identity sites):
```csharp
public static string GetRoleDisplayName(
    IStringLocalizer<SharedResources> localizer,
    string roleName,
    string? jobTypeKey)
{
    // Existing implementation (unchanged)
}
```

### 6.4 Admin/Users backend changes

- `UserVM` and `JoinRequestVM` records: add `RoleTemplateKey` field (from `u.RoleTemplate?.Key`)
- `AssignableRoles` property: change from `List<UserRole>` to `List<RoleTemplate>` queried from DB, filtered by `CanBeAssignedByDefault` and current user's grant level
- Remove `MapUserRoleToRoleTemplateKey` helper — direct template assignment replaces it
- DirectorCompany creation: check `template.DerivedUserRole ∈ {Director, AreaAdmin, Owner}` instead of enum

### 6.5 Command palette (site.js)

Line ~932 has hardcoded role-based navigation filtering:
```javascript
roles: ['Manager', 'Director', 'Owner']
```

Replace with grant-based visibility using data attributes rendered server-side:
```javascript
grants: ['ManagerHomeAccess']
```

### 6.6 Analytics

`Admin/Analytics.cshtml.cs` line 46: `Dictionary<UserRole, decimal> HoursByRole`
Change to: `Dictionary<string, decimal> HoursByRole` keyed by `RoleTemplate.Key`

### 6.7 API DTOs

`Controllers/TeamCalendarsController.cs` line 37: `GetCurrentUserRole()` returns enum.
Change to return `RoleTemplateKey` string from claims.

`SwapRequestApiService.cs` line 464: `Role = user.Role.ToString()` in DTO.
Change to: `Role = user.RoleTemplate?.Key ?? user.Role.ToString()`

---

## 7. DirectorCompany Table

**Decision:** Keep `DirectorCompany` — it maps user IDs to company IDs regardless of role system.

**Change needed:** When assigning a role template, create DirectorCompany entries if `template.DerivedUserRole ∈ {Director, AreaAdmin, Owner}`. This is already done in `Users.cshtml.cs` lines 600-624 for enum values — change the condition to check DerivedUserRole.

No structural changes to DirectorCompany model.

---

## 8. DirectorService.CanAssignRole Refactoring

Current: hardcoded `UserRole` enum checks (lines 97-150).

Replace `CanAssignRole(UserRole)` with `CanAssignRoleTemplateAsync(RoleTemplate template)`:

```csharp
public async Task<bool> CanAssignRoleTemplateAsync(RoleTemplate template)
{
    // AdminAccess → can assign any role
    if (await HasGrantAsync("AdminAccess")) return true;

    // DirectorHubAccess → can assign templates up to Director tier
    if (await HasGrantAsync("DirectorHubAccess"))
        return template.DerivedUserRole != UserRole.Owner;

    // ManagerHomeAccess → can assign Employee/Trainee tier only
    if (await HasGrantAsync("ManagerHomeAccess"))
        return template.DerivedUserRole == UserRole.Employee
            || template.DerivedUserRole == UserRole.Trainee;

    return false;
}
```

---

## 9. Owner Role Management UI

### 9.1 Page location

`Pages/Owner/Hub/RoleTemplates/` — new subfolder under existing Owner Hub.

### 9.2 Pages

**Index.cshtml** — List all role templates (active/inactive, system/custom, user counts, grant counts).

**Create.cshtml** — Create custom role template:
- Key (auto-generated from EN name if blank, alphanumeric, unique)
- Display Name EN (required)
- Display Name HE (required)
- Scope Level (dropdown)
- Derived Tier (dropdown: Employee, Manager, Director, Trainee)
- Is Visible In Signup (checkbox)
- Can Be Assigned By Default (checkbox)
- Job-Type Labels (optional repeater: per job type EN + HE)
- On save: create RoleTemplate + RoleTemplateJobTypeLabel records

**Edit.cshtml** — Edit existing template:
- Same form as Create
- System templates: Key and DerivedUserRole are read-only
- Grant assignment section (reuse `Owner/Hub/Grants` component)
- Shows current user count

**Delete** — POST handler only:
- Cannot delete system templates (IsSystem = true)
- Cannot delete if active users assigned
- Soft-delete (set IsActive = false)

### 9.3 Custom role display names

Custom roles use `DisplayNameEN`/`DisplayNameHE` fields directly (stored in DB on RoleTemplate).
System roles use `NameKey` → resx (unchanged).
Both are editable via Language Management page (for system roles, Owner edits the resx override).

---

## 10. Migration & Backfill

### 10.1 EF Migration

One migration adds all new fields + new table. All new FK fields are nullable for backward compat.

### 10.2 Backfill SQL (in migration Up())

Uses the exact same mapping as `MapUserRoleToRoleTemplateKey`:

```sql
-- Backfill RoleTemplateId for existing users
UPDATE Users SET RoleTemplateId = (
    SELECT rt.Id FROM RoleTemplates rt WHERE rt.Key = CASE
        WHEN Users.Role = 0 THEN 'Owner'
        WHEN Users.Role = 5 THEN 'Assigner'
        WHEN Users.Role = 6 THEN 'AreaAdmin'
        WHEN Users.Role = 2 THEN 'Employee'
        WHEN Users.Role = 4 THEN 'Trainee'
        WHEN Users.Role = 1 THEN (
            SELECT CASE jt.Name
                WHEN 'Alhut' THEN 'AlhutLead'
                WHEN 'Text' THEN 'TextLead'
                ELSE 'BRDirector'
            END FROM JobTypes jt WHERE jt.Id = Users.JobTypeId
        )
        WHEN Users.Role = 3 THEN (
            SELECT CASE jt.Name
                WHEN 'Alhut' THEN 'AlhutDirector'
                WHEN 'Text' THEN 'TextDirector'
                ELSE 'MoleculeAdmin'
            END FROM JobTypes jt WHERE jt.Id = Users.JobTypeId
        )
        ELSE 'Employee'
    END
)
WHERE RoleTemplateId IS NULL;

-- Fallback for users with NULL JobTypeId
UPDATE Users SET RoleTemplateId = (
    SELECT rt.Id FROM RoleTemplates rt WHERE rt.Key = CASE
        WHEN Users.Role = 1 THEN 'BRDirector'
        WHEN Users.Role = 3 THEN 'MoleculeAdmin'
    END
)
WHERE RoleTemplateId IS NULL AND Users.Role IN (1, 3);
```

### 10.3 Test data seed update

`Data/SeedData/TestDataSeed.cs` — `CreateTestUserAsync` must also set `RoleTemplateId` based on the same mapping. Add after user creation:
```csharp
user.RoleTemplateId = MapRoleToTemplateId(role, jobTypeId);
```

---

## 11. Business Identity Checks (Stay on AppUser.Role)

These ~20 locations correctly use `AppUser.Role` for "who are they?" logic. They continue working because `AppUser.Role` is auto-derived from `RoleTemplate.DerivedUserRole`:

**Trainee-specific (6):**
- `My/Index.cshtml.cs:147` — load shadowing shifts
- `TraineeService.cs:191` — validate user is Trainee
- `TraineeService.cs:417` — query all Trainees
- `Requests/Index.cshtml.cs:224` — cancel shadowing on time-off
- `DecisionRibbonViewComponent:42` — hide ribbon for Trainee/Employee
- `NotificationService.cs:1037` — trainee notification methods

**Director-specific (2):**
- `ChoreService.cs:86` — Directors cannot be assigned chores
- `Admin/Directors.cshtml.cs:87,94,124` — query/validate Directors

**Owner-specific (2):**
- `NotificationService.cs:433` — find Owners for access request notifications
- `Owner/SystemHealth.cshtml.cs:275` — check Owner default password

**Display/audit (8+):**
- Security logging in Requests/Index (5 locations)
- DTO serialization in SwapRequestApiService
- DecisionRibbonViewComponent ViewModel
- AnnouncementService role-scoped targeting
- GriffinService claims infrastructure

---

## 12. Complete File Change List

### Models & Data (10 files)

| # | File | Type | Change |
|---|------|------|--------|
| 1 | `Models/RoleTemplate.cs` | Edit | Add 5 new fields |
| 2 | `Models/AppUser.cs` | Edit | Add RoleTemplateId FK + navigation |
| 3 | `Models/RoleTemplateJobTypeLabel.cs` | **New** | Job-type display name overrides |
| 4 | `Models/UserJoinRequest.cs` | Edit | Add RequestedRoleTemplateId FK |
| 5 | `Models/GriffinConfig.cs` | Edit | Add DefaultProvisionedRoleTemplateId FK |
| 6 | `Models/RoleAssignmentAudit.cs` | Edit | Add From/ToRoleTemplateId |
| 7 | `Data/AppDbContext.cs` | Edit | DbSet, FKs, indexes |
| 8 | `Data/SeedData/RoleTemplateSeed.cs` | Edit | DerivedUserRole, new fields, Trainee template (ID 12), new grant assignments |
| 9 | `Data/SeedData/GrantTypeSeed.cs` | Edit | Add 4 new grant types (IDs 118-121) |
| 10 | `Migrations/...AddDynamicRoleTemplateFields.cs` | **New** | EF migration + backfill SQL |

### Authorization (8 files)

| # | File | Type | Change |
|---|------|------|--------|
| 11 | `Program.cs` | Edit | Remove 6 policies, keep 2 auth-only |
| 12 | `ViewComponents/SystemAlertsViewComponent.cs` | Edit | IsInRole → HasGrantAsync("ViewSystemAlerts") |
| 13 | `ViewComponents/OnCallWidgetViewComponent.cs` | Edit | IsInRole → HasGrantAsync("ManagerHomeAccess") |
| 14 | `Services/OwnerCompanySelectorService.cs` | Edit | IsInRole → HasGrantAsync("AdminAccess") |
| 15 | `Pages/Requests/Swaps/Create.cshtml.cs` | Edit | Role check → HasGrantAsync("RequestSwap") |
| 16 | `Pages/Calendar/OnCall.cshtml.cs` | Edit | Role check → HasGrantAsync("ViewAllAreas") |
| 17 | `Services/DirectorService.cs` | Edit | CanAssignRole → CanAssignRoleTemplateAsync |
| 18 | `Pages/Admin/Directors.cshtml.cs` | Edit | Query by DerivedUserRole instead of enum |

### Login & Claims (2 files)

| # | File | Type | Change |
|---|------|------|--------|
| 19 | `Pages/Auth/Login.cshtml.cs` | Edit | RoleTemplateKey claim, login-time backfill |
| 20 | `Services/GriffinService.cs` | Edit | Same pattern |

### Admin UI (8 files)

| # | File | Type | Change |
|---|------|------|--------|
| 21 | `Pages/Admin/Users.cshtml` | Edit | Dynamic filter/assignment dropdowns, RoleDisplayHelper |
| 22 | `Pages/Admin/Users.cshtml.cs` | Edit | Template-based CRUD, remove MapUserRoleToRoleTemplateKey |
| 23 | `Pages/Admin/EditProfile.cshtml` | Edit | Dynamic role dropdown |
| 24 | `Pages/Admin/EditProfile.cshtml.cs` | Edit | Template-based role assignment |
| 25 | `Pages/Admin/Companies.cshtml.cs` | Edit | Template-based auto-create, query by DerivedUserRole |
| 26 | `Pages/Admin/Announcements.cshtml.cs` | Edit | Role dropdown from templates |
| 27 | `Pages/Admin/Analytics.cshtml.cs` | Edit | HoursByRole dict key → string |
| 28 | `Data/SeedData/TestDataSeed.cs` | Edit | Set RoleTemplateId on test users |

### Signup (3 files)

| # | File | Type | Change |
|---|------|------|--------|
| 29 | `Pages/Auth/Signup.cshtml` | Edit | Dynamic role dropdown from templates |
| 30 | `Pages/Auth/Signup.cshtml.cs` | Edit | Use RequestedRoleTemplateId, check DerivedUserRole for HQ |
| 31 | `Pages/Api/Signup/GetSignupOptions.cshtml.cs` | Edit | Add role templates endpoint |

### Owner UI (5 files)

| # | File | Type | Change |
|---|------|------|--------|
| 32 | `Pages/Owner/Hub/RoleTemplates/Index.cshtml` | **New** | List templates |
| 33 | `Pages/Owner/Hub/RoleTemplates/Index.cshtml.cs` | **New** | List handler |
| 34 | `Pages/Owner/Hub/RoleTemplates/Create.cshtml` | **New** | Create form |
| 35 | `Pages/Owner/Hub/RoleTemplates/Create.cshtml.cs` | **New** | Create handler |
| 36 | `Pages/Owner/Hub/RoleTemplates/Edit.cshtml` | **New** | Edit form + grant assignment |
| 37 | `Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs` | **New** | Edit handler |

### Other (7 files)

| # | File | Type | Change |
|---|------|------|--------|
| 38 | `Helpers/RoleDisplayHelper.cs` | Edit | Support template + job-type labels |
| 39 | `Pages/Shared/_Layout.cshtml` | Edit | Use RoleTemplateKey claim |
| 40 | `Owner/GriffinConfig.cshtml.cs` | Edit | Use DefaultProvisionedRoleTemplateId |
| 41 | `Owner/SystemHealth.cshtml.cs` | Edit | Query by DerivedUserRole |
| 42 | `wwwroot/js/site.js` | Edit | Command palette: grant-based visibility |
| 43 | `Controllers/TeamCalendarsController.cs` | Edit | Return template key in DTO |
| 44 | `ViewComponents/DecisionRibbonViewComponent.cs` | Edit | Use DerivedUserRole for tier checks |

**Total: 44 files (37 edits + 7 new)**

---

## 13. Verification Plan

1. `dotnet build` — 0 warnings, 0 errors
2. `dotnet test` — all tests pass (update test assertions as needed)
3. Delete `app.db`, run app, verify:
   - All 12 system templates seeded with correct DerivedUserRole
   - 4 new grant types seeded (IDs 118-121)
   - New grant assignments on templates
   - Test users have RoleTemplateId set
4. Signup flow:
   - Role dropdown shows only IsVisibleInSignup templates
   - Selecting Director-tier template shows HQ hint, hides company
   - Job-type labels update dynamically
5. Login flow:
   - RoleTemplateKey claim present in cookie
   - Login-time backfill works for users without RoleTemplateId
6. Admin/Users:
   - Filter dropdowns show all active templates
   - Add user form uses template dropdown
   - Role change assigns template + syncs Role enum
   - DirectorCompany created for Director/AreaAdmin/Owner tier
7. Owner/Hub/RoleTemplates:
   - Create custom role with EN/HE names
   - Assign grants to custom role
   - Custom role appears in signup/admin dropdowns
   - Users with custom role see correct display name in sidebar
8. Authorization:
   - All `Grant:` policies work (verify each page loads)
   - Custom role with ManagerHomeAccess grant can access admin pages
   - Custom role without grant gets 403
9. Sidebar: shows localized role display name based on template + job type
10. No regressions: all existing features work (calendar, requests, chores, on-duty)

---

## 14. What Is NOT Deferred

Everything ships together. Specifically:
- Owner UI for role creation: **included**
- All 6 policy migrations: **included**
- All 6 inline IsInRole fixes: **included**
- All 10 UI dropdown conversions: **included**
- DirectorService refactoring: **included**
- Backfill migration: **included**
- Test updates: **included**
- Command palette fix: **included**
- Live bug fix (ManageAnnouncements grant): **included**
