# ShiftManager v3.0 UI/UX Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement the complete organizational hierarchy redesign with grant-based permissions, 3 molecule types, JobTypes as user attributes, and all supporting UI/UX.

**Architecture:** Multi-phase approach starting with database schema migration, then core services, then UI components. Grant-based authorization replaces role-based. Hierarchy flows Project → Area → Molecule → Company/Department → User.

**Tech Stack:** ASP.NET Core 8, Entity Framework Core, Razor Pages, SQL Server, Cookie Authentication

---

## Table of Contents

1. [Phase 1: Database Schema & Migrations](#phase-1-database-schema--migrations)
2. [Phase 2: Core Domain Services](#phase-2-core-domain-services)
3. [Phase 3: Authentication & Claims](#phase-3-authentication--claims)
4. [Phase 4: Grant Authorization System](#phase-4-grant-authorization-system)
5. [Phase 5: Admin UI - Hierarchy Management](#phase-5-admin-ui---hierarchy-management)
6. [Phase 6: User Management UI](#phase-6-user-management-ui)
7. [Phase 7: Shift System Refactor](#phase-7-shift-system-refactor)
8. [Phase 8: Calendar System Integration](#phase-8-calendar-system-integration)
9. [Phase 9: Circle/Friends System](#phase-9-circlefriends-system)
10. [Phase 10: Settings Hierarchy](#phase-10-settings-hierarchy)
11. [Phase 11: Smart Task System](#phase-11-smart-task-system)
12. [Phase 12: Data Migration & Seed Data](#phase-12-data-migration--seed-data)
13. [Phase 13: Testing & Validation](#phase-13-testing--validation)

---

## Phase 1: Database Schema & Migrations

### Task 1.1: Create Hierarchy Entities

**Files:**
- Create: `Models/Project.cs`
- Create: `Models/Area.cs`
- Create: `Models/Molecule.cs`
- Create: `Models/Department.cs`
- Create: `Models/Support/MoleculeType.cs`
- Modify: `Models/Company.cs` (add MoleculeId FK)

**Step 1: Create MoleculeType enum**

```csharp
// Models/Support/MoleculeType.cs
namespace ShiftManager.Models.Support;

public enum MoleculeType
{
    Workforce = 0,
    Tech = 1,
    Helper = 2
}
```

**Step 2: Create Project entity**

```csharp
// Models/Project.cs
namespace ShiftManager.Models;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<Area> Areas { get; set; } = new();
}
```

**Step 3: Create Area entity**

```csharp
// Models/Area.cs
namespace ShiftManager.Models;

public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project Project { get; set; } = null!;
    public List<Molecule> Molecules { get; set; } = new();
    public List<JobType> JobTypes { get; set; } = new();
    public AreaSettings? Settings { get; set; }
}
```

**Step 4: Create Molecule entity**

```csharp
// Models/Molecule.cs
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MoleculeType Type { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<Company> Companies { get; set; } = new();       // Workforce molecules
    public List<Department> Departments { get; set; } = new();  // Tech molecules
    public List<ShiftGrouping> ShiftGroupings { get; set; } = new();
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public MoleculeSettings? Settings { get; set; }
}
```

**Step 5: Create Department entity**

```csharp
// Models/Department.cs
namespace ShiftManager.Models;

public class Department
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
```

**Step 6: Modify Company entity**

Add to existing `Models/Company.cs`:

```csharp
// Add these properties:
public int? MoleculeId { get; set; }  // Nullable during migration, required after
public Molecule? Molecule { get; set; }
```

**Step 7: Run migration**

```bash
dotnet ef migrations add AddHierarchyEntities
dotnet ef database update
```

**Step 8: Commit**

```bash
git add Models/Project.cs Models/Area.cs Models/Molecule.cs Models/Department.cs Models/Support/MoleculeType.cs Models/Company.cs
git commit -m "feat: add hierarchy entities (Project, Area, Molecule, Department)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.2: Create JobType Entity

**Files:**
- Create: `Models/JobType.cs`
- Modify: `Models/AppUser.cs` (add JobTypeId FK)

**Step 1: Create JobType entity**

```csharp
// Models/JobType.cs
namespace ShiftManager.Models;

public class JobType
{
    public int Id { get; set; }
    public int AreaId { get; set; }  // Area-scoped (same JobTypes across molecules)
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }  // For UI display
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
```

**Step 2: Modify AppUser entity**

Add to existing `Models/AppUser.cs`:

```csharp
// Add these properties (workforce users only):
public int? JobTypeId { get; set; }
public int? DepartmentId { get; set; }  // Tech molecule users

// Navigation
public JobType? JobType { get; set; }
public Department? Department { get; set; }
```

**Step 3: Run migration**

```bash
dotnet ef migrations add AddJobTypeAndUserRelations
dotnet ef database update
```

**Step 4: Commit**

```bash
git add Models/JobType.cs Models/AppUser.cs
git commit -m "feat: add JobType entity and user relations

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.3: Create ShiftGrouping Entities

**Files:**
- Create: `Models/ShiftGrouping.cs`
- Create: `Models/ShiftGroupingCompany.cs`
- Create: `Models/ShiftGroupingJobType.cs`

**Step 1: Create ShiftGrouping entity**

```csharp
// Models/ShiftGrouping.cs
namespace ShiftManager.Models;

public class ShiftGrouping
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;      // "Tzafon", "Darom"
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<ShiftGroupingCompany> Companies { get; set; } = new();
    public List<ShiftGroupingJobType> JobTypes { get; set; } = new();
}
```

**Step 2: Create junction tables**

```csharp
// Models/ShiftGroupingCompany.cs
namespace ShiftManager.Models;

public class ShiftGroupingCompany
{
    public int ShiftGroupingId { get; set; }
    public int CompanyId { get; set; }

    public ShiftGrouping ShiftGrouping { get; set; } = null!;
    public Company Company { get; set; } = null!;
}

// Models/ShiftGroupingJobType.cs
namespace ShiftManager.Models;

public class ShiftGroupingJobType
{
    public int ShiftGroupingId { get; set; }
    public int JobTypeId { get; set; }

    public ShiftGrouping ShiftGrouping { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
}
```

**Step 3: Run migration**

```bash
dotnet ef migrations add AddShiftGroupingEntities
dotnet ef database update
```

**Step 4: Commit**

```bash
git add Models/ShiftGrouping.cs Models/ShiftGroupingCompany.cs Models/ShiftGroupingJobType.cs
git commit -m "feat: add ShiftGrouping with Company and JobType relations

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.4: Create Grant System Entities

**Files:**
- Create: `Models/GrantType.cs`
- Create: `Models/Grant.cs`
- Create: `Models/RoleTemplate.cs`
- Create: `Models/RoleTemplateGrant.cs`
- Create: `Models/UserRole.cs`
- Create: `Models/Support/GrantCategory.cs`
- Create: `Models/Support/GrantScopeLevel.cs`
- Create: `Models/Support/RoleScopeLevel.cs`

**Step 1: Create enums**

```csharp
// Models/Support/GrantCategory.cs
namespace ShiftManager.Models.Support;

public enum GrantCategory
{
    Shift = 0,
    Duty = 1,
    Chore = 2,
    Vacation = 3,
    Swap = 4,
    UserManagement = 5,
    GrantManagement = 6,
    Hierarchy = 7,
    Settings = 8,
    Analytics = 9,
    Email = 10,
    System = 11,
    Custom = 99
}

// Models/Support/GrantScopeLevel.cs
namespace ShiftManager.Models.Support;

public enum GrantScopeLevel
{
    Self = 0,
    Company = 1,
    Department = 2,
    Molecule = 3,
    Area = 4,
    Project = 5
}

// Models/Support/RoleScopeLevel.cs
namespace ShiftManager.Models.Support;

public enum RoleScopeLevel
{
    Implicit = 0,        // Employee - no assignment needed
    Company = 1,         // BR Director
    CompanyJobType = 2,  // Alhut Lead, Text Lead
    Department = 3,      // Department Lead (tech)
    Molecule = 4,        // Molecule Admin, Assigner
    MoleculeJobType = 5, // Alhut Director, Text Director
    Area = 6,            // Area Admin
    Project = 7          // Owner
}
```

**Step 2: Create GrantType entity**

```csharp
// Models/GrantType.cs
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class GrantType
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "AssignAlhutShifts"
    public string NameKey { get; set; } = string.Empty;  // Localization key
    public string DescriptionKey { get; set; } = string.Empty;
    public GrantCategory Category { get; set; }
    public GrantScopeLevel DefaultScope { get; set; }
    public bool IsSystem { get; set; }  // true = built-in, false = custom
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }

    // Navigation
    public AppUser? CreatedByUser { get; set; }
    public List<Grant> Grants { get; set; } = new();
    public List<RoleTemplateGrant> RoleTemplateGrants { get; set; } = new();
}
```

**Step 3: Create Grant entity**

```csharp
// Models/Grant.cs
namespace ShiftManager.Models;

public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GrantTypeId { get; set; }

    // Hierarchical Scope (one or more set depending on scope level)
    public int? ProjectId { get; set; }
    public int? AreaId { get; set; }
    public int? MoleculeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }

    // Capabilities
    public bool CanOwn { get; set; }   // Can perform the action
    public bool CanGive { get; set; }  // Can grant to others

    // Audit
    public int? GrantedByUserId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsAutoGrant { get; set; }  // true = from role template

    // Navigation
    public AppUser User { get; set; } = null!;
    public GrantType GrantType { get; set; } = null!;
    public AppUser? GrantedByUser { get; set; }
    public Project? Project { get; set; }
    public Area? Area { get; set; }
    public Molecule? Molecule { get; set; }
    public Department? Department { get; set; }
    public Company? Company { get; set; }
    public JobType? JobType { get; set; }
}
```

**Step 4: Create RoleTemplate entity**

```csharp
// Models/RoleTemplate.cs
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class RoleTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "BRDirector", "AlhutLead"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public RoleScopeLevel ScopeLevel { get; set; }
    public bool IsSystem { get; set; }  // Built-in vs custom
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<RoleTemplateGrant> AutoGrants { get; set; } = new();
    public List<UserRoleAssignment> UserRoles { get; set; } = new();
}
```

**Step 5: Create RoleTemplateGrant entity**

```csharp
// Models/RoleTemplateGrant.cs
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class RoleTemplateGrant
{
    public int Id { get; set; }
    public int RoleTemplateId { get; set; }
    public int GrantTypeId { get; set; }
    public bool CanOwn { get; set; }
    public bool CanGive { get; set; }
    public GrantScopeMode ScopeMode { get; set; }
    public bool IsOverride { get; set; }  // Owner modified default

    public RoleTemplate RoleTemplate { get; set; } = null!;
    public GrantType GrantType { get; set; } = null!;
}

public enum GrantScopeMode
{
    SameAsRole = 0,        // Grant scope = role scope
    ExpandToMolecule = 1,  // Expand to molecule (shift assignment)
    ExpandToArea = 2,      // Expand to area (Katzin, Hakam)
    Custom = 3             // Explicit scope
}
```

**Step 6: Create UserRoleAssignment entity (renamed from UserRole to avoid conflict)**

```csharp
// Models/UserRoleAssignment.cs
namespace ShiftManager.Models;

public class UserRoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoleTemplateId { get; set; }

    // Scope of this role assignment
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public int? MoleculeId { get; set; }
    public int? AreaId { get; set; }
    public int? JobTypeId { get; set; }

    // Audit
    public int AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation
    public AppUser User { get; set; } = null!;
    public RoleTemplate RoleTemplate { get; set; } = null!;
    public AppUser AssignedByUser { get; set; } = null!;
    public Company? Company { get; set; }
    public Department? Department { get; set; }
    public Molecule? Molecule { get; set; }
    public Area? Area { get; set; }
    public JobType? JobType { get; set; }
}
```

**Step 7: Run migration**

```bash
dotnet ef migrations add AddGrantSystem
dotnet ef database update
```

**Step 8: Commit**

```bash
git add Models/GrantType.cs Models/Grant.cs Models/RoleTemplate.cs Models/RoleTemplateGrant.cs Models/UserRoleAssignment.cs Models/Support/GrantCategory.cs Models/Support/GrantScopeLevel.cs Models/Support/RoleScopeLevel.cs
git commit -m "feat: add complete grant system entities

- GrantType with 107 built-in grants
- Grant for user-specific permissions
- RoleTemplate for 11 role definitions
- RoleTemplateGrant for auto-grant mappings
- UserRoleAssignment for role assignments

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.5: Create Settings Hierarchy Entities

**Files:**
- Create: `Models/AreaSettings.cs`
- Create: `Models/MoleculeSettings.cs`
- Create: `Models/CompanySettings.cs`

**Step 1: Create settings entities**

```csharp
// Models/AreaSettings.cs
namespace ShiftManager.Models;

public class AreaSettings
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int DefaultRestHours { get; set; } = 11;
    public int DefaultWeeklyCap { get; set; } = 60;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Area Area { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}

// Models/MoleculeSettings.cs
namespace ShiftManager.Models;

public class MoleculeSettings
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}

// Models/CompanySettings.cs
namespace ShiftManager.Models;

public class CompanySettings
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}
```

**Step 2: Run migration**

```bash
dotnet ef migrations add AddSettingsHierarchy
dotnet ef database update
```

**Step 3: Commit**

```bash
git add Models/AreaSettings.cs Models/MoleculeSettings.cs Models/CompanySettings.cs
git commit -m "feat: add hierarchical settings entities

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.6: Create Circle/Friends Entities

**Files:**
- Create: `Models/UserFriendship.cs`
- Create: `Models/Support/FriendshipStatus.cs`

**Step 1: Create entities**

```csharp
// Models/Support/FriendshipStatus.cs
namespace ShiftManager.Models.Support;

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

// Models/UserFriendship.cs
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class UserFriendship
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FriendId { get; set; }
    public FriendshipStatus Status { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public AppUser Friend { get; set; } = null!;
}
```

**Step 2: Run migration**

```bash
dotnet ef migrations add AddUserFriendship
dotnet ef database update
```

**Step 3: Commit**

```bash
git add Models/UserFriendship.cs Models/Support/FriendshipStatus.cs
git commit -m "feat: add Circle/Friends system entities

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.7: Create Smart Task System Entities

**Files:**
- Create: `Models/SetupTask.cs`
- Create: `Models/Support/SetupTaskType.cs`
- Create: `Models/Support/SetupTaskStatus.cs`

**Step 1: Create entities**

```csharp
// Models/Support/SetupTaskType.cs
namespace ShiftManager.Models.Support;

public enum SetupTaskType
{
    AssignMoleculeAdmin = 0,
    AssignAlhutDirector = 1,
    AssignTextDirector = 2,
    AssignBRDirector = 3,
    AssignAlhutLead = 4,
    AssignTextLead = 5,
    AssignAssigners = 6,
    SetupShiftGroupings = 7,
    SetupDutyPrograms = 8,
    SetupShiftBlueprints = 9
}

// Models/Support/SetupTaskStatus.cs
namespace ShiftManager.Models.Support;

public enum SetupTaskStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Skipped = 3
}

// Models/SetupTask.cs
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class SetupTask
{
    public int Id { get; set; }
    public SetupTaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Context
    public int? MoleculeId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }

    // Suggested action
    public int? SuggestedUserId { get; set; }
    public string? SuggestionReason { get; set; }

    // Assignment
    public int AssignedToUserId { get; set; }
    public SetupTaskStatus Status { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }

    // Navigation
    public Molecule? Molecule { get; set; }
    public Company? Company { get; set; }
    public JobType? JobType { get; set; }
    public AppUser? SuggestedUser { get; set; }
    public AppUser AssignedToUser { get; set; } = null!;
    public AppUser? CompletedByUser { get; set; }
}
```

**Step 2: Run migration**

```bash
dotnet ef migrations add AddSetupTaskSystem
dotnet ef database update
```

**Step 3: Commit**

```bash
git add Models/SetupTask.cs Models/Support/SetupTaskType.cs Models/Support/SetupTaskStatus.cs
git commit -m "feat: add Smart Task system for onboarding

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 1.8: Update DbContext with New Entities

**Files:**
- Modify: `Data/AppDbContext.cs`

**Step 1: Add DbSets**

Add to `AppDbContext.cs`:

```csharp
// Hierarchy
public DbSet<Project> Projects => Set<Project>();
public DbSet<Area> Areas => Set<Area>();
public DbSet<Molecule> Molecules => Set<Molecule>();
public DbSet<Department> Departments => Set<Department>();
public DbSet<JobType> JobTypes => Set<JobType>();

// Shift Groupings
public DbSet<ShiftGrouping> ShiftGroupings => Set<ShiftGrouping>();
public DbSet<ShiftGroupingCompany> ShiftGroupingCompanies => Set<ShiftGroupingCompany>();
public DbSet<ShiftGroupingJobType> ShiftGroupingJobTypes => Set<ShiftGroupingJobType>();

// Grant System
public DbSet<GrantType> GrantTypes => Set<GrantType>();
public DbSet<Grant> Grants => Set<Grant>();
public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
public DbSet<RoleTemplateGrant> RoleTemplateGrants => Set<RoleTemplateGrant>();
public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

// Settings
public DbSet<AreaSettings> AreaSettings => Set<AreaSettings>();
public DbSet<MoleculeSettings> MoleculeSettings => Set<MoleculeSettings>();
public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();

// Circle/Friends
public DbSet<UserFriendship> UserFriendships => Set<UserFriendship>();

// Smart Tasks
public DbSet<SetupTask> SetupTasks => Set<SetupTask>();
```

**Step 2: Add OnModelCreating configurations**

Add entity configurations for indexes, relationships, and constraints.

**Step 3: Run migration**

```bash
dotnet ef migrations add UpdateDbContextWithAllEntities
dotnet ef database update
```

**Step 4: Commit**

```bash
git add Data/AppDbContext.cs
git commit -m "feat: update DbContext with all v3.0 entities

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Phase 2: Core Domain Services

### Task 2.1: Create HierarchyService

**Files:**
- Create: `Services/HierarchyService.cs`
- Create: `Services/IHierarchyService.cs`

**Purpose:** Navigate and query the organizational hierarchy.

**Key Methods:**
- `GetProjectAsync(int projectId)`
- `GetAreasAsync(int projectId)`
- `GetMoleculesAsync(int areaId)`
- `GetCompaniesAsync(int moleculeId)`
- `GetDepartmentsAsync(int moleculeId)`
- `GetUserHierarchyContextAsync(int userId)` - Returns full path

---

### Task 2.2: Create JobTypeService

**Files:**
- Create: `Services/JobTypeService.cs`
- Create: `Services/IJobTypeService.cs`

**Purpose:** Manage JobTypes and user assignments.

**Key Methods:**
- `GetJobTypesAsync(int areaId)`
- `GetUserJobTypeAsync(int userId)`
- `AssignJobTypeAsync(int userId, int jobTypeId)`
- `ChangeJobTypeAsync(int userId, int newJobTypeId)` - Handles grant removal

---

### Task 2.3: Create GrantService

**Files:**
- Create: `Services/GrantService.cs`
- Create: `Services/IGrantService.cs`

**Purpose:** Check and manage grants.

**Key Methods:**
- `HasGrantAsync(int userId, string grantKey, scope)`
- `GetUserGrantsAsync(int userId)`
- `GrantAsync(int userId, int grantTypeId, scope, grantedBy)`
- `RevokeAsync(int grantId)`
- `ApplyAutoGrantsAsync(int userId, int roleTemplateId)` - From role

---

### Task 2.4: Create RoleService

**Files:**
- Create: `Services/RoleService.cs`
- Create: `Services/IRoleService.cs`

**Purpose:** Manage role assignments and auto-grants.

**Key Methods:**
- `AssignRoleAsync(int userId, int roleTemplateId, scope)`
- `RemoveRoleAsync(int userRoleId)`
- `GetUserRolesAsync(int userId)`
- `GetRoleTemplatesAsync()`

---

### Task 2.5: Create ShiftGroupingService

**Files:**
- Create: `Services/ShiftGroupingService.cs`
- Create: `Services/IShiftGroupingService.cs`

**Purpose:** Manage shift groupings for Alhut/Text shifts.

**Key Methods:**
- `GetGroupingsAsync(int moleculeId)`
- `CreateGroupingAsync(moleculeId, name, companyIds, jobTypeIds)`
- `UpdateGroupingAsync(groupingId, ...)`
- `GetUsersInGroupingAsync(int groupingId)`

---

## Phase 3: Authentication & Claims

### Task 3.1: Update User Claims

**Files:**
- Modify: `Services/AuthenticationProvider.cs` (or equivalent)
- Modify: Claims creation logic

**Step 1: Add new claims to user principal**

```csharp
// Add these claims at login:
new Claim("MoleculeId", moleculeId.ToString()),
new Claim("AreaId", areaId.ToString()),
new Claim("ProjectId", projectId.ToString()),
new Claim("JobTypeId", jobTypeId?.ToString() ?? ""),
new Claim("DepartmentId", departmentId?.ToString() ?? ""),
new Claim("IsWorkforce", (companyId.HasValue).ToString()),
new Claim("IsTech", (departmentId.HasValue).ToString()),
```

---

### Task 3.2: Create CurrentUserService

**Files:**
- Create: `Services/CurrentUserService.cs`
- Create: `Services/ICurrentUserService.cs`

**Purpose:** Easy access to current user's context.

```csharp
public interface ICurrentUserService
{
    int UserId { get; }
    int? CompanyId { get; }
    int? DepartmentId { get; }
    int MoleculeId { get; }
    int AreaId { get; }
    int ProjectId { get; }
    int? JobTypeId { get; }
    bool IsWorkforce { get; }
    bool IsTech { get; }
}
```

---

## Phase 4: Grant Authorization System

### Task 4.1: Create GrantAuthorizationHandler

**Files:**
- Create: `Authorization/GrantRequirement.cs`
- Create: `Authorization/GrantAuthorizationHandler.cs`

**Purpose:** Policy-based authorization using grants.

```csharp
// Usage in Razor Page:
[Authorize(Policy = "Grant:AssignAlhutShifts")]
public class AssignShiftsModel : PageModel { }
```

---

### Task 4.2: Create Grant Policies

**Files:**
- Modify: `Program.cs`

**Step 1: Register grant policies dynamically**

```csharp
// In Program.cs
var grantTypes = await LoadGrantTypesAsync();
foreach (var grant in grantTypes)
{
    options.AddPolicy($"Grant:{grant.Key}", policy =>
        policy.Requirements.Add(new GrantRequirement(grant.Key)));
}
```

---

### Task 4.3: Create GrantTagHelper

**Files:**
- Create: `TagHelpers/RequireGrantTagHelper.cs`

**Purpose:** Conditionally render UI based on grants.

```html
<require-grant key="AssignAlhutShifts">
    <button>Assign Shift</button>
</require-grant>
```

---

## Phase 5: Admin UI - Hierarchy Management

### Task 5.1: Create Organization Structure Page

**Files:**
- Create: `Pages/Admin/Organization/Index.cshtml`
- Create: `Pages/Admin/Organization/Index.cshtml.cs`

**Purpose:** Visual hierarchy browser with tree view.

---

### Task 5.2: Create Molecule Management Pages

**Files:**
- Create: `Pages/Admin/Molecules/Index.cshtml`
- Create: `Pages/Admin/Molecules/Create.cshtml`
- Create: `Pages/Admin/Molecules/Edit.cshtml`

---

### Task 5.3: Create Company Management Pages

**Files:**
- Create: `Pages/Admin/Companies/Index.cshtml`
- Create: `Pages/Admin/Companies/Create.cshtml`
- Create: `Pages/Admin/Companies/Edit.cshtml`

---

### Task 5.4: Create Department Management Pages

**Files:**
- Create: `Pages/Admin/Departments/Index.cshtml`
- Create: `Pages/Admin/Departments/Create.cshtml`
- Create: `Pages/Admin/Departments/Edit.cshtml`

---

### Task 5.5: Create JobType Management Pages

**Files:**
- Create: `Pages/Admin/JobTypes/Index.cshtml`
- Create: `Pages/Admin/JobTypes/Create.cshtml`
- Create: `Pages/Admin/JobTypes/Edit.cshtml`

---

### Task 5.6: Create ShiftGrouping Management Pages

**Files:**
- Create: `Pages/Admin/ShiftGroupings/Index.cshtml`
- Create: `Pages/Admin/ShiftGroupings/Create.cshtml`
- Create: `Pages/Admin/ShiftGroupings/Edit.cshtml`

---

## Phase 6: User Management UI

### Task 6.1: Update User List Page

**Files:**
- Modify: `Pages/Users/Index.cshtml`
- Modify: `Pages/Users/Index.cshtml.cs`

**Changes:**
- Add JobType column/filter
- Add Department column (for Tech users)
- Add Molecule filter
- Show user's grants count

---

### Task 6.2: Update User Edit Page

**Files:**
- Modify: `Pages/Users/Edit.cshtml`
- Modify: `Pages/Users/Edit.cshtml.cs`

**Changes:**
- Add JobType selector (workforce only)
- Add Department selector (tech only)
- Add role assignment section
- Add grants management section

---

### Task 6.3: Create Grant Management Page

**Files:**
- Create: `Pages/Admin/Grants/Index.cshtml`
- Create: `Pages/Admin/Grants/UserGrants.cshtml`
- Create: `Pages/Admin/Grants/AssignGrant.cshtml`

---

### Task 6.4: Create Role Assignment Page

**Files:**
- Create: `Pages/Admin/Roles/Index.cshtml`
- Create: `Pages/Admin/Roles/AssignRole.cshtml`

---

## Phase 7: Shift System Refactor

### Task 7.1: Update ShiftBlueprint Entity

**Files:**
- Modify: `Models/ShiftType.cs` (rename to ShiftBlueprint)

**Changes:**
- Add `MoleculeId` FK (scope to molecule)
- Add `JobTypeId` FK (null for tech shifts)
- Add `ShiftGroupingId` FK (null for BR/Hakam)
- Add `TechShiftType` string (for Hanava, Delta, etc.)

---

### Task 7.2: Update ShiftProgram Entity

**Files:**
- Modify: `Models/ShiftProgram.cs`

**Changes:**
- Add `JobTypeId` FK
- Add `ShiftGroupingId` FK
- Add `TechShiftType` string

---

### Task 7.3: Create Shift Assignment Logic by Scope

**Files:**
- Create: `Services/ShiftAssignmentService.cs`

**Key Methods:**
- `GetEligibleUsersForShiftAsync(shiftInstance)` - Based on JobType/Grouping
- `ValidateShiftAssignmentAsync(userId, shiftInstance)` - Check eligibility
- `AssignShiftAsync(userId, shiftInstanceId, assignedBy)`

---

## Phase 8: Calendar System Integration

### Task 8.1: Update Shift Calendar

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml`
- Modify: `Pages/Calendar/Shifts.cshtml.cs`

**Changes:**
- Add JobType filter
- Add ShiftGrouping filter
- Show only user's eligible shifts + friends' shifts

---

### Task 8.2: Update Chore Calendar

**Files:**
- Modify: `Pages/Calendar/Chores.cshtml`

**Changes:**
- Scope to Molecule (not Company)
- Show all users in molecule

---

### Task 8.3: Create Duty Calendar

**Files:**
- Create: `Pages/Calendar/Duties.cshtml`
- Create: `Pages/Calendar/Duties.cshtml.cs`

**Purpose:** Area-wide calendar for Hakam and Katzin on-call.

---

### Task 8.4: Update Vacation Calendar

**Files:**
- Modify: `Pages/Calendar/Vacations.cshtml`

**Changes:**
- Show company vacations + friends' vacations
- Add JobType filter (optional)

---

## Phase 9: Circle/Friends System

### Task 9.1: Create Friends List Page

**Files:**
- Create: `Pages/Friends/Index.cshtml`
- Create: `Pages/Friends/Index.cshtml.cs`

**Features:**
- List current friends
- Pending requests (incoming/outgoing)
- Search users to add

---

### Task 9.2: Create Friend Request Flow

**Files:**
- Create: `Services/FriendshipService.cs`
- Create: `Pages/Friends/Request.cshtml`

**Key Methods:**
- `SendRequestAsync(userId, friendId)`
- `AcceptRequestAsync(friendshipId)`
- `RejectRequestAsync(friendshipId)`
- `RemoveFriendAsync(friendshipId)`

---

### Task 9.3: Integrate Friends into Calendars

**Files:**
- Modify calendar services to include friend data

**Logic:**
- Friends can see each other's shifts, vacations, chores
- Cross-molecule visibility via friendship

---

## Phase 10: Settings Hierarchy

### Task 10.1: Create Settings Service

**Files:**
- Create: `Services/SettingsService.cs`
- Create: `Services/ISettingsService.cs`

**Key Methods:**
- `GetEffectiveSettingsAsync(companyId)` - Cascade resolution
- `UpdateAreaSettingsAsync(areaId, settings)`
- `UpdateMoleculeSettingsAsync(moleculeId, settings)`
- `UpdateCompanySettingsAsync(companyId, settings)`

---

### Task 10.2: Create Settings UI

**Files:**
- Create: `Pages/Admin/Settings/Area.cshtml`
- Create: `Pages/Admin/Settings/Molecule.cshtml`
- Create: `Pages/Admin/Settings/Company.cshtml`

---

## Phase 11: Smart Task System

### Task 11.1: Create SetupTaskService

**Files:**
- Create: `Services/SetupTaskService.cs`
- Create: `Services/ISetupTaskService.cs`

**Key Methods:**
- `GenerateTasksForMoleculeAsync(moleculeId)`
- `GenerateTasksForCompanyAsync(companyId)`
- `GetPendingTasksAsync(userId)`
- `CompleteTaskAsync(taskId, userId)`

---

### Task 11.2: Create Setup Tasks Dashboard

**Files:**
- Create: `Pages/Admin/SetupTasks/Index.cshtml`
- Create: `Pages/Admin/SetupTasks/Index.cshtml.cs`

**Features:**
- List pending tasks for current admin
- Quick actions to complete tasks
- Progress tracking

---

## Phase 12: Data Migration & Seed Data

### Task 12.1: Create Seed Data for Grants

**Files:**
- Create: `Data/SeedData/GrantTypeSeed.cs`

**Purpose:** Seed all 107 built-in grants.

---

### Task 12.2: Create Seed Data for Role Templates

**Files:**
- Create: `Data/SeedData/RoleTemplateSeed.cs`

**Purpose:** Seed 11 role templates with auto-grant mappings.

---

### Task 12.3: Create Seed Data for Shifty Organization

**Files:**
- Create: `Data/SeedData/ShiftyOrganizationSeed.cs`

**Purpose:** Seed the real-world structure:
- Project: Shifty
- Area: 190
- Molecules: Oren, Ella, Gefen, Harava, Shaked (Workforce), Shikma (Tech), Shiklut, NOC (Helper)
- Companies and Departments
- JobTypes: Alhut, BR, Text, Hakam

---

### Task 12.4: Create Migration Script for Existing Data

**Files:**
- Create: `Data/Migrations/MigrateExistingData.cs`

**Purpose:** Map existing Companies to new structure.

---

## Phase 13: Testing & Validation

### Task 13.1: Test Grant Authorization

**Test Cases:**
- User with grant can access protected resource
- User without grant is denied
- Grant scope is respected (company vs molecule vs area)
- Auto-grants from roles work correctly

---

### Task 13.2: Test Role Assignments

**Test Cases:**
- Assigning role adds auto-grants
- Removing role removes auto-grants
- Role scope is enforced

---

### Task 13.3: Test Shift Assignment by JobType

**Test Cases:**
- Only users with matching JobType can be assigned
- ShiftGrouping respects company membership
- BR shifts are molecule-wide
- Hakam shifts are area-wide

---

### Task 13.4: Test Circle/Friends Visibility

**Test Cases:**
- Friends can see each other's shifts
- Non-friends cannot see cross-molecule data
- Pending requests don't grant visibility

---

### Task 13.5: Test Settings Cascade

**Test Cases:**
- Area settings apply when no overrides
- Molecule override takes precedence
- Company override takes precedence over molecule

---

## Implementation Priority

### Critical Path (Must complete in order):
1. Phase 1 (Database Schema) - Foundation
2. Phase 2 (Core Services) - Business logic
3. Phase 3 (Authentication) - Security
4. Phase 4 (Authorization) - Access control
5. Phase 12 (Seed Data) - Initial data

### Parallel Work (Can proceed independently):
- Phase 5-6 (Admin UI) - After Phase 4
- Phase 7-8 (Shift/Calendar) - After Phase 2
- Phase 9 (Friends) - After Phase 2
- Phase 10 (Settings) - After Phase 2
- Phase 11 (Smart Tasks) - After Phase 5

### Final Validation:
- Phase 13 (Testing) - After all phases

---

## Risk Mitigation

### Data Migration Risks:
- **Risk:** Existing data loss during migration
- **Mitigation:** Create backup before migration, use transactions, test on staging

### Breaking Changes:
- **Risk:** Existing functionality breaks
- **Mitigation:** Feature flags for gradual rollout, parallel old/new code paths

### Performance:
- **Risk:** Grant checking adds latency
- **Mitigation:** Cache user grants, use efficient indexes

---

## Estimated Task Count

| Phase | Tasks | Complexity |
|-------|-------|------------|
| 1. Database | 8 | Medium |
| 2. Services | 5 | High |
| 3. Auth | 2 | Medium |
| 4. Authorization | 3 | High |
| 5. Admin UI | 6 | Medium |
| 6. User UI | 4 | Medium |
| 7. Shifts | 3 | High |
| 8. Calendar | 4 | Medium |
| 9. Friends | 3 | Low |
| 10. Settings | 2 | Low |
| 11. Tasks | 2 | Low |
| 12. Seed Data | 4 | Medium |
| 13. Testing | 5 | Medium |
| **Total** | **51** | - |

---

## Success Criteria

1. All 107 grants are seeded and functional
2. 11 role templates with auto-grants work correctly
3. Hierarchy navigation (Project → Area → Molecule → Company/Department → User) is complete
4. JobType correctly scopes shift eligibility
5. ShiftGroupings combine companies for shift scheduling
6. Circle/Friends enables cross-molecule visibility
7. Settings cascade correctly
8. Smart Tasks guide new entity setup
9. All existing functionality preserved
10. Performance acceptable (grant check < 50ms)

---

**Plan complete and saved to `docs/plans/2026-01-25-v3-ui-integration-plan.md`.**

**Two execution options:**

1. **Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

2. **Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

**Which approach?**
