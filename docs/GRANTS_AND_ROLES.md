# Grants and Role Templates Documentation

> **Last Updated:** 2026-02-06
> **Total Grant Types:** 114
> **Total Role Templates:** 11

---

## Terminology

This section clarifies the distinction between two commonly confused concepts in ShiftManager.

### Role (Permission Level)

A **Role** determines what actions a user can perform within the system. Roles are associated with permission levels and are assigned via `RoleTemplate`.

| Role | Description |
|------|-------------|
| Owner | Full system access, can manage everything |
| Director | Can oversee multiple companies/molecules |
| Manager | Can manage team within a company |
| Employee | Standard user with basic access |
| Trainee | Limited access, cannot be assigned shifts |
| Assigner | Can assign chores (uses Employee navigation) |

**Code entities:**
- `RoleTemplate` - Defines a bundle of grants for a role
- `UserRoleAssignment` - Links users to role templates
- `AppUser.Role` - The user's permission role (legacy enum)

**UI labels:**
- Filter dropdown: "Role"
- Column header: "Role"
- Edit button: "Edit Role"

---

### Job Type (Work Classification)

A **Job Type** determines what kind of shifts a user can be assigned to. It classifies the type of work, not the permission level.

| Job Type | Description |
|----------|-------------|
| Alhut | Alhut shift eligibility |
| BR | BR shift eligibility |
| Text | Text shift eligibility |
| Hakam | Hakam duty eligibility |
| Tech | Technical department |

**Code entities:**
- `JobType` - The job type entity
- `AppUser.JobTypeId` - Links user to their job type
- `CanBeAssigned*Shifts` grants - Control eligibility per job type

**UI labels:**
- Filter dropdown: "Job Type"
- Column header: "Job Type"
- Edit button: "Edit Job Type"

---

### Summary

| Concept | Purpose | Example Values | Controls |
|---------|---------|----------------|----------|
| **Role** | Permission level | Owner, Director, Manager | What user CAN DO |
| **Job Type** | Work classification | Alhut, BR, Text, Hakam | What user CAN BE ASSIGNED |

**Never mix these terms** - a user's Role (e.g., Manager) is independent of their Job Type (e.g., Alhut).

---

## Overview

ShiftManager uses a grant-based authorization system. Grants control what users can do in the system, organized by category and scope level.

---

## Grant Scope Levels

| Level | Description |
|-------|-------------|
| `Self` | User's own data only |
| `Company` | User's company only |
| `Molecule` | All companies in user's molecule |
| `Area` | All companies in user's area |
| `Department` | User's department within company |
| `Project` | System-wide (Owner level) |

---

## Grant Categories

### Shift Grants (IDs 1-11, 62-107)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 1 | `ViewShifts` | Company | View shifts in own company |
| 2 | `ViewAllShifts` | Molecule | View all shifts across molecule |
| 3 | `AssignAlhutShifts` | Company | Assign Alhut shifts |
| 4 | `AssignTextShifts` | Company | Assign Text shifts |
| 5 | `AssignBRShifts` | Molecule | Assign BR shifts |
| 6 | `AssignTechShifts` | Department | Assign Tech shifts |
| 7 | `EditShiftPrograms` | Company | Edit shift programs |
| 8 | `CreateShiftPrograms` | Company | Create shift programs |
| 9 | `DeleteShiftPrograms` | Company | Delete shift programs |
| 10 | `EditShiftTypes` | Company | Edit shift types/blueprints |
| 11 | `CreateShiftTypes` | Company | Create shift types |
| 62 | `ViewAlhutShiftCalendar` | Company | View Alhut shift calendar |
| 63 | `ViewTextShiftCalendar` | Company | View Text shift calendar |
| 64 | `ViewBRShiftCalendar` | Molecule | View BR shift calendar |
| 65 | `ViewHakamShiftCalendar` | Area | View Hakam shift calendar |
| 66 | `CanBeAssignedAlhutShifts` | Self | Eligibility for Alhut shift assignment |
| 67 | `CanBeAssignedTextShifts` | Self | Eligibility for Text shift assignment |
| 68 | `CanBeAssignedBRShifts` | Self | Eligibility for BR shift assignment |
| 69 | `CanBeAssignedHakamShifts` | Self | Eligibility for Hakam shift assignment |
| 70-85 | Blueprint/Program Management | Various | Manage specific shift type blueprints/programs |
| 86-107 | Tech Department Grants | Department | Tech-specific calendars, assignments, blueprints |

### Duty Grants (IDs 12-16, 99-100)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 13 | `ViewDuties` | Area | View duty assignments |
| 14 | `AssignHakamDuties` | Area | Assign Hakam duties |
| 15 | `AssignKatzinDuties` | Area | Assign Katzin duties |
| 16 | `EditDutyPrograms` | Area | Edit duty programs |
| 99 | `ManageKatzinBlueprints` | Area | Manage Katzin blueprints |
| 100 | `ManageKatzinPrograms` | Area | Manage Katzin programs |

### Chore Grants (IDs 17-20, 101-107)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 17 | `ViewChores` | Molecule | View chores |
| 18 | `AssignChores` | Molecule | Assign chores |
| 19 | `EditChoreTypes` | Molecule | Edit chore types |
| 20 | `CreateChoreTypes` | Molecule | Create chore types |
| 101-107 | Shiklut/NOC Grants | Molecule | Helper molecule-specific chore grants |

### Vacation Grants (IDs 21-24)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 21 | `ViewVacations` | Company | View vacation requests |
| 22 | `RequestVacation` | Self | Request vacation |
| 23 | `ApproveVacations` | Company | Approve vacation requests |
| 24 | `OverrideVacationLimits` | Company | Override vacation limits |

### Swap Grants (IDs 25-28)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 26 | `RequestSwap` | Self | Request shift swap |
| 27 | `ApproveSwaps` | Company | Approve swap requests |
| 28 | `InitiateSwap` | Company | Initiate swap on behalf of others |

### User Management Grants (IDs 29-35)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 29 | `ViewUsers` | Company | View users in company |
| 30 | `EditUsers` | Company | Edit user profiles |
| 31 | `CreateUsers` | Company | Create new users |
| 32 | `DeactivateUsers` | Company | Deactivate users |
| 33 | `ResetPasswords` | Company | Reset user passwords |
| 34 | `AssignJobTypes` | Company | Assign job types to users |
| 35 | `ViewAllUsers` | Molecule | View all users in molecule |

### Grant Management Grants (IDs 36-40)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 37 | `ViewGrants` | Company | View grants |
| 38 | `AssignGrants` | Company | Assign grants to users |
| 39 | `RevokeGrants` | Company | Revoke grants from users |
| 40 | `AssignRoles` | Company | Assign role templates |

### Hierarchy Grants (IDs 41-49)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 41 | `ViewHierarchy` | Company | View organizational hierarchy |
| 42 | `EditCompany` | Company | Edit company settings |
| 43 | `EditMolecule` | Molecule | Edit molecule settings |
| 44 | `EditArea` | Area | Edit area settings |
| 45 | `CreateCompany` | Molecule | Create new companies |
| 46 | `CreateMolecule` | Area | Create new molecules |
| 47 | `ManageShiftGroupings` | Molecule | Manage shift groupings |
| 48 | `ManageJobTypes` | Area | Manage job types |
| 49 | `ManageDepartments` | Molecule | Manage departments |

### Settings Grants (IDs 50-53)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 50 | `ViewSettings` | Company | View settings |
| 51 | `EditCompanySettings` | Company | Edit company settings |
| 52 | `EditMoleculeSettings` | Molecule | Edit molecule settings |
| 53 | `EditAreaSettings` | Area | Edit area settings |

### Analytics Grants (IDs 54-56)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 54 | `ViewAnalytics` | Company | View analytics |
| 55 | `ViewReports` | Company | View reports |
| 56 | `ExportData` | Company | Export data |

### System Grants (IDs 57-61, 108-111)

| ID | Key | Default Scope | Description |
|----|-----|---------------|-------------|
| 57 | `AdminAccess` | Project | Full admin access (Owner) |
| 58 | `SystemConfiguration` | Project | System-wide configuration |
| 59 | `ViewAuditLog` | Company | View audit logs |
| 60 | `ManageApiKeys` | Company | Manage API keys |
| **108** | **`AccessAdminNavigation`** | Company | **See admin navigation sidebar** |
| **109** | **`DirectorHubAccess`** | Area | **Access Director Hub home** |
| **110** | **`ManagerHomeAccess`** | Company | **Access Manager home** |
| **111** | **`ViewCompanyCalendar`** | Company | **View company calendar** |
| **112** | **`ManageJoinRequests`** | Company | **Approve/reject join requests** |
| **113** | **`ViewCompanyUsers`** | Company | **View users in company scope** |
| **114** | **`EditCompanyUsers`** | Company | **Edit users in company scope** |

---

## Role Templates

Role templates define the default grants assigned to users when they are given a role.

### Role Template Summary

| ID | Role Name | Navigation | Key Capabilities |
|----|-----------|------------|------------------|
| 1 | Employee | Employee nav | View shifts, request vacation/swap |
| 2 | BR Director | Admin nav | Assign BR shifts, approve vacations/swaps |
| 3 | Alhut Lead | Admin nav | Assign Alhut shifts, view users |
| 4 | Text Lead | Admin nav | Assign Text shifts, view users |
| 5 | Alhut Director | Admin nav + Director Hub | Molecule-wide Alhut management |
| 6 | Text Director | Admin nav + Director Hub | Molecule-wide Text management |
| 7 | Molecule Admin | Admin nav | Full molecule administration |
| 8 | Assigner | **Employee nav** | Chore assignment only |
| 9 | Department Lead | Admin nav | Tech department management |
| 10 | Area Admin | Admin nav + Director Hub | Area-wide administration |
| 11 | Owner | Admin nav + Director Hub | Full system access |

### Navigation Grant Matrix

| Role | AccessAdminNavigation | DirectorHubAccess | AdminAccess |
|------|:---------------------:|:-----------------:|:-----------:|
| Employee | - | - | - |
| Assigner | - | - | - |
| BR Director | ✓ | - | - |
| Alhut Lead | ✓ | - | - |
| Text Lead | ✓ | - | - |
| Department Lead | ✓ | - | - |
| Molecule Admin | ✓ | - | - |
| Alhut Director | ✓ | ✓ | - |
| Text Director | ✓ | ✓ | - |
| Area Admin | ✓ | ✓ | - |
| Owner | ✓ (CanGive) | ✓ (CanGive) | ✓ (CanGive) |

### Detailed Role Template Grants

#### 1. Employee (RoleTemplateId: 1)
- ViewShifts (1)
- ViewChores (17)
- ViewVacations (21)
- RequestVacation (22)
- RequestSwap (26)

#### 2. BR Director (RoleTemplateId: 2)
- AssignBRShifts (5) - CanGive, ExpandToMolecule
- ViewAllShifts (2) - ExpandToMolecule
- ApproveVacations (23)
- ApproveSwaps (27)
- ViewUsers (29)
- EditUsers (30)
- **AccessAdminNavigation (108)**

#### 3. Alhut Lead (RoleTemplateId: 3)
- AssignAlhutShifts (3)
- ViewShifts (1)
- ViewUsers (29)
- **AccessAdminNavigation (108)**

#### 4. Text Lead (RoleTemplateId: 4)
- AssignTextShifts (4)
- ViewShifts (1)
- ViewUsers (29)
- **AccessAdminNavigation (108)**

#### 5. Alhut Director (RoleTemplateId: 5)
- AssignAlhutShifts (3) - CanGive
- ViewAllShifts (2)
- EditShiftPrograms (7)
- ViewAllUsers (35)
- AssignRoles (40)
- **AccessAdminNavigation (108)**
- **DirectorHubAccess (109)**

#### 6. Text Director (RoleTemplateId: 6)
- AssignTextShifts (4) - CanGive
- ViewAllShifts (2)
- EditShiftPrograms (7)
- ViewAllUsers (35)
- AssignRoles (40)
- **AccessAdminNavigation (108)**
- **DirectorHubAccess (109)**

#### 7. Molecule Admin (RoleTemplateId: 7)
- ViewAllShifts (2) - CanGive
- AssignChores (18) - CanGive
- EditChoreTypes (19)
- ViewAllUsers (35)
- EditUsers (30)
- AssignRoles (40) - CanGive
- EditMolecule (43)
- ManageShiftGroupings (47)
- EditMoleculeSettings (51)
- **AccessAdminNavigation (108)**

#### 8. Assigner (RoleTemplateId: 8)
**Note: Uses employee navigation (no AccessAdminNavigation)**
- AssignChores (17)
- ViewChores (16)

#### 9. Department Lead (RoleTemplateId: 9)
- AssignTechShifts (6)
- ViewShifts (1)
- ViewUsers (29)
- EditUsers (30)
- **AccessAdminNavigation (108)**

#### 10. Area Admin (RoleTemplateId: 10)
- ViewDuties (13) - CanGive
- AssignHakamDuties (14) - CanGive
- AssignKatzinDuties (15) - CanGive
- EditArea (44)
- CreateCompany (45)
- CreateMolecule (46)
- ManageJobTypes (48)
- EditAreaSettings (52)
- **AccessAdminNavigation (108)**
- **DirectorHubAccess (109)**

#### 11. Owner (RoleTemplateId: 11)
- AdminAccess (57) - CanGive
- SystemConfiguration (58)
- **AccessAdminNavigation (108)** - CanGive
- **DirectorHubAccess (109)** - CanGive

---

## Using Grants in Code

### View Authorization (Razor)

```html
<!-- Show content only if user has grant -->
<require-grant key="AccessAdminNavigation">
    @* Admin sidebar content *@
</require-grant>

<!-- Negate for employee-only content -->
<require-grant key="AccessAdminNavigation" negate="true">
    @* Employee navigation *@
</require-grant>
```

### Code-Behind Authorization

```csharp
// Inject IGrantService
private readonly IGrantService _grantService;

// Check grant
var hasAccess = await _grantService.HasGrantAsync(userId, "AccessAdminNavigation");

// Check multiple grants
var isAdmin = await _grantService.HasGrantAsync(userId, "AdminAccess");
var isDirector = await _grantService.HasGrantAsync(userId, "DirectorHubAccess");
```

### Scope-Aware Authorization (Cross-Company Access)

```csharp
// Get all companies the user can access for a specific grant
var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(
    userId,
    "ManageJoinRequests"
);

// Check if user has grant for a specific company
var canApprove = await _grantService.HasGrantForCompanyAsync(
    userId,
    "ManageJoinRequests",
    targetCompanyId
);
```

The scope resolution works based on the grant's scope level:
- **Project scope** → All companies in project
- **Area scope** → All companies in area
- **Molecule scope** → All companies in molecule
- **Company scope** → That specific company
- **Self scope** → User's own company

### Policy-Based Authorization

```csharp
// In PageModel attribute
[Authorize(Policy = "CanEditChores")]

// In Startup/Program.cs
services.AddAuthorization(options =>
{
    options.AddPolicy("CanEditChores", policy =>
        policy.RequireClaim("Grant", "AssignChores", "EditChoreTypes"));
});
```

---

## Important Notes

1. **Never use role checks** - Always use grant-based authorization
2. **Grant inheritance** - Users inherit grants from their role template on creation
3. **CanGive flag** - Allows user to grant this permission to others
4. **ScopeMode** - Controls how grant scope expands (SameAsRole, ExpandToMolecule, etc.)

---

## Navigation Verification Matrix

This matrix verifies the expected navigation behavior for each role:

| Role | Sidebar | Home Link | Admin Hub | Owner Panel | People Link |
|------|---------|-----------|-----------|-------------|-------------|
| Employee | Employee nav | My Calendar | - | - | My Team |
| Assigner | Employee nav | My Calendar | - | - | My Team |
| BR Director | Admin nav | Admin Home | ✓ | - | My Team |
| Alhut Lead | Admin nav | Admin Home | ✓ | - | My Team |
| Text Lead | Admin nav | Admin Home | ✓ | - | My Team |
| Department Lead | Admin nav | Admin Home | ✓ | - | My Team |
| Molecule Admin | Admin nav | Admin Home | ✓ | - | People (all) |
| Alhut Director | Admin nav | Director Hub | ✓ | - | People (all) |
| Text Director | Admin nav | Director Hub | ✓ | - | People (all) |
| Area Admin | Admin nav | Director Hub | ✓ | - | People (all) |
| Owner | Admin nav | Owner Home | - | ✓ | People (all) |

### Verification Status (2026-02-06)

- [x] Build succeeds with 0 errors
- [x] 33/33 grant-related tests pass
- [x] Navigation grants seeded in GrantTypeSeed.cs (IDs 108-111)
- [x] Role templates have correct navigation grants in RoleTemplateSeed.cs
- [x] _Layout.cshtml uses `<require-grant>` tag helpers exclusively
- [x] No role-based checks (`user.Role`, `isAdmin`) in navigation code

---

## Related Files

- `Data/SeedData/GrantTypeSeed.cs` - Grant type definitions
- `Data/SeedData/RoleTemplateSeed.cs` - Role template grant mappings
- `Services/IGrantService.cs` - Grant checking interface
- `Services/GrantService.cs` - Grant checking implementation
- `TagHelpers/RequireGrantTagHelper.cs` - Razor tag helper
