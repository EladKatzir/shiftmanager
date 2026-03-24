# Grants & Permissions System — Complete Deep Dive

> **Last Updated:** 2026-02-25
> **Total Grant Types:** 125
> **Total Role Templates:** 11 (system) + custom
> **Supersedes:** `GRANTS_AND_ROLES.md`, `GRANT-KEY-REFERENCE.md` (both outdated at 125 grants)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [What Are Grants?](#2-what-are-grants)
3. [All 125 Grant Types](#3-all-125-grant-types)
4. [UserRole Enum](#4-userrole-enum)
5. [Role Templates](#5-role-templates)
6. [All 11 System Role Templates](#6-all-11-system-role-templates)
7. [Who Can Create & Assign Roles](#7-who-can-create--assign-roles)
8. [Seed Data](#8-seed-data)
9. [How Permissions Are Enforced](#9-how-permissions-are-enforced)
10. [Scope Matching Logic](#10-scope-matching-logic)
11. [Grant Lifecycle](#11-grant-lifecycle)
12. [Delegation (CanGive)](#12-delegation-cangive)
13. [Verification & Repair](#13-verification--repair)
14. [Security Design Points](#14-security-design-points)
15. [Key Files Reference](#15-key-files-reference)

---

## 1. Architecture Overview

ShiftManager implements a **grant-based authorization model** (closer to ABAC) rather than traditional role-based access control (RBAC).

**Key principle:** Roles don't directly control access. They serve as **templates** that auto-provision individual grants. This allows fine-grained, scope-aware, delegatable permissions.

```
RoleTemplate (e.g., "BRDirector")
    │
    ├── RoleTemplateGrant (AssignBRShifts, ScopeMode=ETM, CanGive=true)
    ├── RoleTemplateGrant (ApproveVacations, ScopeMode=SAR, TargetJobTypeId=BR)
    └── RoleTemplateGrant (ApproveVacations, ScopeMode=SAR, TargetJobTypeId=Hakam)
         │
         ▼  (on role assignment or login)
    Grant (userId=42, grantTypeId=23, companyId=5, jobTypeId=2, CanOwn=true, CanGive=true)
```

All enforcement converges on a single source of truth: `GrantService.HasGrantWithScopeAsync()`.

---

## 2. What Are Grants?

A **Grant** is a single permission record linking a **user** to a **grant type** at a specific **hierarchical scope**.

### Grant Entity (`Models/Grant.cs`)

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | int | Primary key |
| `UserId` | int | Who has the grant |
| `GrantTypeId` | int | What permission (1 of 125) |
| `ProjectId` | int? | Scope: entire project |
| `AreaId` | int? | Scope: area |
| `MoleculeId` | int? | Scope: molecule |
| `DepartmentId` | int? | Scope: department |
| `CompanyId` | int? | Scope: company |
| `JobTypeId` | int? | Scope: job type (cross-cutting) |
| `CanOwn` | bool | Can **perform** the action |
| `CanGive` | bool | Can **delegate** this grant to others |
| `IsAutoGrant` | bool | `true` = from role template, `false` = manual |
| `GrantedByUserId` | int? | Audit: who assigned it |
| `GrantedAt` | DateTime | Audit: when assigned |
| `Notes` | string? | Audit: context/reason |

### GrantType Entity (`Models/GrantType.cs`)

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | int | Primary key |
| `Key` | string | Unique identifier (e.g., `"AssignAlhutShifts"`) |
| `NameKey` | string | Localization key for display name |
| `DescriptionKey` | string | Localization key for description |
| `Category` | GrantCategory | Grouping (Shift, Duty, Chore, etc.) |
| `DefaultScope` | GrantScopeLevel | Recommended scope level |
| `IsSystem` | bool | `true` = built-in, `false` = custom |
| `IsActive` | bool | Can be disabled |

### Scope Hierarchy (broadest → narrowest)

```
Project  ──covers──▶  Area  ──covers──▶  Molecule  ──covers──▶  Company / Department
                                                                       │
                                                                 JobType (cross-cutting)
```

A grant scoped at **Project** level covers everything below it. A grant scoped at **Company** level only works for that specific company.

**Self-scoped grants** (all scope fields null) only apply to the user's **own resources** — validated by `targetUserId == userId`.

### Supporting Enums

```csharp
public enum GrantCategory
{
    Shift = 0, Duty = 1, Chore = 2, Vacation = 3, Swap = 4,
    UserManagement = 5, GrantManagement = 6, Hierarchy = 7,
    Settings = 8, Analytics = 9, Email = 10, System = 11, Custom = 99
}

public enum GrantScopeLevel
{
    Self = 0, Company = 1, Department = 2, Molecule = 3, Area = 4, Project = 5
}

public enum GrantScopeMode
{
    SameAsRole = 0,        // Grant scope = CompanyId + DepartmentId only
    ExpandToMolecule = 1,  // Expand to molecule level
    ExpandToArea = 2,      // Expand to area level
    Custom = 3,            // Explicit scope
    ExpandToProject = 4    // Expand to project level (Owner)
}
```

---

## 3. All 125 Grant Types

### Shift Grants (IDs 1-12)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 1 | `ViewShifts` | Company | View shifts |
| 2 | `ViewAllShifts` | Molecule | View all shifts across companies |
| 3 | `AssignAlhutShifts` | Company | Assign Alhut shifts |
| 4 | `AssignTextShifts` | Company | Assign Text shifts |
| 5 | `AssignBRShifts` | Molecule | Assign BR shifts |
| 6 | `AssignTechShifts` | Department | Assign Tech shifts |
| 7 | `EditShiftPrograms` | Company | Edit shift programs |
| 8 | `CreateShiftPrograms` | Company | Create shift programs |
| 9 | `DeleteShiftPrograms` | Company | Delete shift programs |
| 10 | `EditShiftTypes` | Company | Edit shift types/blueprints |
| 11 | `CreateShiftTypes` | Company | Create shift types/blueprints |

### Duty Grants (IDs 13-16)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 13 | `ViewDuties` | Area | View duties |
| 14 | `AssignHakamDuties` | Area | Assign Hakam duties |
| 15 | `AssignKatzinDuties` | Area | Assign Katzin duties |
| 16 | `EditDutyPrograms` | Area | Edit duty programs |

### Chore Grants (IDs 17-20)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 17 | `ViewChores` | Molecule | View chores |
| 18 | `AssignChores` | Molecule | Assign chores |
| 19 | `EditChoreTypes` | Molecule | Edit chore types |
| 20 | `CreateChoreTypes` | Molecule | Create chore types |

### Vacation Grants (IDs 21-25)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 21 | `ViewVacations` | Company | View vacations |
| 22 | `RequestVacation` | Self | Request own vacation |
| 23 | `ApproveVacations` | Company | Approve vacations |
| 24 | `OverrideVacationLimits` | Company | Override vacation limits |
| 25 | `ApproveExtendedLeave` | Company | Approve extended leave |

### Swap Grants (IDs 26-28)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 26 | `RequestSwap` | Self | Request swap |
| 27 | `ApproveSwaps` | Company | Approve swaps |
| 28 | `InitiateSwap` | Company | Initiate swap for others |

### User Management Grants (IDs 29-35)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 29 | `ViewUsers` | Company | View users |
| 30 | `EditUsers` | Company | Edit users |
| 31 | `CreateUsers` | Company | Create users |
| 32 | `DeactivateUsers` | Company | Deactivate users |
| 33 | `ResetPasswords` | Company | Reset passwords |
| 34 | `AssignJobTypes` | Company | Assign job types |
| 35 | `ViewAllUsers` | Molecule | View all users across companies |

### Grant Management Grants (IDs 36-39)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 36 | `ViewGrants` | Company | View grants |
| 37 | `AssignGrants` | Company | Assign grants |
| 38 | `RevokeGrants` | Company | Revoke grants |
| 39 | `AssignRoles` | Company | Assign role templates |

### Hierarchy Grants (IDs 40-48)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 40 | `ViewHierarchy` | Company | View hierarchy |
| 41 | `EditCompany` | Company | Edit company |
| 42 | `EditMolecule` | Molecule | Edit molecule |
| 43 | `EditArea` | Area | Edit area |
| 44 | `CreateCompany` | Molecule | Create company |
| 45 | `CreateMolecule` | Area | Create molecule |
| 46 | `ManageShiftGroupings` | Molecule | Manage shift groupings |
| 47 | `ManageJobTypes` | Area | Manage job types |
| 48 | `ManageDepartments` | Molecule | Manage departments |

### Settings Grants (IDs 49-52)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 49 | `ViewSettings` | Company | View settings |
| 50 | `EditCompanySettings` | Company | Edit company settings |
| 51 | `EditMoleculeSettings` | Molecule | Edit molecule settings |
| 52 | `EditAreaSettings` | Area | Edit area settings |

### Analytics Grants (IDs 53-55)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 53 | `ViewAnalytics` | Company | View analytics |
| 54 | `ViewReports` | Company | View reports |
| 55 | `ExportData` | Company | Export data |

### Email Grants (IDs 56-57)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 56 | `SendNotifications` | Company | Send notifications |
| 57 | `ConfigureEmailSettings` | Company | Configure email settings |

### System Grants (IDs 58-61)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 58 | `AdminAccess` | Project | System admin access |
| 59 | `SystemConfiguration` | Project | System configuration |
| 60 | `ViewAuditLog` | Company | View audit log |
| 61 | `ManageApiKeys` | Company | Manage API keys |

### Shift Calendar View Grants (IDs 62-65)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 62 | `ViewAlhutShiftCalendar` | Company | View Alhut calendar |
| 63 | `ViewTextShiftCalendar` | Company | View Text calendar |
| 64 | `ViewBRShiftCalendar` | Molecule | View BR calendar |
| 65 | `ViewHakamShiftCalendar` | Area | View Hakam calendar |

### Shift Eligibility Grants (IDs 66-69)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 66 | `CanBeAssignedAlhutShifts` | Self | Eligible for Alhut shifts |
| 67 | `CanBeAssignedTextShifts` | Self | Eligible for Text shifts |
| 68 | `CanBeAssignedBRShifts` | Self | Eligible for BR shifts |
| 69 | `CanBeAssignedHakamShifts` | Self | Eligible for Hakam shifts |

### Blueprint/Program Management by Type (IDs 70-77)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 70 | `ManageAlhutBlueprints` | Molecule | Manage Alhut blueprints |
| 71 | `ManageAlhutPrograms` | Molecule | Manage Alhut programs |
| 72 | `ManageTextBlueprints` | Molecule | Manage Text blueprints |
| 73 | `ManageTextPrograms` | Molecule | Manage Text programs |
| 74 | `ManageBRBlueprints` | Molecule | Manage BR blueprints |
| 75 | `ManageBRPrograms` | Molecule | Manage BR programs |
| 76 | `ManageHakamBlueprints` | Area | Manage Hakam blueprints |
| 77 | `ManageHakamPrograms` | Area | Manage Hakam programs |

### Tech Department Calendar Grants (IDs 78-81)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 78 | `ViewHanavaCalendar` | Department | View Hanava calendar |
| 79 | `ViewDeltaCalendar` | Department | View Delta calendar |
| 80 | `ViewYekevCalendar` | Department | View Yekev calendar |
| 81 | `ViewMoviltechCalendar` | Department | View Moviltech calendar |

### Tech Department Assignment Grants (IDs 82-85)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 82 | `AssignHanavaShifts` | Department | Assign Hanava shifts |
| 83 | `AssignDeltaShifts` | Department | Assign Delta shifts |
| 84 | `AssignYekevShifts` | Department | Assign Yekev shifts |
| 85 | `AssignMoviltechShifts` | Department | Assign Moviltech shifts |

### Tech Department Blueprint/Program Grants (IDs 86-93)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 86 | `ManageHanavaBlueprints` | Department | Manage Hanava blueprints |
| 87 | `ManageHanavaPrograms` | Department | Manage Hanava programs |
| 88 | `ManageDeltaBlueprints` | Department | Manage Delta blueprints |
| 89 | `ManageDeltaPrograms` | Department | Manage Delta programs |
| 90 | `ManageYekevBlueprints` | Department | Manage Yekev blueprints |
| 91 | `ManageYekevPrograms` | Department | Manage Yekev programs |
| 92 | `ManageMoviltechBlueprints` | Department | Manage Moviltech blueprints |
| 93 | `ManageMoviltechPrograms` | Department | Manage Moviltech programs |

### Tech Eligibility Grants (IDs 94-97)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 94 | `CanBeAssignedHanava` | Self | Eligible for Hanava shifts |
| 95 | `CanBeAssignedDelta` | Self | Eligible for Delta shifts |
| 96 | `CanBeAssignedYekev` | Self | Eligible for Yekev shifts |
| 97 | `CanBeAssignedMoviltech` | Self | Eligible for Moviltech shifts |

### Helper Molecule Grants — Shiklut (IDs 98-102)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 98 | `ViewShiklutCalendar` | Molecule | View Shiklut calendar |
| 99 | `AssignShiklutChores` | Molecule | Assign Shiklut chores |
| 100 | `ManageShiklutBlueprints` | Molecule | Manage Shiklut blueprints |
| 101 | `ManageShiklutPrograms` | Molecule | Manage Shiklut programs |
| 102 | `CanBeAssignedShiklut` | Self | Eligible for Shiklut |

### Helper Molecule Grants — NOC (IDs 103-107)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 103 | `ViewNOCCalendar` | Molecule | View NOC calendar |
| 104 | `AssignNOCChores` | Molecule | Assign NOC chores |
| 105 | `ManageNOCBlueprints` | Molecule | Manage NOC blueprints |
| 106 | `ManageNOCPrograms` | Molecule | Manage NOC programs |
| 107 | `CanBeAssignedNOC` | Self | Eligible for NOC |

### Katzin Duty Management (IDs 108-109)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 108 | `ManageKatzinBlueprints` | Area | Manage Katzin blueprints |
| 109 | `ManageKatzinPrograms` | Area | Manage Katzin programs |

### Calendar Grants (IDs 110-112)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 110 | `ManageShiftCapacity` | Molecule | Manage shift capacity |
| 111 | `WriteOverviewNotes` | Company | Write overview notes |
| 112 | `ManageOnDutyTypes` | Area | Manage OnDuty types |

### Navigation Grants (IDs 113-116)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 113 | `AccessAdminNavigation` | Company | Access admin navigation |
| 114 | `DirectorHubAccess` | Area | Director hub access |
| 115 | `ManagerHomeAccess` | Company | Manager home access |
| 116 | `ViewCompanyCalendar` | Company | View company calendar |

### Join Request Grants (IDs 117-119)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 117 | `ManageJoinRequests` | Company | Manage join requests |
| 118 | `ViewCompanyUsers` | Company | View company users |
| 119 | `EditCompanyUsers` | Company | Edit company users |

### Dynamic Role Template Grants (IDs 120-123)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 120 | `ManageAnnouncements` | Company | Manage announcements |
| 121 | `ViewSystemAlerts` | Company | View system alerts |
| 122 | `ViewAllAreas` | Area | View all areas |
| 123 | `ManageOnDuty` | Area | Manage on-duty |

### Hierarchy Reorder (ID 124)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 124 | `ReorderHierarchy` | Project | Reorder hierarchy |

---

## 4. UserRole Enum

The `UserRole` enum defines 7 roles. These are **NOT directly used for access control** — they map to role templates which provision grants.

| Value | Role | Purpose |
|-------|------|---------|
| 0 | **Owner** | System administrator — all 125 grants at Project scope |
| 1 | **Manager** | Company-level management (AlhutLead, TextLead, BRDirector, MoleculeAdmin, DepartmentLead) |
| 2 | **Employee** | Standard user — view + self-service grants |
| 3 | **Director** | Molecule-level oversight (AlhutDirector, TextDirector) |
| 4 | **Trainee** | Like Employee but no swap capability |
| 5 | **Assigner** | Molecule-scoped chore assignment only |
| 6 | **AreaAdmin** | Area-level administration (nearly Owner-level within their area) |

> **Note:** There is no "Admin" role. The `AdminAccess` grant (ID 58) provides Owner-level system access. Even "admin" capabilities are individually grantable and auditable.

---

## 5. Role Templates

A **RoleTemplate** is a named bundle of auto-grants provisioned when a user is assigned that role.

### RoleTemplate Entity (`Models/RoleTemplate.cs`)

| Field | Type | Purpose |
|-------|------|---------|
| `Key` | string | Unique identifier (e.g., `"BRDirector"`) |
| `DerivedUserRole` | UserRole | Maps to enum |
| `ScopeLevel` | RoleScopeLevel | How scope is determined |
| `IsSystem` | bool | `true` = built-in (11 templates) |
| `DisplayNameEN/HE` | string? | Free-text display names for custom roles |
| `CanBeAssignedByDefault` | bool | Controls assignability |
| `IsVisibleInSignup` | bool | Controls signup form visibility |
| `SortOrder` | int | UI ordering |
| `AutoGrants` | List | `RoleTemplateGrant` records |

### RoleTemplateGrant Entity (`Models/RoleTemplateGrant.cs`)

| Field | Type | Purpose |
|-------|------|---------|
| `RoleTemplateId` | int | Which template |
| `GrantTypeId` | int | Which grant type |
| `CanOwn` | bool | User gets the grant |
| `CanGive` | bool | User can delegate |
| `ScopeMode` | GrantScopeMode | How scope is resolved (SAR/ETM/ETA/ETP) |
| `TargetJobTypeId` | int? | Explicit JobType restriction |
| `UseOwnJobType` | bool | Resolve to user's own JobType at login |
| `IsOverride` | bool | Owner manually modified default |

### ScopeMode Resolution

| Mode | Abbreviation | Extracts | Example Use |
|------|-------------|----------|-------------|
| `SameAsRole` | SAR | CompanyId + DepartmentId only | Employee viewing own shifts |
| `ExpandToMolecule` | ETM | MoleculeId only | Director managing all companies in molecule |
| `ExpandToArea` | ETA | AreaId only | AreaAdmin managing entire area |
| `ExpandToProject` | ETP | ProjectId only | Owner managing everything |
| `Custom` | — | Full hierarchy | Special cases |

> **Security-critical:** `DetermineEffectiveScope()` extracts exactly ONE hierarchy level per ScopeMode. If it extracted the full hierarchy, a Project-scoped grant would also match Company-scoped checks, causing cascade over-granting.

---

## 6. All 11 System Role Templates

### Employee (ID=1)
- **Scope:** Implicit | **UserRole:** Employee | **SortOrder:** 100
- **19 grants** (all SAR/company-scoped):
  - Base: ViewShifts, ViewChores, ViewDuties, ViewVacations, RequestVacation, RequestSwap, ViewCompanyCalendar, ViewCompanyUsers, ViewGrants, ViewHierarchy, WriteOverviewNotes
  - Shift calendars: ViewAlhutShiftCalendar, ViewTextShiftCalendar, ViewBRShiftCalendar, ViewHakamShiftCalendar
  - Eligibility (UseOwnJobType): CanBeAssignedAlhutShifts, CanBeAssignedTextShifts, CanBeAssignedBRShifts, CanBeAssignedHakamShifts

### Trainee (ID=12)
- **Scope:** Implicit | **UserRole:** Trainee | **SortOrder:** 101
- **18 grants** — same as Employee EXCEPT no `RequestSwap`

### Assigner (ID=8)
- **Scope:** Molecule | **UserRole:** Assigner | **SortOrder:** 30 | **Not visible in signup**
- **20 grants** — all Employee grants + `AssignChores` at ETM/molecule-wide

### AlhutLead (ID=3)
- **Scope:** CompanyJobType | **UserRole:** Manager | **SortOrder:** 20
- **40 grants** — Employee base + Alhut-specific management:
  - `AssignAlhutShifts` (ETM + UseOwnJobType), `ManageAlhutBlueprints`, `ManageAlhutPrograms`
  - `EditShiftTypes`, `ApproveVacations`, `ApproveSwaps`, `ManageJoinRequests`
  - `AssignGrants`, `AssignRoles`, `AccessAdminNavigation`, `ManagerHomeAccess`

### TextLead (ID=4)
- **Scope:** CompanyJobType | **UserRole:** Manager | **SortOrder:** 21
- **40 grants** — identical structure to AlhutLead but Text-specific

### BRDirector (ID=2)
- **Scope:** Company | **UserRole:** Manager | **SortOrder:** 10
- **50 grants** — Employee base + BR/Hakam management:
  - `AssignBRShifts` (ETM, CanGive), `ManageBRBlueprints`, `ManageHakamBlueprints`
  - `ApproveVacations`: **TWO entries** — one for BR JobType, one for Hakam JobType (sentinel IDs)
  - `ApproveExtendedLeave`: **TWO entries** similarly
  - `EditCompany`

### AlhutDirector (ID=5)
- **Scope:** MoleculeJobType | **UserRole:** Director | **SortOrder:** 5
- **49 grants** — AlhutLead grants widened to ETM + director extras:
  - `AssignAlhutShifts` (ETM, CanGive, UseOwnJobType)
  - `DirectorHubAccess`, `ViewAllUsers`

### TextDirector (ID=6)
- **Scope:** MoleculeJobType | **UserRole:** Director | **SortOrder:** 6
- **49 grants** — identical to AlhutDirector but Text-specific

### MoleculeAdmin (ID=7)
- **Scope:** Molecule | **UserRole:** Manager | **SortOrder:** 3
- **87 grants** — merges BRDirector + Alhut/TextDirector at ETM:
  - All shift types (Alhut, Text, BR, all with CanGive)
  - All tech grants (Hanava, Delta, Yekev, Moviltech)
  - `EditCompany`, full user management

### AreaAdmin (ID=10)
- **Scope:** Area | **UserRole:** AreaAdmin | **SortOrder:** 2
- **102 grants** — merges all Directors + MoleculeAdmin at ETA:
  - ALL shift assignment with CanGive
  - `EditArea`, `CreateCompany`, `CreateMolecule`, `ManageJobTypes`, `ManageDepartments`
  - `DirectorHubAccess`, `ManageOnDuty`

### Owner (ID=11)
- **Scope:** Project | **UserRole:** Owner | **SortOrder:** 1 | **Not visible in signup**
- **123 grants** — ALL grant types at ETP with CanGive=true
  - Self-scoped (SAR): RequestVacation, RequestSwap, CanBeAssigned* eligibilities
  - Everything else: @ ETP (project-wide) + CanGive

---

## 7. Who Can Create & Assign Roles

### Creating New Role Templates

| Who | Where | Requirement |
|-----|-------|-------------|
| **Owner only** | `Pages/Owner/Hub/RoleTemplates/Create.cshtml.cs` | `[Authorize(Policy = "Grant:AdminAccess")]` |

Custom role templates:
- Always `IsSystem=false`
- Start with **zero grants** (added manually via Edit page)
- Require unique alphanumeric key
- System templates have locked `ScopeLevel` and `DerivedUserRole`

### Assigning Role Templates to Users

| Who | Where | Requirement |
|-----|-------|-------------|
| Anyone with `AssignRoles` grant | `Pages/Admin/Organization/Roles/Assign.cshtml.cs` | `[Authorize(Policy = "Grant:AssignRoles")]` |

Assignment flow:
1. Select user, role, scope
2. Validate scope matches role's ScopeLevel
3. Create `UserRoleAssignment` record
4. Call `ApplyAutoGrantsAsync()` — provisions all template grants
5. User immediately has all permissions

### Editing Role Templates

| Who | Where | Requirement |
|-----|-------|-------------|
| **Owner only** | `Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs` | `[Authorize(Policy = "Grant:AdminAccess")]` |

---

## 8. Seed Data

All seed data in `Data/SeedData/`:

| File | What's Seeded |
|------|---------------|
| `GrantTypeSeed.cs` | All 125 grant type definitions |
| `RoleTemplateSeed.cs` | All 11 system role templates + auto-grant mappings |

BRDirector uses **sentinel JobType IDs** (-1 for BR, -2 for Hakam) that are resolved to actual DB IDs during `Program.cs` startup.

---

## 9. How Permissions Are Enforced

There are **7 enforcement layers**, all converging on `GrantService.HasGrantWithScopeAsync()`.

### Layer 1: Page-Level `[Authorize]` Attributes

```csharp
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class TableModel : PageModel { }
```

**35+ pages** with grant enforcement:

| Grant Required | Pages |
|----------------|-------|
| `ManagerHomeAccess` | Calendar/Table, Admin/Users, Assignments/Manage, Requests |
| `AdminAccess` | Owner/Hub/* pages |
| `AssignRoles` | Admin/Organization/Roles/* |
| `ViewGrants` | Admin/Organization/Grants/Index |
| `AssignGrants` | Admin/Organization/Grants/Assign |
| `EditArea` | Admin/Organization/Areas/*, Projects/* |
| `ManageDepartments` | Admin/Organization/Departments/* |
| `EditCompany` | Admin/Companies |
| `ManageOnDuty` | DutyRotation/* |
| `SystemConfiguration` | ApprovalRules |
| `ReorderHierarchy` | Api/Hierarchy/Reorder |
| `ManageAnnouncements` | Announcements |

### Layer 2: Dynamic Policy Provider (`GrantPolicyProvider.cs`)

Intercepts any `"Grant:*"` policy string, extracts the grant key, and dynamically creates an `AuthorizationPolicy` with `GrantRequirement`. No need to register each of 125 grants manually.

### Layer 3: Authorization Handler (`GrantAuthorizationHandler.cs`)

Builds scope from `ICurrentUserService` claims (ProjectId, AreaId, MoleculeId, CompanyId) and calls `HasGrantAsync()`.

### Layer 4: In-Handler Grant Checks

Post handlers do per-action validation:
```csharp
if (!await _grantService.HasGrantForCompanyAsync(userId, "AssignAlhutShifts", targetCompanyId))
    return Forbid();
```

### Layer 5: View-Level Tag Helper (`<require-grant>`)

Conditionally renders UI elements:
```html
<require-grant key="AssignAlhutShifts">
    <button>Assign Shift</button>
</require-grant>

<!-- OR logic -->
<require-grant key="ViewShifts,EditShifts" mode="any">
    <div>Shift access</div>
</require-grant>

<!-- Negate: hide if user HAS grant -->
<require-grant key="ViewAnalytics" negate="true">
    <p>You don't have analytics access</p>
</require-grant>
```

### Layer 6: API Key Authentication (`ApiAuthenticationMiddleware.cs`)

External API requests use `X-API-Key` header with scope mapping:
```
GET  /api/v1/users     → "user:read"
POST /api/v1/users     → "user:write"
GET  /api/v1/shifts    → "shift:read"
POST /api/v1/shifts    → "shift:write"
```

### Layer 7: Default-Deny (`Program.cs`)

```csharp
options.Conventions.AuthorizeFolder("/");  // ALL pages require auth by default
options.Conventions.AllowAnonymousToPage("/Auth/Login");
options.Conventions.AllowAnonymousToPage("/Auth/Signup");
// ... explicit whitelist only
```

### Authorization Flow Example

```
User clicks "Assign Shift" on Calendar/Table

1. Page Load: [Authorize(Policy = "Grant:ManagerHomeAccess")]
   → GrantPolicyProvider creates GrantRequirement("ManagerHomeAccess")
   → GrantAuthorizationHandler builds scope from claims
   → HasGrantAsync(userId, "ManagerHomeAccess", scope)
   → ✓ Page loads

2. POST Action: OnPostAssignAsync(...)
   → HasGrantForCompanyAsync(userId, "AssignAlhutShifts", targetCompanyId)
   → Validates target company is within user's grant scope
   → ✓ Assignment saved

3. View Render: <require-grant key="AssignAlhutShifts">
   → RequireGrantTagHelper checks HasGrantAsync
   → ✓ Button rendered (or suppressed)
```

---

## 10. Scope Matching Logic

`HasGrantWithScopeAsync()` checks grants in priority order:

```
1. Self-scoped (all null)  → valid ONLY if targetUserId == userId
2. ProjectId match         → covers everything below (Area, Molecule, Company, Dept, JobType)
3. AreaId match            → covers Molecule, Company, Department within that area
4. MoleculeId match        → covers Company, Department within that molecule
5. DepartmentId match      → exact match only
6. CompanyId (no JobType)  → exact match only
7. JobTypeId (± CompanyId) → exact match; CompanyId optional if also specified
```

**Per-request caching** prevents N+1 queries:
- `_hierarchyCache<userId, UserHierarchyContext>` — cached per request
- `_grantTypeCache<key, GrantType>` — cached per request

### GetAccessibleCompanyIdsForGrantAsync

Returns all company IDs a user can access for a specific grant, cascading from broadest to narrowest:
- Project scope → all companies in project
- Area scope → all companies in area's molecules
- Molecule scope → all companies in molecule
- Company scope → just that company
- Self scope → user's own company

---

## 11. Grant Lifecycle

| Event | What Happens | Method |
|-------|-------------|--------|
| **User Created** | Template grants provisioned | `AssignRoleTemplateGrantsAsync()` |
| **User Logs In** | Missing grants added, stale removed | `ApplyAutoGrantsAsync()` |
| **Role Changed** | Old auto-grants removed, new ones applied | `RemoveAutoGrantsAsync()` + `AssignRoleTemplateGrantsAsync()` |
| **Manual Grant** | Single grant inserted with CanGive check | `GrantAsync()` |
| **Grant Revoked** | Immediate removal + audit log | `RevokeAsync()` |
| **User Deactivated** | Grants **kept** (can reactivate) but user can't log in | — |
| **User Deleted** | All grants hard-deleted | `RevokeAllUserGrantsAsync()` |
| **Verification** | Reports missing/extra grants | `VerifyUserGrantsAsync()` |
| **Repair** | Adds missing grants without removing extras | `RepairUserGrantsAsync()` |

---

## 12. Delegation (CanGive)

### How It Works

- `CanOwn=true` → you can **use** the permission
- `CanGive=true` → you can **grant** it to others

### Scope Rules for Delegation

Granters can only delegate at **same or narrower** scope:

```
Owner (CanGive=true, Project scope)
  └─▶ Can grant at any level
       └─▶ Manager (CanGive=true, Area scope)
             └─▶ Can grant within their Area only
                  └─▶ Employee (CanGive=false)
                        └─▶ Cannot delegate to anyone
```

`CanUserGrantAsync()` enforces this via `ScopeCovers()` validation.

### CanGive in GrantService.GrantAsync

```csharp
if (grantedByUserId.HasValue)
{
    var canGrant = await CanUserGrantAsync(grantedByUserId.Value, grantTypeId, scope);
    if (!canGrant)
        throw new UnauthorizedAccessException("User does not have permission to grant this type or scope");
}
```

---

## 13. Verification & Repair

### Verify User Grants

Compares actual grants vs. expected grants from role template:

```
Expected (from template) - Actual (in DB) = Missing (permission gaps)
Actual (in DB) - Expected (from template) = Extra (manual grants or security risk)
```

### Repair User Grants

Adds missing grants without removing extras. Run manually or via API:

```
GET  /api/admin/verify-grants              — Verify all users
GET  /api/admin/verify-grants/{userId}     — Verify single user
POST /api/admin/verify-grants/repair       — Repair all users
POST /api/admin/verify-grants/{userId}/repair — Repair single user
```

All endpoints require `Grant:AdminAccess`.

---

## 14. Security Design Points

| Design Point | Why It Matters |
|-------------|----------------|
| **Self-scoped skip in CanGive** | Grants with all-null scope only apply to own resources — cannot be delegated |
| **DetermineEffectiveScope extracts ONE level** | Prevents cascade over-granting from multi-scope grants |
| **CanOwn/CanGive separation** | Having a permission doesn't mean you can delegate it |
| **ScopeCovers validation** | Granters can only delegate at same or narrower scope |
| **Role change removes old grants** | Prevents privilege escalation when switching roles |
| **Login-time reconciliation** | Template changes propagate automatically on next login |
| **All IgnoreQueryFilters() audited** | Every usage in GrantService has security audit comment |
| **Default-deny for pages** | `AuthorizeFolder("/")` — everything requires auth unless whitelisted |
| **Claims-based scope** | Scope built from CurrentUserService claims, not request parameters (prevents spoofing) |

---

## 15. Key Files Reference

| File | Lines | Purpose |
|------|-------|---------|
| `Models/Grant.cs` | ~30 | Grant entity |
| `Models/GrantType.cs` | ~25 | Grant type definition |
| `Models/RoleTemplate.cs` | ~30 | Role template definition |
| `Models/RoleTemplateGrant.cs` | ~20 | Template → grant mapping |
| `Services/GrantService.cs` | ~850 | Core grant logic (checking, assigning, revoking, scoping) |
| `Services/IGrantService.cs` | ~60 | Interface + GrantScope record |
| `Authorization/GrantAuthorizationHandler.cs` | ~60 | [Authorize] handler |
| `Authorization/GrantPolicyProvider.cs` | ~50 | Dynamic policy factory |
| `Authorization/GrantRequirement.cs` | ~17 | IAuthorizationRequirement |
| `TagHelpers/RequireGrantTagHelper.cs` | ~117 | View-level `<require-grant>` |
| `Data/SeedData/GrantTypeSeed.cs` | — | All 125 grant types |
| `Data/SeedData/RoleTemplateSeed.cs` | — | All 11 role templates + mappings |
| `Pages/Owner/Hub/Grants.cshtml.cs` | — | Grant management UI |
| `Pages/Owner/Hub/RoleTemplates/*.cshtml.cs` | — | Role template CRUD |
| `Controllers/Api/V1/AdminGrantsController.cs` | ~194 | Verify/repair API |
| `Middleware/ApiAuthenticationMiddleware.cs` | ~434 | External API auth |
| `Program.cs` | L177-270 | Authorization service registration |
