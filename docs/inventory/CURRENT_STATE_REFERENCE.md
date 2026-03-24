# ShiftManager Current State Reference Document

**Version:** 1.0
**Date:** 2026-02-04
**Branch:** update

---

## Table of Contents

1. [Calendars](#1-calendars)
2. [Roles & Permissions](#2-roles--permissions)
3. [Military Rank System](#3-military-rank-system)
4. [Chores](#4-chores)
5. [Shared Concepts & Constraints](#5-shared-concepts--constraints)
6. [Permission Matrix](#6-permission-matrix)
7. [Known Gaps & Gotchas](#7-known-gaps--gotchas)

---

## 1. Calendars

### 1.1 Calendar Types Overview

| Calendar | Location | Purpose | Scope | Users |
|----------|----------|---------|-------|-------|
| **Month View** | `/Calendar/Month` | Read-only 6-week grid | A-018 Filtered | All authenticated |
| **Week View** | `/Calendar/Week` | Read-only 7-day horizontal | A-018 Filtered | All authenticated |
| **Day View** | `/Calendar/Day` | Read-only single-day detail | A-018 Filtered | All authenticated |
| **Table (Shift Mgmt)** | `/Calendar/Table` | Shift assignment matrix | Company | Managers+ |
| **Personal Timeline** | `/My/Index` | User's own schedule | Current user | All authenticated |
| **Team Calendars** | `/MyTeam/Index` | Private multi-member view | Company | All (own calendars) |
| **Chores Calendar** | `/Chores/Calendar` | Chore management | Company | Managers+ |
| **Public On-Duty** | `/Public/OnDuty` | Cross-company on-duty view | **Global** | All authenticated |

### 1.2 Calendar Data Sources

Each calendar aggregates from these data models:

| Data Type | Model | CompanyId? | Tenant Isolated? | Notes |
|-----------|-------|------------|------------------|-------|
| Scheduled Shifts | `ShiftInstance` + `ShiftAssignment` | Yes | Yes | Company-scoped |
| Chores | `Chore` | Yes | Yes | Company + Molecule scoped |
| On-Duty (Day Shifts) | `OnDuty` | **No** | **No** | Intentionally global |
| Vacations | `TimeOffRequest` | Yes | Yes | Company-scoped |
| Team Calendars | `TeamCalendar` | Yes | Yes | Owner-private |

### 1.3 Tenant Isolation Rules

#### Company-Scoped Entities (Standard Multi-Tenancy)
```csharp
// These entities implement IBelongsToCompany
// Query filter automatically applied:
modelBuilder.Entity<ShiftInstance>()
    .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());
```

**Affected:** ShiftInstance, ShiftAssignment, Chore, TeamCalendar, TimeOffRequest, AppUser

#### Global Entities (No Tenant Isolation)
```csharp
// OnDuty queries explicitly bypass tenant filter:
var onDuties = await _db.OnDuties.IgnoreQueryFilters()
    .Where(od => od.Date >= start && od.Date <= end && od.CanceledAt == null)
    .ToListAsync();
```

**Affected:** OnDuty, OnDutyTypeConfig

### 1.4 Scope Filtering System (A-018)

The unified calendars (Month/Week/Day) support scope filtering:

| Scope Type | Description | Resolution |
|------------|-------------|------------|
| `company` | Single company | User's company or selected |
| `molecule` | All companies in molecule | Resolves to list of CompanyIds |
| `area` | All companies in area | Resolves to list of CompanyIds |
| `mine` | User's items only | Forces MyItemsOnly filter |

**Implementation:** `ScopeFilterService.ResolveCompanyIdsForScopeAsync()`

### 1.5 Calendar Permissions

| Calendar | Read | Write | Admin |
|----------|------|-------|-------|
| Month/Week/Day | All authenticated | N/A (read-only) | N/A |
| Table | Managers+ | Managers+ | Owners |
| Personal | Own user | N/A (read-only) | N/A |
| Team Calendars | Calendar owner | Calendar owner | Calendar owner |
| Chores Calendar | All authenticated | Managers+ | Owners |
| Public On-Duty | All authenticated | AreaAdmins+ | Owners |

### 1.6 Cross-Tenant Behaviors (Intentional)

| Feature | Behavior | Justification |
|---------|----------|---------------|
| On-Duty assignments | Users from any company can be assigned | Coordination requires cross-org visibility |
| On-Duty visibility | All users see all on-duty assignments | Public schedule for contact info |
| Trainee shadowing | Can shadow trainer from different company | Training programs cross boundaries |
| Owner company selector | Owners can switch "watched" company | Multi-company administration |

---

## 2. Roles & Permissions

### 2.1 Role Definitions

#### Legacy Role System (UserRole enum)
```csharp
public enum UserRole
{
    Owner = 0,      // System-wide admin
    Manager = 1,    // Company admin
    Employee = 2,   // Standard worker
    Director = 3,   // Multi-company admin
    Trainee = 4,    // Learning worker
    Assigner = 5,   // Can edit Chores only
    AreaAdmin = 6   // Area-wide administration
}
```

#### V3 Role Template System (RoleTemplate + RoleScopeLevel)

| Role Template | Key | Scope Level | Purpose |
|---------------|-----|-------------|---------|
| Employee | `Employee` | Implicit | Base level, no assignment needed |
| BR Director | `BRDirector` | Company | Company-wide BR shift management |
| Alhut Lead | `AlhutLead` | CompanyJobType | Assign Alhut shifts in Company+JobType |
| Text Lead | `TextLead` | CompanyJobType | Assign Text shifts in Company+JobType |
| Alhut Director | `AlhutDirector` | MoleculeJobType | Molecule-wide Alhut management |
| Text Director | `TextDirector` | MoleculeJobType | Molecule-wide Text management |
| Molecule Admin | `MoleculeAdmin` | Molecule | Full molecule administration |
| Assigner | `Assigner` | Molecule | Chore assignment only |
| Department Lead | `DepartmentLead` | Department | Tech department management |
| Area Admin | `AreaAdmin` | Area | Area-wide administration |
| Owner | `Owner` | Project | Full system access |

### 2.2 Scope Level Hierarchy

```
Project (Owner)
    └── Area (AreaAdmin)
        └── Molecule (MoleculeAdmin, Assigner)
            ├── MoleculeJobType (AlhutDirector, TextDirector)
            │   └── CompanyJobType (AlhutLead, TextLead)
            ├── Company (BRDirector)
            └── Department (DepartmentLead)
                └── Implicit (Employee)
```

**Inheritance Rule:** Higher scope grants include access to all lower scopes within that hierarchy branch.

### 2.3 Grant Categories

| Category | Count | Examples |
|----------|-------|----------|
| Shift | 43 | ViewShifts, AssignAlhutShifts, ManageHakamPrograms |
| Duty | 5 | ViewDuties, AssignHakamDuties, AssignKatzinDuties |
| Chore | 9 | ViewChores, AssignChores, ManageShiklutPrograms |
| Vacation | 4 | ViewVacations, RequestVacation, ApproveVacations |
| Swap | 3 | RequestSwap, ApproveSwaps, InitiateSwap |
| UserManagement | 7 | ViewUsers, EditUsers, CreateUsers, ViewAllUsers |
| GrantManagement | 4 | ViewGrants, AssignGrants, RevokeGrants, AssignRoles |
| Hierarchy | 9 | ViewHierarchy, EditCompany, ManageShiftGroupings |
| Settings | 4 | ViewSettings, EditCompanySettings, EditAreaSettings |
| Analytics | 3 | ViewAnalytics, ViewReports, ExportData |
| Email | 2 | SendNotifications, ConfigureEmailSettings |
| System | 4 | AdminAccess, SystemConfiguration, ViewAuditLog |

**Total:** 125 defined grant types

### 2.4 Role Assignment

| Method | Who Assigns | Who Gets Assigned | Notes |
|--------|-------------|-------------------|-------|
| Manual (Legacy) | Owner/Admin | Any user | Via user edit page |
| Auto-Grant | System | On role template assignment | Triggered by UserRoleAssignment |
| Setup Tasks | Directed by system | Suggested users | Not yet implemented |

**Current State:** Roles are primarily assigned via legacy `UserRole` enum on `AppUser`. V3 role templates exist but auto-grant wiring is incomplete.

### 2.5 Grant Inheritance & Edge Cases

| Scenario | Behavior |
|----------|----------|
| Project grant + Company scope request | Granted (Project > Company) |
| Company grant + Molecule scope request | **Denied** (Company < Molecule) |
| Multiple overlapping grants | Union (additive merging) |
| Self-scope grant | Only applies to user's own resources |
| CanOwn=true, CanGive=false | Can perform action but cannot delegate |
| CanOwn=false, CanGive=true | Cannot perform but can delegate (rare) |

---

## 3. Military Rank System

### 3.1 Overview

The Military Rank system tracks IDF (Israel Defense Forces) ranks for users. It is primarily used for **eligibility checks** when assigning users to certain on-duty types that require officer rank (e.g., Katzin duty).

### 3.2 MilitaryRank Enum

```csharp
public enum MilitaryRank
{
    // Enlisted (0-8)
    Turai = 0,              // טוראי - Private
    TuraiRishon = 1,        // טוראי ראשון - Private First Class
    RavTurai = 2,           // רב טוראי - Corporal
    Samal = 3,              // סמל - Sergeant
    SamalRishon = 4,        // סמל ראשון - Staff Sergeant
    RavSamal = 5,           // רב סמל - Sergeant First Class
    RavSamalMitkadam = 6,   // רב סמל מתקדם - Master Sergeant
    RavSamalBakhir = 7,     // רב סמל בכיר - Senior Master Sergeant
    RavNagad = 8,           // רב נגד - Warrant Officer

    // Officers (9+)
    SegenMishne = 9,        // סגן משנה - Second Lieutenant
    Segen = 10,             // סגן - Lieutenant
    Seren = 11,             // סרן - Captain
    RavSeren = 12,          // רב סרן - Major
    SganAluf = 13,          // סגן אלוף - Lieutenant Colonel
    AlufMishne = 14,        // אלוף משנה - Colonel
    TatAluf = 15,           // תת אלוף - Brigadier General
    Aluf = 16,              // אלוף - Major General
    RavAluf = 17            // רב אלוף - Lieutenant General
}
```

**Rank Categories:**
- **Enlisted (0-2):** Private through Corporal
- **NCO (3-8):** Sergeant through Warrant Officer
- **Officers (9+):** Second Lieutenant through Lieutenant General

### 3.3 Rank-Based Eligibility

#### Feature Flag

```json
// appsettings.json
{
  "Features": {
    "EnforceRankEligibility": true  // Set to false to disable rank checks
  }
}
```

When `EnforceRankEligibility` is `true`:
- On-duty types with `RequiresOfficerRank = true` will only allow users with rank >= `SegenMishne` (9)
- Non-officers attempting to be assigned will be rejected with an error

When `EnforceRankEligibility` is `false`:
- Rank checks are bypassed (useful for development/testing)

#### OnDutyTypeConfig Integration

```csharp
public class OnDutyTypeConfig
{
    // ... other properties
    public bool RequiresOfficerRank { get; set; } = false;  // e.g., Katzin duty
}
```

#### Eligibility Check Flow

```
User Assignment Request
       │
       ▼
OnDutyService.IsUserEligibleForDutyTypeAsync()
       │
       ├── Check: Is EnforceRankEligibility enabled?
       │   └── No → Return true (skip rank check)
       │
       ├── Check: Does duty type require officer rank?
       │   └── No → Return true (all ranks eligible)
       │
       └── Check: Is user rank >= SegenMishne (9)?
           ├── Yes → Return true (eligible)
           └── No → Return false (not eligible)
```

### 3.4 Extension Methods

Located in `Models/Support/MilitaryRankExtensions.cs`:

| Method | Description | Example |
|--------|-------------|---------|
| `IsOfficer()` | Returns true if rank >= 9 | `Seren.IsOfficer() // true` |
| `IsNCO()` | Returns true if rank 3-8 | `Samal.IsNCO() // true` |
| `IsEnlisted()` | Returns true if rank < 3 | `Turai.IsEnlisted() // true` |
| `GetDisplayName(lang)` | Returns localized name | `Seren.GetDisplayName("he") // "סרן"` |
| `GetAbbreviation()` | Returns Hebrew abbreviation | `Seren.GetAbbreviation() // "סרן"` |
| `GetBadgeClass()` | Returns CSS class for styling | `Seren.GetBadgeClass() // "officer"` |

### 3.5 UI Locations

| Location | Purpose | Access |
|----------|---------|--------|
| `/Admin/EditProfile` | Edit user's military rank | Managers+ |
| `/My/Settings` | View own rank (read-only for non-admins) | All authenticated |
| `/Public/OnDuty` | Display rank badges on on-duty assignments | All authenticated |
| On-Duty assignment dropdowns | Filter eligible users by rank | Managers+ |

### 3.6 Audit Logging

Rank changes are logged to both:
1. **ProfileChangeAudit** - Tracks field-level changes for profile history display
2. **AuditLog** - Main audit trail with `action: "UserRankChanged"` for compliance

```csharp
// AuditLog entry details format
{
  "action": "UserRankChanged",
  "entityType": "User",
  "entityId": 123,
  "description": "Changed military rank for John Doe (john@example.com)",
  "details": "{\"oldRank\": \"Turai\", \"newRank\": \"Segen\", ...}"
}
```

### 3.7 Data Storage

- **Property:** `AppUser.Rank` (type: `MilitaryRank`, default: `Turai`)
- **Database column:** `Rank` (integer, stored as enum value 0-17)

---

## 4. Chores

### 4.1 Data Model

```csharp
public class Chore : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }           // Multi-tenant isolation
    public int? MoleculeId { get; set; }         // Molecule scoping (v2.8.4+)
    public int UserId { get; set; }              // Assignee
    public DateOnly Date { get; set; }           // Date only, no time
    public string Title { get; set; }            // Max 200 chars
    public string? Notes { get; set; }           // Max 1000 chars
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }    // Soft delete
    public int? CanceledBy { get; set; }

    public bool IsActive => CanceledAt == null;
}
```

### 4.2 Ownership & Assignment Rules

| Role | Can Create | Can Assign To | Can Cancel |
|------|-----------|---------------|------------|
| Owner | Yes | All users (except Directors) | Yes |
| Director | Yes | Users in managed companies | Yes (managed companies) |
| Manager | Yes | Users in own company | Yes (own company) |
| Assigner | Yes | Users in own company | Limited |
| Employee | No | — | No |
| Trainee | No | — | No |

**Key Constraint:** Directors cannot be assigned chores (enforced in service layer).

### 4.3 Lifecycle

```
[Not Exists] → CreateChoreAsync() → [Active] → CancelChoreAsync() → [Canceled]
                                        │
                                        └─→ ReplaceChoreWithShiftAsync() → [Replaced by Shift]
```

### 4.4 Conflict Detection

| Conflict Type | Detection | Resolution |
|---------------|-----------|------------|
| Shift conflict | `HasShiftOnDateAsync()` | Replace shift with chore (transactional) |
| Vacation conflict | `GetVacationConflictDetailsAsync()` | Block unless `forceAssign=true` |
| Duplicate chore | Unique index on (CompanyId, UserId, Date, CanceledAt IS NULL) | Blocked by DB |

### 4.5 Special Behaviors

| Feature | Behavior |
|---------|----------|
| Soft delete | Uses `CanceledAt` timestamp, never hard deleted |
| Force assign | Bypasses vacation check, logs warning |
| Audit trail | All mutations logged to AuditLog |
| Notifications | Assignee notified on create/cancel |
| Calendar integration | Appears in Month/Week/Day views |
| Team calendar | Shows as lowest priority (after Vacation > OnDuty > Shift) |

---

## 5. Shared Concepts & Constraints

### 5.1 Multi-Tenancy Implementation

```csharp
// CompanyIdInterceptor auto-sets CompanyId on insert
public class CompanyIdInterceptor : SaveChangesInterceptor
{
    // Intercepts SaveChanges/SaveChangesAsync
    // For entities implementing IBelongsToCompany:
    // - If CompanyId == 0, sets from TenantResolver
    // - Logs warning if no tenant context available
}
```

**Feature Flag:** `Features:EnforceCompanyScope` (default: false in dev)

### 5.2 Organizational Hierarchy

```
Project (Shifty)
    └── Area (Hir, Yosh, etc.)
        └── Molecule (Oren, Dvash, etc.)
            ├── Company (Workforce users)
            │   └── User (with JobType: Alhut, Text, BR, Hakam)
            └── Department (Tech users)
                └── User
```

**Key Insight:** JobType is a USER attribute, not an organizational level.

### 5.3 Shift Groupings

Shift Groupings are sub-divisions within a Workforce Molecule:
- Example: Oren Molecule has groupings Tzafon, Darom, Tacti
- Used for labeling shifts, not access control
- Scope: Molecule-level

### 5.4 Date/Time Conventions

| Entity | Time Handling |
|--------|---------------|
| Shifts | `StartTime` / `EndTime` (TimeOnly) |
| Chores | `Date` only (DateOnly) |
| On-Duty | `Date` only (DateOnly) |
| Vacations | `StartDate` / `EndDate` (DateOnly) |

**Timezone:** All dates stored as UTC, displayed in user's local time.

### 5.5 Soft Delete Pattern

Used by: Chore, OnDuty, TimeOffRequest

```csharp
// Query active records
Where(x => x.CanceledAt == null)

// Soft delete
entity.CanceledAt = DateTime.UtcNow;
entity.CanceledBy = currentUserId;
```

### 5.6 Global vs Tenant-Scoped Entities

| Global (No CompanyId) | Tenant-Scoped (Has CompanyId) |
|-----------------------|-------------------------------|
| OnDuty | ShiftInstance |
| OnDutyTypeConfig | ShiftAssignment |
| Project | Chore |
| Area | TimeOffRequest |
| Molecule | TeamCalendar |
| JobType | AppUser |
| GrantType | Grant |
| RoleTemplate | Company |

---

## 6. Permission Matrix

### 6.1 Calendar Permissions

| Calendar | Employee | Trainee | Assigner | Manager | Director | Owner |
|----------|----------|---------|----------|---------|----------|-------|
| Month/Week/Day (Read) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Table (Read) | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| Table (Edit) | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| Personal Timeline | ✅ (own) | ✅ (own) | ✅ (own) | ✅ (own) | ✅ (own) | ✅ (own) |
| Team Calendars | ✅ (own) | ✅ (own) | ✅ (own) | ✅ (own) | ✅ (own) | ✅ (own) |
| Chores Calendar (Read) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Chores Calendar (Edit) | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Public On-Duty (Read) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Public On-Duty (Edit) | ❌ | ❌ | ❌ | ❌ | ❌ | ✅* |

*AreaAdmin can also edit On-Duty via grants

### 6.2 Chore Permissions

| Action | Employee | Trainee | Assigner | Manager | Director | Owner |
|--------|----------|---------|----------|---------|----------|-------|
| View all chores | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Create chore | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Cancel own created | ❌ | ❌ | ⚠️ | ✅ | ✅ | ✅ |
| Cancel any chore | ❌ | ❌ | ❌ | ✅ (company) | ✅ (managed) | ✅ |
| Assign to self | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Assign to others | ❌ | ❌ | ✅ (company) | ✅ (company) | ✅ (managed) | ✅ |
| Assign to Director | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

### 6.3 Shift Permissions

| Action | Employee | Trainee | Manager | Director | Owner |
|--------|----------|---------|---------|----------|-------|
| View own shifts | ✅ | ✅ | ✅ | ✅ | ✅ |
| View company shifts | ❌ | ❌ | ✅ | ✅ | ✅ |
| Create shift instance | ❌ | ❌ | ✅ | ✅ | ✅ |
| Assign to shift | ❌ | ❌ | ✅ | ✅ | ✅ |
| Remove from shift | ❌ | ❌ | ✅ | ✅ | ✅ |
| Delete shift instance | ❌ | ❌ | ✅ | ✅ | ✅ |
| Create shift type | ❌ | ❌ | ✅ | ✅ | ✅ |

### 6.4 On-Duty Permissions

| Action | Employee | Manager | AreaAdmin | Owner |
|--------|----------|---------|-----------|-------|
| View all on-duty | ✅ | ✅ | ✅ | ✅ |
| Create on-duty | ❌ | ❌ | ✅ | ✅ |
| Cancel on-duty | ❌ | ❌ | ✅ | ✅ |
| Configure types | ❌ | ❌ | ❌ | ✅ |

### 6.5 Grant-Based Permissions (V3)

| Grant Key | Default Scope | Description |
|-----------|---------------|-------------|
| ViewShifts | Company | View shifts in scope |
| ViewAllShifts | Molecule | View all shifts in molecule |
| AssignAlhutShifts | Company | Assign Alhut shift type |
| AssignTextShifts | Company | Assign Text shift type |
| AssignBRShifts | Molecule | Assign BR shift type |
| ViewChores | Molecule | View chores |
| AssignChores | Molecule | Assign chores |
| ViewDuties | Area | View on-duty assignments |
| AssignHakamDuties | Area | Assign Hakam on-duty |
| AssignKatzinDuties | Area | Assign Katzin on-duty |
| ViewVacations | Company | View vacation requests |
| ApproveVacations | Company | Approve/reject vacations |
| ViewUsers | Company | View user list |
| EditUsers | Company | Edit user profiles |
| AssignRoles | Company | Assign role templates |
| AdminAccess | Project | Owner-level access |

---

## 7. Known Gaps & Gotchas

### 7.1 Implementation Gaps

| Feature | Status | Impact |
|---------|--------|--------|
| V3 Role Template auto-grants | Designed, not wired | Roles don't auto-grant permissions |
| Military Rank system | **Implemented (v2.8.4)** | Katzin eligibility enforced via feature flag |
| Setup Tasks | Designed, not implemented | No guided onboarding |
| Circle/Friends | Designed, not implemented | No cross-molecule visibility |
| Grant scope inheritance | Partial | Some edge cases not handled |

### 7.2 Developer Gotchas

| Issue | Description | Mitigation |
|-------|-------------|------------|
| OnDuty global scope | OnDuty intentionally bypasses tenant filter | Use `IgnoreQueryFilters()` explicitly |
| Owner company selector | Owner's "current company" differs from their actual CompanyId | Check `ITenantResolver.GetCurrentTenantId()` |
| JobType is user attribute | JobType is NOT an organizational level | Don't treat as hierarchy |
| Directors can't have chores | Hardcoded business rule | Check `CanUserManageChoreForAssigneeAsync()` |
| Soft delete pattern | CanceledAt != null means deleted | Always filter `CanceledAt == null` |
| Shift vs Chore conflict | Mutually exclusive on same date | Use ReplaceShiftWithChore/vice versa |

### 7.3 Security Considerations

| Risk | Current Mitigation | Notes |
|------|-------------------|-------|
| Cross-tenant data leak | Query filters + explicit checks | Test with multi-company scenarios |
| Privilege escalation | Role-based policies | V3 grant system more granular |
| Unauthorized assignment | Service-level permission checks | Always call CanUser* methods |
| API without auth | ApiAuthenticationMiddleware | Internal endpoints need whitelist |

### 7.4 Performance Considerations

| Scenario | Issue | Mitigation |
|----------|-------|------------|
| Large calendar queries | Many joins across shift/chore/onduty | Indexed by CompanyId+Date |
| Scope resolution | Multiple hierarchy traversals | Cache hierarchy paths |
| Grant checking | Multiple DB queries per check | Consider caching user grants |
| Team calendar aggregation | N+1 query potential | Batch load with TeamCalendarEventAggregator |

### 7.5 Data Integrity Constraints

| Constraint | Enforcement | Notes |
|------------|-------------|-------|
| Unique active chore per user/date | DB unique index | CanceledAt IS NULL condition |
| CompanyId required | IBelongsToCompany + interceptor | May log warning if missing |
| Soft delete audit trail | CanceledBy + CanceledAt | Never hard delete |
| Shift assignment uniqueness | DB unique index | One user per shift instance |

---

## Appendix A: Key File Locations

| Component | Path |
|-----------|------|
| Calendar Pages | `Pages/Calendar/` |
| Chore Service | `Services/ChoreService.cs` |
| Grant Service | `Services/GrantService.cs` |
| Role Templates | `Data/SeedData/RoleTemplateSeed.cs` |
| Grant Types | `Data/SeedData/GrantTypeSeed.cs` |
| Tenant Resolver | `Services/TenantResolver.cs` |
| Company Interceptor | `Data/CompanyIdInterceptor.cs` |
| Hierarchy Service | `Services/HierarchyService.cs` |
| Scope Filter Service | `Services/ScopeFilterService.cs` |
| Military Rank Enum | `Models/Support/MilitaryRank.cs` |
| Military Rank Extensions | `Models/Support/MilitaryRankExtensions.cs` |
| On-Duty Service (Eligibility) | `Services/OnDutyService.cs` |

## Appendix B: Related Documentation

- `docs/TERMINOLOGY.md` - UI vs code terminology mapping
- `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` - V3 design spec
- `docs/genesis/` - Original architecture documentation
