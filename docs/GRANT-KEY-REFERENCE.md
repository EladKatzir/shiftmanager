# Grant Key Reference

This document provides a complete reference for all grant keys used in the ShiftManager application for authorization and access control.

## Table of Contents

1. [Overview](#overview)
2. [Grant System Architecture](#grant-system-architecture)
3. [Grant Categories](#grant-categories)
4. [Complete Grant Key Reference](#complete-grant-key-reference)
5. [Grant Scope Hierarchy](#grant-scope-hierarchy)
6. [UI Usage Reference](#ui-usage-reference)
7. [Adding New Grants](#adding-new-grants)

---

## Overview

ShiftManager uses a grant-based authorization system that provides fine-grained access control. Grants are permissions that can be:
- **Owned** (`CanOwn`): The user can perform the action
- **Given** (`CanGive`): The user can grant this permission to others

Grants are scoped to organizational hierarchy levels (Project > Area > Molecule > Company/Department).

### Key Concepts

| Concept | Description |
|---------|-------------|
| **Grant** | A specific permission assigned to a user |
| **GrantType** | A definition of a permission type (e.g., `ViewShifts`) |
| **GrantScope** | The organizational level at which the grant applies |
| **GrantCategory** | A grouping of related grant types |
| **Auto-Grant** | Grants automatically assigned when a user receives a role |

---

## Grant System Architecture

### Models

- **`Grant`** (`Models/Grant.cs`): Individual grant instance assigned to a user
- **`GrantType`** (`Models/GrantType.cs`): Definition of a grant type
- **`GrantScope`** (`Services/IGrantService.cs`): Record defining scope parameters
- **`RoleTemplateGrant`** (`Models/RoleTemplateGrant.cs`): Links role templates to auto-grants

### Services

- **`IGrantService`** / **`GrantService`**: Core service for grant checking and management

### UI Components

- **`<require-grant>`** Tag Helper: Conditionally renders content based on grants
- **`[Authorize(Policy = "Grant:*")]`**: Page-level authorization attribute

---

## Grant Categories

Grants are organized into categories defined in `GrantCategory` enum:

| Category | Value | Description |
|----------|-------|-------------|
| **Shift** | 0 | Shift viewing, assignment, and management |
| **Duty** | 1 | Duty (Hakam/Katzin) related permissions |
| **Chore** | 2 | Chore viewing and assignment |
| **Vacation** | 3 | Vacation requests and approvals |
| **Swap** | 4 | Shift swap operations |
| **UserManagement** | 5 | User administration |
| **GrantManagement** | 6 | Grant and role assignment |
| **Hierarchy** | 7 | Organizational structure management |
| **Settings** | 8 | Configuration and settings |
| **Analytics** | 9 | Reports and analytics |
| **Email** | 10 | Email and notification configuration |
| **System** | 11 | System-level administration |
| **Custom** | 99 | User-defined grants |

---

## Complete Grant Key Reference

### Shift Grants (Category: Shift)

#### Viewing

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewShifts` | View shifts in the calendar | Company | Layout, Calendar pages |
| `ViewAllShifts` | View all shifts across companies | Molecule | - |
| `ViewAlhutShiftCalendar` | View Alhut shift calendar | Company | - |
| `ViewTextShiftCalendar` | View Text shift calendar | Company | - |
| `ViewBRShiftCalendar` | View BR shift calendar | Molecule | - |
| `ViewHakamShiftCalendar` | View Hakam shift calendar | Area | - |

#### Assignment

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `AssignAlhutShifts` | Assign Alhut shifts to employees | Company | Calendar/Month |
| `AssignTextShifts` | Assign Text shifts to employees | Company | Calendar/Month |
| `AssignBRShifts` | Assign BR shifts to employees | Molecule | Calendar/Month |
| `AssignTechShifts` | Assign Tech shifts to employees | Department | Calendar/Month |

#### Program/Blueprint Management

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `EditShiftPrograms` | Edit existing shift programs | Company |
| `CreateShiftPrograms` | Create new shift programs | Company |
| `DeleteShiftPrograms` | Delete shift programs | Company |
| `EditShiftTypes` | Edit shift type definitions | Company |
| `CreateShiftTypes` | Create new shift types | Company |
| `ManageAlhutBlueprints` | Manage Alhut shift blueprints | Molecule |
| `ManageAlhutPrograms` | Manage Alhut shift programs | Molecule |
| `ManageTextBlueprints` | Manage Text shift blueprints | Molecule |
| `ManageTextPrograms` | Manage Text shift programs | Molecule |
| `ManageBRBlueprints` | Manage BR shift blueprints | Molecule |
| `ManageBRPrograms` | Manage BR shift programs | Molecule |
| `ManageHakamBlueprints` | Manage Hakam shift blueprints | Area |
| `ManageHakamPrograms` | Manage Hakam shift programs | Area |

#### Eligibility (Self-Scoped)

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `CanBeAssignedAlhutShifts` | User can be assigned Alhut shifts | Self |
| `CanBeAssignedTextShifts` | User can be assigned Text shifts | Self |
| `CanBeAssignedBRShifts` | User can be assigned BR shifts | Self |
| `CanBeAssignedHakamShifts` | User can be assigned Hakam shifts | Self |

#### Tech Department Calendars

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `ViewHanavaCalendar` | View Hanava department calendar | Department |
| `ViewDeltaCalendar` | View Delta department calendar | Department |
| `ViewYekevCalendar` | View Yekev department calendar | Department |
| `ViewMoviltechCalendar` | View Moviltech department calendar | Department |

#### Tech Department Assignment

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `AssignHanavaShifts` | Assign Hanava department shifts | Department |
| `AssignDeltaShifts` | Assign Delta department shifts | Department |
| `AssignYekevShifts` | Assign Yekev department shifts | Department |
| `AssignMoviltechShifts` | Assign Moviltech department shifts | Department |

#### Tech Department Management

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `ManageHanavaBlueprints` | Manage Hanava blueprints | Department |
| `ManageHanavaPrograms` | Manage Hanava programs | Department |
| `ManageDeltaBlueprints` | Manage Delta blueprints | Department |
| `ManageDeltaPrograms` | Manage Delta programs | Department |
| `ManageYekevBlueprints` | Manage Yekev blueprints | Department |
| `ManageYekevPrograms` | Manage Yekev programs | Department |
| `ManageMoviltechBlueprints` | Manage Moviltech blueprints | Department |
| `ManageMoviltechPrograms` | Manage Moviltech programs | Department |

#### Tech Eligibility

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `CanBeAssignedHanava` | User can be assigned to Hanava | Self |
| `CanBeAssignedDelta` | User can be assigned to Delta | Self |
| `CanBeAssignedYekev` | User can be assigned to Yekev | Self |
| `CanBeAssignedMoviltech` | User can be assigned to Moviltech | Self |

---

### Duty Grants (Category: Duty)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewDuties` | View duty assignments | Area | Layout, Calendar pages |
| `AssignHakamDuties` | Assign Hakam duties | Area | Calendar/Month |
| `AssignKatzinDuties` | Assign Katzin duties | Area | Calendar/Month |
| `EditDutyPrograms` | Edit duty programs | Area | - |
| `ManageKatzinBlueprints` | Manage Katzin blueprints | Area | - |
| `ManageKatzinPrograms` | Manage Katzin programs | Area | - |

---

### Chore Grants (Category: Chore)

#### General Chores

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewChores` | View chore assignments | Molecule | Layout, Calendar pages |
| `AssignChores` | Assign chores to employees | Molecule | Calendar/Month |
| `EditChoreTypes` | Edit chore type definitions | Molecule | - |
| `CreateChoreTypes` | Create new chore types | Molecule | - |

#### Shiklut (Helper Molecule)

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `ViewShiklutCalendar` | View Shiklut calendar | Molecule |
| `AssignShiklutChores` | Assign Shiklut chores | Molecule |
| `ManageShiklutBlueprints` | Manage Shiklut blueprints | Molecule |
| `ManageShiklutPrograms` | Manage Shiklut programs | Molecule |
| `CanBeAssignedShiklut` | User can be assigned Shiklut | Self |

#### NOC (Helper Molecule)

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `ViewNOCCalendar` | View NOC calendar | Molecule |
| `AssignNOCChores` | Assign NOC chores | Molecule |
| `ManageNOCBlueprints` | Manage NOC blueprints | Molecule |
| `ManageNOCPrograms` | Manage NOC programs | Molecule |
| `CanBeAssignedNOC` | User can be assigned NOC | Self |

---

### Vacation Grants (Category: Vacation)

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `ViewVacations` | View vacation calendar | Company |
| `RequestVacation` | Submit vacation requests | Self |
| `ApproveVacations` | Approve/deny vacation requests | Company |
| `OverrideVacationLimits` | Override vacation quota limits | Company |

---

### Swap Grants (Category: Swap)

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `RequestSwap` | Request shift swaps | Self |
| `ApproveSwaps` | Approve/deny swap requests | Company |
| `InitiateSwap` | Initiate swaps on behalf of others | Company |

---

### User Management Grants (Category: UserManagement)

| Key | Description | Default Scope |
|-----|-------------|---------------|
| `ViewUsers` | View user list and profiles | Company |
| `EditUsers` | Edit user information | Company |
| `CreateUsers` | Create new users | Company |
| `DeactivateUsers` | Deactivate user accounts | Company |
| `ResetPasswords` | Reset user passwords | Company |
| `AssignJobTypes` | Assign job types to users | Company |
| `ViewAllUsers` | View all users across companies | Molecule |

---

### Grant Management Grants (Category: GrantManagement)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewGrants` | View grant assignments | Company | Admin/Organization/Grants |
| `AssignGrants` | Assign grants to users | Company | Admin/Organization/Grants/Assign |
| `RevokeGrants` | Revoke grants from users | Company | - |
| `AssignRoles` | Assign roles to users | Company | Admin/Directors, Admin/Organization/Roles |

---

### Hierarchy Grants (Category: Hierarchy)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewHierarchy` | View organizational hierarchy | Company | Admin/Organization |
| `EditCompany` | Edit company information | Company | Admin/Index, Admin/Companies |
| `EditMolecule` | Edit molecule information | Molecule | Admin/Organization/Molecules |
| `EditArea` | Edit area information | Area | Admin/Organization/Areas, Projects |
| `CreateCompany` | Create new companies | Molecule | - |
| `CreateMolecule` | Create new molecules | Area | - |
| `ManageShiftGroupings` | Manage shift groupings | Molecule | Admin/Organization/ShiftGroupings |
| `ManageJobTypes` | Manage job type definitions | Area | Admin/Organization/JobTypes |
| `ManageDepartments` | Manage departments | Molecule | Admin/Organization/Departments |

---

### Settings Grants (Category: Settings)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewSettings` | View configuration settings | Company | Layout, Admin/Settings |
| `EditCompanySettings` | Edit company-level settings | Company | - |
| `EditMoleculeSettings` | Edit molecule-level settings | Molecule | - |
| `EditAreaSettings` | Edit area-level settings | Area | - |

---

### Analytics Grants (Category: Analytics)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `ViewAnalytics` | View analytics dashboard | Company | Layout, Calendar pages |
| `ViewReports` | View generated reports | Company | - |
| `ExportData` | Export data to files | Company | - |

---

### Email Grants (Category: Email)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `SendNotifications` | Send email notifications | Company | - |
| `ConfigureEmailSettings` | Configure email settings | Company | Admin/Index, Owner/EmailConfig |

---

### System Grants (Category: System)

| Key | Description | Default Scope | Used In |
|-----|-------------|---------------|---------|
| `AdminAccess` | Administrative access to owner pages | Project | Layout, Diagnostic, Owner pages |
| `SystemConfiguration` | System-wide configuration | Project | Admin/Index, Owner pages |
| `ViewAuditLog` | View audit log entries | Company | Layout |
| `ManageApiKeys` | Manage API keys | Company | - |

---

## Grant Scope Hierarchy

Grants follow a hierarchical scope model where broader scopes include narrower ones:

```
Project (Broadest)
    └── Area
        └── Molecule
            ├── Company
            │   └── JobType (can be scoped to company)
            └── Department
                └── JobType
Self (Narrowest - user's own resources only)
```

### Scope Levels

| Level | Description | Example Use |
|-------|-------------|-------------|
| **Self** | User's own resources only | Vacation requests, eligibility |
| **Company** | Single company | Standard manager grants |
| **Department** | Single department | Tech shift management |
| **Molecule** | Molecule and all companies within | BR shifts, chores |
| **Area** | Area and all molecules within | Hakam duties, Katzin |
| **Project** | Entire project | System admin |

### Scope Modes for Role Auto-Grants

When grants are automatically assigned via role templates, the scope can be determined by:

| Mode | Description |
|------|-------------|
| `SameAsRole` | Grant scope matches the role's scope |
| `ExpandToMolecule` | Expand scope to the molecule level |
| `ExpandToArea` | Expand scope to the area level |
| `Custom` | Use explicitly defined scope |

---

## UI Usage Reference

### Tag Helper: `<require-grant>`

The `<require-grant>` tag helper conditionally renders content based on user grants.

**Location**: `TagHelpers/RequireGrantTagHelper.cs`

#### Attributes

| Attribute | Description | Default |
|-----------|-------------|---------|
| `key` | Grant key(s), comma-separated | Required |
| `mode` | `"all"` (require all) or `"any"` (require one) | `"all"` |
| `negate` | If true, show when user lacks the grant | `false` |

#### Examples

```html
<!-- Single grant check -->
<require-grant key="ViewShifts">
    <p>Shifts content here</p>
</require-grant>

<!-- Any of multiple grants -->
<require-grant key="ViewShifts,ViewChores,ViewDuties" mode="any">
    <p>Calendar legend</p>
</require-grant>

<!-- All grants required -->
<require-grant key="EditCompany,AssignRoles" mode="all">
    <p>Advanced admin options</p>
</require-grant>

<!-- Negated (show when lacking grant) -->
<require-grant key="AdminAccess" negate="true">
    <p>Contact admin for access</p>
</require-grant>
```

### PageModel Authorization: `[Authorize(Policy = "Grant:*")]`

Page-level authorization using policy-based authorization.

#### Examples

```csharp
[Authorize(Policy = "Grant:EditCompany")]
public class CompaniesModel : PageModel { }

[Authorize(Policy = "Grant:AdminAccess")]
public class DiagnosticModel : PageModel { }

[Authorize(Policy = "Grant:SystemConfiguration")]
public class FeatureFlagsModel : PageModel { }
```

### Pages Using Grant Authorization

| Page | Grant Required |
|------|----------------|
| `Admin/Companies` | `EditCompany` |
| `Admin/Directors` | `AssignRoles` |
| `Admin/Settings/Index` | `ViewSettings` |
| `Admin/SetupTasks/Index` | `SystemConfiguration` |
| `Admin/Organization/Index` | `ViewHierarchy` |
| `Admin/Organization/Areas/Index` | `EditArea` |
| `Admin/Organization/Departments/Index` | `ManageDepartments` |
| `Admin/Organization/Grants/Index` | `ViewGrants` |
| `Admin/Organization/Grants/Assign` | `AssignGrants` |
| `Admin/Organization/JobTypes/Index` | `ManageJobTypes` |
| `Admin/Organization/Molecules/Index` | `EditMolecule` |
| `Admin/Organization/Projects/Index` | `EditArea` |
| `Admin/Organization/Roles/Index` | `AssignRoles` |
| `Admin/Organization/Roles/Assign` | `AssignRoles` |
| `Admin/Organization/ShiftGroupings/Index` | `ManageShiftGroupings` |
| `Diagnostic` | `AdminAccess` |
| `GriffinDiagnostic` | `AdminAccess` |
| `Owner/Backup` | `AdminAccess` |
| `Owner/ClearCompanySelection` | `AdminAccess` |
| `Owner/DatabaseConsole` | `SystemConfiguration` |
| `Owner/DataLifecycle` | `SystemConfiguration` |
| `Owner/EmailConfig` | `ConfigureEmailSettings` |
| `Owner/EmailTemplates` | `AdminAccess` |
| `Owner/FeatureFlags` | `SystemConfiguration` |
| `Owner/GameConfig` | `AdminAccess` |
| `Owner/GriffinConfig` | `AdminAccess` |
| `Owner/Index` | `AdminAccess` |
| `Owner/LanguageEditMode` | `AdminAccess` |
| `Owner/LanguageManagement` | `AdminAccess` |
| `Owner/SelectCompany` | `AdminAccess` |
| `Owner/SystemHealth` | `AdminAccess` |

---

## Adding New Grants

### Step 1: Define the Grant Type

Add a new entry in `Data/SeedData/GrantTypeSeed.cs`:

```csharp
grants.Add(new GrantType
{
    Id = id++,
    Key = "MyNewGrant",                           // Unique key
    NameKey = "Grant_MyNewGrant",                 // Localization key for name
    DescriptionKey = "Grant_MyNewGrant_Desc",    // Localization key for description
    Category = GrantCategory.Shift,              // Appropriate category
    DefaultScope = GrantScopeLevel.Company,      // Default scope level
    IsSystem = true                              // true for built-in grants
});
```

### Step 2: Add Localization Keys

Add localization entries for the grant name and description in the appropriate resource files.

### Step 3: Apply Database Migration

Run the application to seed the new grant type, or create a migration if needed.

### Step 4: Use in UI

#### Tag Helper

```html
<require-grant key="MyNewGrant">
    <!-- Protected content -->
</require-grant>
```

#### Page Authorization

```csharp
[Authorize(Policy = "Grant:MyNewGrant")]
public class MyPageModel : PageModel { }
```

### Step 5: Check Grants Programmatically

```csharp
// In a service or PageModel
var hasGrant = await _grantService.HasGrantAsync(userId, "MyNewGrant");

// With scope
var scope = new GrantScope(CompanyId: companyId);
var hasGrantInScope = await _grantService.HasGrantAsync(userId, "MyNewGrant", scope);
```

### Step 6: Configure Auto-Grants (Optional)

If the grant should be automatically assigned with certain roles, configure it in the role template setup.

---

## Best Practices

1. **Use Specific Grants**: Create specific grants rather than overloading existing ones
2. **Follow Naming Conventions**: Use `Verb + Noun` pattern (e.g., `ViewShifts`, `AssignChores`)
3. **Choose Appropriate Scope**: Select the narrowest scope that makes sense
4. **Document Purpose**: Always provide clear name and description localization keys
5. **Test Both Paths**: Test both authorized and unauthorized access
6. **Use Tag Helper for UI**: Prefer `<require-grant>` over manual checks in views
7. **Combine with Page Authorization**: Use both tag helpers (for UI elements) and `[Authorize]` (for page access)

---

## Related Files

| File | Purpose |
|------|---------|
| `Models/Grant.cs` | Grant entity model |
| `Models/GrantType.cs` | Grant type definition model |
| `Models/Support/GrantCategory.cs` | Category enumeration |
| `Models/Support/GrantScopeLevel.cs` | Scope level enumeration |
| `Models/Support/GrantScopeMode.cs` | Scope mode enumeration |
| `Models/RoleTemplateGrant.cs` | Role-to-grant mapping |
| `Services/IGrantService.cs` | Grant service interface |
| `Services/GrantService.cs` | Grant service implementation |
| `TagHelpers/RequireGrantTagHelper.cs` | UI tag helper |
| `Data/SeedData/GrantTypeSeed.cs` | Grant type seed data |
