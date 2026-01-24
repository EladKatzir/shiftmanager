# Organizational Hierarchy Redesign - Complete Design Document

**Date:** 2026-01-24
**Status:** Approved - Ready for Implementation
**Version:** 1.0

---

## Executive Summary

This document describes the complete redesign of ShiftManager's organizational model from a single-level "Company" system to a 6-level hierarchy with explicit grant-based permissions, separated duty types, and JobType-based scheduling.

**Current State:**
- Single organizational level: Company
- Role-based permissions: Owner/Manager/Director/Employee enum
- Free-text JobTitle and Department fields
- Confused OnDuty system (mixes Responsibility vs Coverage)
- Company-scoped calendars only

**Target State:**
- 6-level hierarchy: Project → Area → Molecule → Department → Company → User
- Grant-based permissions with hierarchical actions
- JobType entity with ManagementPattern attribute
- Separated Duty system (Responsibility vs Coverage)
- Multi-level calendars with JobType filtering

---

## Table of Contents

1. [Organizational Hierarchy](#1-organizational-hierarchy)
2. [Grant System](#2-grant-system)
3. [JobType Entity](#3-jobtype-entity)
4. [Duty System](#4-duty-system)
5. [Programs & Blueprints](#5-programs--blueprints)
6. [Calendar System](#6-calendar-system)
7. [Smart Task System](#7-smart-task-system)
8. [Admin UI Organization](#8-admin-ui-organization)
9. [Email/ADFS/Game Configuration](#9-emailadfsgame-configuration)
10. [Seed Data Structure](#10-seed-data-structure)
11. [Migration Strategy](#11-migration-strategy)

---

## 1. Organizational Hierarchy

### 1.1 Hierarchy Levels

```
Project (Top Level)
  └─ Area
      └─ Molecule
          └─ Department
              └─ Company
                  └─ User
```

**Real-World Example:**
```
Shifty (שיפטי) - Project
  └─ 190 - Area
      └─ Oren (אורן) - Molecule
          └─ Defence and Manuver (הגנה ותמרון) - Department
              ├─ Radio (טקטי) - Company
              ├─ North (צפון) - Company
              ├─ City (העיר) - Company
              └─ Hir (ח'י"ר) - Company
```

### 1.2 Purpose of Each Level

| Level | Purpose | Permission Tier | Example |
|-------|---------|-----------------|---------|
| **Project** | Top-level organizational unit | (Reserved for future) | Shifty |
| **Area** | Permission boundary for Area Admins | Area Admin grant scope | 190 |
| **Molecule** | Operational boundary for chores and duties | Molecule Admin grant scope | Oren |
| **Department** | Policy and JobType container | Department Admin grant scope | Defence and Manuver |
| **Company** | Team/unit with shared calendar | Company Manager/Director grants | Radio, North, City, Hir |
| **User** | Individual employee | N/A | Individual person |

### 1.3 Database Schema

```csharp
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty; // Hebrew
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public List<Area> Areas { get; set; } = new();
}

public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public List<Molecule> Molecules { get; set; } = new();
}

public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Area Area { get; set; } = null!;
    public List<Department> Departments { get; set; } = new();
}

public class Department
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<Company> Companies { get; set; } = new();
    public List<JobType> JobTypes { get; set; } = new();
}

public class Company
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}

public class AppUser
{
    public int Id { get; set; }

    // Primary organizational association
    public int CompanyId { get; set; }

    // Cached hierarchy (for performance)
    public int DepartmentId { get; set; }
    public int MoleculeId { get; set; }
    public int AreaId { get; set; }
    public int ProjectId { get; set; }

    // Job & Rank
    public int? PrimaryJobTypeId { get; set; }
    public MilitaryRank Rank { get; set; }

    // ... existing fields (Email, PasswordHash, etc.)

    // DEPRECATED (will be removed after migration)
    public string? Department { get; set; }  // Old free-text field
    public UserRole Role { get; set; }       // Replaced by Grants

    // Navigation
    public Company Company { get; set; } = null!;
    public JobType? PrimaryJobType { get; set; }
    public List<Grant> Grants { get; set; } = new();
}
```

### 1.4 Hierarchy Context Service

```csharp
public interface IHierarchyContext
{
    // Cached values (from user claims)
    int? CompanyId { get; }
    int? DepartmentId { get; }

    // Set context (for Owner multi-company management)
    Task SetCompanyAsync(int companyId);
    Task SetDepartmentAsync(int departmentId);

    // Derived values (query when needed)
    Task<int> GetMoleculeIdAsync();
    Task<int> GetAreaIdAsync();
    Task<int> GetProjectIdAsync();

    // Convenience methods
    Task<Department> GetDepartmentAsync();
    Task<Molecule> GetMoleculeAsync();
    Task<Area> GetAreaAsync();
    Task<Project> GetProjectAsync();
}

public class HierarchyContext : IHierarchyContext
{
    private readonly IHttpContextAccessor _httpContext;
    private readonly AppDbContext _db;

    public int? CompanyId => GetClaimInt("CompanyId");
    public int? DepartmentId => GetClaimInt("DepartmentId");

    public async Task<int> GetMoleculeIdAsync()
    {
        var deptId = DepartmentId ?? throw new InvalidOperationException("No department context");
        var dept = await _db.Departments.FindAsync(deptId);
        return dept.MoleculeId;
    }

    // ... similar for Area, Project
}
```

---

## 2. Grant System

### 2.1 Grant Model

```csharp
public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }

    // Scope
    public ScopeType ScopeType { get; set; }  // Project/Area/Molecule/Department/Company
    public int ScopeId { get; set; }

    // Job dimension (null = all JobTypes)
    public int? JobTypeId { get; set; }

    // Action
    public GrantAction Action { get; set; }

    // Timebox (optional)
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    // Audit
    public int GrantedBy { get; set; }
    public DateTime GrantedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? RevokedBy { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public AppUser GrantedByUser { get; set; } = null!;
    public JobType? JobType { get; set; }
}

public enum ScopeType
{
    Project = 1,
    Area = 2,
    Molecule = 3,
    Department = 4,
    Company = 5
}

public enum GrantAction
{
    SystemAdmin = 0,   // Owner only - full system access
    ManageGrants = 1,  // Approve/revoke grants within scope
    ManageUsers = 2,   // Create/edit/delete users (JobType-scoped)
    Configure = 3,     // Edit shift types, programs, department settings
    Edit = 4,          // Assign/unassign shifts, manage schedules
    Assign = 5,        // Quick-assign shifts only (limited Edit)
    View = 6           // Read-only access
}
```

### 2.2 Grant Hierarchy

```
SystemAdmin (0)
  └─ ManageGrants (1)
      └─ ManageUsers (2)
          └─ Configure (3)
              └─ Edit (4)
                  └─ Assign (5)
                      └─ View (6)
```

**Inheritance:** User with `ManageGrants` automatically has `ManageUsers`, `Configure`, `Edit`, `Assign`, and `View`.

### 2.3 JobType-Scoped User Management

`ManageUsers` action is JobType-scoped:

```csharp
// Grant example: BR Job Director in Radio Company
Grant
{
    UserId = 42,
    ScopeType = ScopeType.Company,
    ScopeId = 1,  // Radio Company
    JobTypeId = 1,  // BR JobType
    Action = GrantAction.ManageUsers
}

// This user can:
// ✅ Create/edit/delete users with PrimaryJobTypeId = 1 (BR) in Radio Company
// ❌ Cannot manage users with PrimaryJobTypeId = 2 (Producer)
// ❌ Cannot manage users with PrimaryJobTypeId = 3 (Hakam)
```

### 2.4 Grant Examples

```csharp
// Example 1: Area Admin (full area control)
new Grant
{
    UserId = 1,
    ScopeType = ScopeType.Area,
    ScopeId = 1,  // Area 190
    JobTypeId = null,  // All JobTypes
    Action = GrantAction.Configure
}
// Can configure all departments/molecules/companies in Area 190

// Example 2: Department Job Director (BR only)
new Grant
{
    UserId = 5,
    ScopeType = ScopeType.Department,
    ScopeId = 1,  // Defence and Manuver
    JobTypeId = 1,  // BR
    Action = GrantAction.ManageUsers
}
// Can manage BR employees across all companies in Defence and Manuver

// Example 3: Company Job Lead (BR in Radio)
new Grant
{
    UserId = 10,
    ScopeType = ScopeType.Company,
    ScopeId = 1,  // Radio
    JobTypeId = 1,  // BR
    Action = GrantAction.Edit
}
// Can assign BR shifts in Radio Company only

// Example 4: Molecule Admin
new Grant
{
    UserId = 3,
    ScopeType = ScopeType.Molecule,
    ScopeId = 1,  // Oren
    JobTypeId = null,  // All JobTypes
    Action = GrantAction.Configure
}
// Can configure all departments/companies in Oren Molecule
// Default manager for employees without specific Job Directors
```

### 2.5 Molecule Boundary Rule

**Critical constraint:** Users cannot receive `Edit` grants outside their home Molecule.

```csharp
// Validation in Grant creation:
public async Task<bool> ValidateGrantAsync(Grant grant)
{
    if (grant.Action <= GrantAction.Edit)
    {
        var user = await _db.Users.FindAsync(grant.UserId);
        var grantMoleculeId = await GetMoleculeIdForScope(grant.ScopeType, grant.ScopeId);

        if (user.MoleculeId != grantMoleculeId)
        {
            return false; // Cannot grant Edit outside user's Molecule
        }
    }

    return true;
}
```

**Reason:** Prevents cross-molecule interference. Users can only actively manage schedules within their own operational unit.

---

## 3. JobType Entity

### 3.1 JobType Model

```csharp
public class JobType
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ManagementPattern ManagementPattern { get; set; }
    public bool IsOnCallBased { get; set; }
    public string? Color { get; set; }  // For UI badges
    public string? CertificationsJson { get; set; }  // Future: required certs
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
    public List<Grant> Grants { get; set; } = new();
    public List<ShiftTypeJobTypeAssignment> AssignedShiftTypes { get; set; } = new();
}

public enum ManagementPattern
{
    Hierarchical,      // BR, Producer: Department Director → Company Leads
    DirectDepartment   // Hakam: Department Director only, no Company Leads
}
```

### 3.2 ManagementPattern Explained

**Hierarchical (BR, Producer):**
```
Department Job Director (ManageUsers at Department scope)
  └─ Company Job Lead (Edit at Company scope)
      └─ Employees
```

**DirectDepartment (Hakam):**
```
Department Job Director (ManageUsers at Department scope)
  └─ Employees (no intermediate Company Leads)
```

However, Hakam employees can report to another JobType's Company Job Lead via `CompanyJobTypeManager` configuration.

### 3.3 Cross-JobType Reporting (CompanyJobTypeManager)

```csharp
public class CompanyJobTypeManager
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int JobTypeId { get; set; }           // Delegated JobType (e.g., Hakam)
    public int ManagerJobTypeId { get; set; }    // Managing JobType (e.g., BR)
    public int ConfiguredBy { get; set; }
    public DateTime ConfiguredAt { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
    public JobType ManagerJobType { get; set; } = null!;
}

// Example: Hakam employees in Radio report to BR Job Lead in Radio
new CompanyJobTypeManager
{
    CompanyId = 1,  // Radio
    JobTypeId = 3,  // Hakam
    ManagerJobTypeId = 1,  // BR
    ConfiguredBy = moleculeAdminId
}

// Result: BR Job Lead in Radio can manage Hakam employees' schedules
```

### 3.4 Seed JobTypes

```csharp
new JobType { Name = "BR", DisplayName = "ב\"ר", ManagementPattern = Hierarchical, IsOnCallBased = false, Color = "#1565C0" }
new JobType { Name = "Producer", DisplayName = "אלחוטן", ManagementPattern = Hierarchical, IsOnCallBased = false, Color = "#00695C" }
new JobType { Name = "Hakam", DisplayName = "חק\"מ", ManagementPattern = DirectDepartment, IsOnCallBased = true, Color = "#E65100" }
```

---

## 4. Duty System

### 4.1 DutyRole Model (Replaces OnDutyType)

```csharp
public class DutyRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;  // "On-Duty Lead", "Hakam On-Call"
    public string DisplayName { get; set; } = string.Empty;
    public int? JobTypeId { get; set; }  // null = all jobs (On-Duty Lead), set = job-specific (Hakam On-Call)
    public bool RequiresTimeBlocks { get; set; }  // false = all-day, true = time-specific
    public TimeOnly? DefaultStartTime { get; set; }
    public TimeOnly? DefaultEndTime { get; set; }
    public string? EligibilityRuleJson { get; set; }  // {"MinimumRank": "Officer", "MinimumGrantLevel": "Department"}
    public ScopeType DefaultScope { get; set; }  // Molecule
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public JobType? JobType { get; set; }
    public List<DutyAssignment> Assignments { get; set; } = new();
}
```

### 4.2 DutyAssignment Model (Unified)

```csharp
public class DutyAssignment
{
    public int Id { get; set; }
    public int DutyRoleId { get; set; }
    public int UserId { get; set; }

    // Scope
    public ScopeType ScopeType { get; set; }
    public int ScopeId { get; set; }

    // Date & Time
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }  // null if DutyRole.RequiresTimeBlocks = false
    public TimeOnly? EndTime { get; set; }

    // Backup
    public int? BackupUserId { get; set; }

    // Audit
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public int? CanceledBy { get; set; }

    // Navigation
    public DutyRole DutyRole { get; set; } = null!;
    public AppUser User { get; set; } = null!;
    public AppUser? BackupUser { get; set; }
}
```

### 4.3 Responsibility vs Coverage Duties

**Responsibility Duties (all-day, no time blocks):**
```csharp
new DutyRole
{
    Name = "On-Duty Lead",
    DisplayName = "מוביל תורנות",
    JobTypeId = null,  // All JobTypes eligible
    RequiresTimeBlocks = false,  // All-day duty
    DefaultScope = ScopeType.Molecule,
    EligibilityRuleJson = "{\"MinimumRank\": \"SegenMishne\", \"MinimumGrantLevel\": \"Department\"}"
}

// Assignment:
new DutyAssignment
{
    DutyRoleId = 1,
    UserId = 5,
    ScopeType = ScopeType.Molecule,
    ScopeId = 1,  // Oren
    Date = new DateOnly(2026, 1, 24),
    StartTime = null,  // All-day
    EndTime = null
}
```

**Coverage Duties (time-specific blocks):**
```csharp
new DutyRole
{
    Name = "Hakam On-Call",
    DisplayName = "חק\"מ בכוננות",
    JobTypeId = 3,  // Hakam only
    RequiresTimeBlocks = true,  // Time-specific
    DefaultStartTime = new TimeOnly(8, 0),
    DefaultEndTime = new TimeOnly(20, 0),
    DefaultScope = ScopeType.Molecule
}

// Assignment:
new DutyAssignment
{
    DutyRoleId = 2,
    UserId = 10,
    ScopeType = ScopeType.Molecule,
    ScopeId = 1,
    Date = new DateOnly(2026, 1, 24),
    StartTime = new TimeOnly(8, 0),  // Coverage block
    EndTime = new TimeOnly(20, 0)
}
```

### 4.4 Officer Rank Requirement

```csharp
public enum MilitaryRank
{
    // Enlisted / NCO (0-8)
    Turai = 0,              // טוראי
    RavTurai = 1,           // רב־טוראי
    Samal = 2,              // סמל
    SamalRishon = 3,        // סמל ראשון
    RavSamal = 4,           // רב־סמל
    RavSamalRishon = 5,     // רב־סמל ראשון
    RavSamalMitkadem = 6,   // רב־סמל מתקדם
    RavSamalBakhir = 7,     // רב־סמל בכיר
    RavNagad = 8,           // רב־נגד

    // Commissioned Officers (9-17)
    SegenMishne = 9,        // סגן־משנה  ← Officer rank starts here
    Segen = 10,             // סגן
    Seren = 11,             // סרן
    RavSeren = 12,          // רב סרן
    SganAluf = 13,          // סגן־אלוף
    AlufMishne = 14,        // אלוף משנה
    TatAluf = 15,           // תת־אלוף
    Aluf = 16,              // אלוף
    RavAluf = 17            // רב־אלוף
}

public static bool IsCommissionedOfficer(MilitaryRank rank)
{
    return rank >= MilitaryRank.SegenMishne;  // 9+
}

// Usage in DutyRole eligibility check:
public bool IsUserEligible(AppUser user, DutyRole role)
{
    var rules = JsonSerializer.Deserialize<EligibilityRules>(role.EligibilityRuleJson);

    // Check rank requirement
    if (rules.MinimumRank == "Officer" && !IsCommissionedOfficer(user.Rank))
        return false;

    // Check grant level requirement
    if (rules.MinimumGrantLevel == "Department" && !user.Grants.Any(g =>
        g.ScopeType <= ScopeType.Department && g.Action <= GrantAction.Configure))
        return false;

    return true;
}
```

**Important:** Rank only affects duty eligibility and future vacation rules. It does NOT affect Grant eligibility.

### 4.5 DutyProgram (Manual with Programs/Blueprints Support)

```csharp
public class DutyProgram
{
    public int Id { get; set; }
    public int DutyRoleId { get; set; }
    public ScopeType ScopeType { get; set; }
    public int ScopeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UserRotationJson { get; set; }  // ["UserId:5", "UserId:10", "UserId:15"]
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public DutyRole DutyRole { get; set; } = null!;
}

// Example: On-Duty Lead rotation for Oren Molecule
new DutyProgram
{
    DutyRoleId = 1,
    ScopeType = ScopeType.Molecule,
    ScopeId = 1,
    Name = "Oren Lead Rotation",
    UserRotationJson = "[5, 10, 15, 20]",  // 4 officers rotate
    IsActive = true
}
```

**No DutyMasterProgram initially (YAGNI)** - just DutyProgram with rotation list.

---

## 5. Programs & Blueprints

### 5.1 ShiftType (Blueprints) - Department-Scoped

```csharp
public class ShiftType
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }  // Changed from CompanyId
    public string Key { get; set; } = string.Empty;
    public string? CustomName { get; set; }
    public string? NameKey { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public List<ShiftTypeJobTypeAssignment> AssignedJobTypes { get; set; } = new();
}
```

### 5.2 ShiftTypeJobTypeAssignment (Many-to-Many)

```csharp
public class ShiftTypeJobTypeAssignment
{
    public int Id { get; set; }
    public int ShiftTypeId { get; set; }
    public int JobTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; }
    public int AssignedBy { get; set; }

    // Navigation
    public ShiftType ShiftType { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
}
```

### 5.3 Blueprint Creation Rules

**Rule 1: Must select at least one JobType**
```csharp
// UI validation:
if (selectedJobTypeIds.Count == 0)
{
    Error = "You must select at least one JobType for this blueprint";
    return Page();
}
```

**Rule 2: No auto-assignment**
- User sees unchecked checkboxes for all JobTypes in Department
- Must manually check which jobs can use this shift

**UI:**
```
Create Shift Blueprint - Defence and Manuver
─────────────────────────────────────────────

Shift Name (EN): Morning Shift
Shift Name (HE): משמרת בוקר
Start Time: [08:00]
End Time: [16:00]

Apply to JobTypes: *
[ ] BR (ב"ר)
[ ] Producer (אלחוטן)
[ ] Hakam (חק"מ)

⚠️ You must select at least one JobType

[Create]  [Cancel]
```

### 5.4 JobType Creation - Optional Shift Assignment

```
Create New JobType
──────────────────────────────────────

Step 1: Basic Information
  Name (EN): Analyst
  Name (HE): מנתח
  Management Pattern: ● Hierarchical  ○ DirectDepartment
  On-Call Based: [ ]

[Next: Assign to Shifts →]

──────────────────────────────────────

Step 2: Assign to Existing Shift Blueprints (Optional)

Which shifts should Analyst employees work?

[ ] Morning Shift (08:00-16:00)
    Currently used by: BR, Producer

[ ] Afternoon Shift (16:00-00:00)
    Currently used by: BR, Producer

ℹ️ You can skip this and assign shifts later

[Create JobType]  [← Back]
```

**No auto-assignment:** New JobType does NOT automatically get all Blueprints. User assigns manually.

### 5.5 ShiftProgram - JobType-Scoped

```csharp
public class ShiftProgram
{
    public int Id { get; set; }
    public int JobTypeId { get; set; }  // NEW: JobType-specific programs
    public int CompanyId { get; set; }  // Still company-scoped for instance
    public string Name { get; set; } = string.Empty;
    public string? ProgramDataJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public JobType JobType { get; set; } = null!;
    public Company Company { get; set; } = null!;
}

// Example: BR rotation in Radio Company
new ShiftProgram
{
    JobTypeId = 1,  // BR
    CompanyId = 1,  // Radio
    Name = "BR Standard Rotation",
    ProgramDataJson = "..." // Monday: 2× Morning, 1× Afternoon, etc.
}
```

When creating a Program for BR, user only sees Blueprints that have BR checked in `ShiftTypeJobTypeAssignment`.

---

## 6. Calendar System

### 6.1 Calendar Naming Pattern

**Format:** `Scope + JobType`

Examples:
- "Radio — BR" (Company Job Calendar)
- "Defence Dept — BR" (Department Job Rollup Calendar)
- "Oren — On-Duty Leads" (Molecule Duty Calendar)

### 6.2 Calendar Sidebar Navigation

```
📅 Scheduling

Calendars:
  ├─ 📊 Radio — BR (Company Calendar)
  ├─ 📊 Radio — Producer
  ├─ 📊 Radio — Hakam
  ├─ 📋 Defence Dept — BR (Department Rollup)
  ├─ 🎯 Oren — On-Duty Leads (Molecule Duties)
  └─ 🏖️ My Shifts (Personal Calendar)

Filters:
  ├─ Circle (Team/Friend Filter)
  ├─ Shift Types
  ├─ Chores
  └─ Vacations
```

**Collapsible sections:**
- "Company Calendars" (expand to show Radio—BR, Radio—Producer, etc.)
- "Department Rollups"
- "Duties"

### 6.3 Circle Functionality

**Dual implementation:**
1. **Dedicated Circle page** (`/Circle/Index`) - Manage friend list
2. **Calendar filter toggle** - Show/hide non-Circle members in calendar

User can add teammates to "Circle" (friends list), then toggle calendar filter to focus on Circle members only.

---

## 7. Smart Task System

### 7.1 Task Model

```csharp
public class Task
{
    public int Id { get; set; }
    public TaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Auto-suggested candidate
    public int? SuggestedUserId { get; set; }
    public string? SuggestionReason { get; set; }
    public string? AlternativeCandidatesJson { get; set; }  // Ranked list with scores

    // Grant details (for grant-creation tasks)
    public ScopeType? ScopeType { get; set; }
    public int? ScopeId { get; set; }
    public int? JobTypeId { get; set; }
    public GrantAction? Action { get; set; }

    // Task assignment
    public int AssignedTo { get; set; }
    public TaskStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public AppUser? SuggestedUser { get; set; }
    public AppUser AssignedToUser { get; set; } = null!;
}

public enum TaskType
{
    AssignMoleculeAdmin,      // Find Molecule Admin
    ConfigureJobTypes,        // Set up Department JobTypes
    AssignJobDirector,        // Find Department Job Director
    AssignJobLead,            // Find Company Job Lead
    ConfigureCompanyJobTypeManager,  // Set up cross-JobType reporting
    SetupDutyPrograms         // Create duty rotations
}

public enum TaskStatus
{
    Pending,      // Awaiting action
    Approved,     // Accepted auto-suggestion
    Modified,     // User picked alternative candidate
    Rejected      // User dismissed task
}
```

### 7.2 Candidate Scoring Algorithm

```csharp
public class CandidateScore
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public Dictionary<string, int> ScoreBreakdown { get; set; } = new();
}

public async Task<List<CandidateScore>> ScoreCandidatesAsync(
    TaskType taskType,
    ScopeType scopeType,
    int scopeId,
    int? jobTypeId = null)
{
    var candidates = await GetEligibleCandidatesAsync(scopeType, scopeId, jobTypeId);
    var scores = new List<CandidateScore>();

    foreach (var candidate in candidates)
    {
        var score = new CandidateScore { UserId = candidate.Id, UserName = candidate.FullName };

        // +30 points: Primary JobType match (if JobType-scoped task)
        if (jobTypeId.HasValue && candidate.PrimaryJobTypeId == jobTypeId)
        {
            score.ScoreBreakdown["JobType Match"] = 30;
        }

        // +25 points: Same scope (Company/Department)
        if (candidate.CompanyId == scopeId || candidate.DepartmentId == scopeId)
        {
            score.ScoreBreakdown["Same Scope"] = 25;
        }

        // +20 points: No existing grants (fresh candidate, no conflicts)
        if (!candidate.Grants.Any())
        {
            score.ScoreBreakdown["No Existing Grants"] = 20;
        }

        // +15 points max: Seniority (1 point per year)
        var yearsOfService = (DateTime.UtcNow - candidate.CreatedAt).TotalDays / 365;
        var seniorityPoints = Math.Min((int)yearsOfService, 15);
        score.ScoreBreakdown["Seniority"] = seniorityPoints;

        // +10 points: Senior officer rank (Seren+)
        if (candidate.Rank >= MilitaryRank.Seren)
        {
            score.ScoreBreakdown["Senior Officer"] = 10;
        }

        score.TotalScore = score.ScoreBreakdown.Values.Sum();
        scores.Add(score);
    }

    return scores.OrderByDescending(s => s.TotalScore).ToList();
}
```

### 7.3 One-Click Grant Approval

```csharp
public async Task<IActionResult> OnPostApproveTaskAsync(int taskId, int? selectedUserId = null)
{
    var task = await _db.Tasks.FindAsync(taskId);
    if (task == null) return NotFound();

    // Use suggested user or user-selected alternative
    var userId = selectedUserId ?? task.SuggestedUserId ?? throw new InvalidOperationException();

    // Create grant based on task details
    var grant = new Grant
    {
        UserId = userId,
        ScopeType = task.ScopeType!.Value,
        ScopeId = task.ScopeId!.Value,
        JobTypeId = task.JobTypeId,
        Action = task.Action!.Value,
        GrantedBy = GetCurrentUserId(),
        GrantedAt = DateTime.UtcNow
    };

    _db.Grants.Add(grant);

    // Update task status
    task.Status = selectedUserId.HasValue ? TaskStatus.Modified : TaskStatus.Approved;
    task.CompletedAt = DateTime.UtcNow;

    await _db.SaveChangesAsync();

    return RedirectToPage("/Admin/Tasks");
}
```

### 7.4 Task Creation Triggers

```csharp
// When new Molecule is created:
var task = new Task
{
    Type = TaskType.AssignMoleculeAdmin,
    Title = $"Assign Molecule Admin for {molecule.DisplayName}",
    Description = $"The new Molecule '{molecule.DisplayName}' needs an admin to manage its configuration.",
    ScopeType = ScopeType.Molecule,
    ScopeId = molecule.Id,
    Action = GrantAction.Configure,
    AssignedTo = areaAdminId  // Assign to Area Admin
};

var candidates = await ScoreCandidatesAsync(task.Type, ScopeType.Molecule, molecule.Id);
if (candidates.Any())
{
    task.SuggestedUserId = candidates[0].UserId;
    task.SuggestionReason = $"Top match: {candidates[0].UserName} (Score: {candidates[0].TotalScore})";
    task.AlternativeCandidatesJson = JsonSerializer.Serialize(candidates.Skip(1).Take(3));
}

_db.Tasks.Add(task);
```

---

## 8. Admin UI Organization

### 8.1 OwnerHub Structure

```
🔧 Owner Administration

🌍 Global System Configuration
  ├─ 📧 Email Configuration (SMTP settings only)
  ├─ 🔐 Griffin ADFS (global auth settings)
  ├─ 🎮 Game Configuration (global game rules)
  ├─ 🚩 Feature Flags
  ├─ 🌐 Language Management
  ├─ 💾 Database Console
  ├─ 💼 Backups & Data Lifecycle
  └─ 🏥 System Health

🏢 Multi-Company Management
  [Company Selector Dropdown: Radio (טקטי) ▼]
    ├─ Radio (טקטי)
    ├─ North (צפון)
    ├─ City (העיר)
    └─ Hir (ח'י"ר)

  Per-Selected-Company:
    ├─ 📧 Email Templates (company-specific templates)
    ├─ 📘 Blueprints (shift type definitions)
    ├─ 📅 Programs (weekly schedule templates)
    └─ 🎯 Master Programs (program compositions)
```

**Key Features:**
- **Company Selector** prominently displayed at top of "Multi-Company Management" section
- Clear visual separation: Global (no selector) vs Per-Company (requires selector)
- Owner selects company via dropdown → cookie stores CompanyId → context applies to Email Templates, Blueprints, Programs, MasterPrograms

### 8.2 Admin Navigation Sidebar

```
📅 My Shifty
  ├─ 🏠 Home
  ├─ 📅 Schedule
  └─ 📝 Requests

📊 Analytics & Monitoring
  ├─ 📊 Analytics Dashboard
  └─ 🔍 Audit Log

👥 People & Structure
  ├─ 🏗️ Organizational Structure
  ├─ 👥 Users
  ├─ 💼 JobTypes
  └─ 🎖️ Grants

📅 Scheduling
  ├─ 📘 Blueprints
  ├─ 📅 Programs
  ├─ 🎯 Master Programs
  └─ ⚙️ Department Settings (RestHours, WeeklyCap, DutyRoles)

🎯 Operations
  ├─ 📊 Scheduled Shifts (Calendar/Table)
  ├─ 🗂️ Chores
  └─ 🎯 Duties

🔔 Tasks

🎛️ Admin Hub (Dashboard)

🔧 Owner Administration (if SystemAdmin grant)
```

**Key Changes:**
1. **New "People & Structure" section:**
   - 🏗️ Organizational Structure (manage hierarchy)
   - 💼 JobTypes (JobType CRUD)
   - 🎖️ Grants (grant management UI)

2. **New "Scheduling" section:**
   - Consolidates Blueprints, Programs, MasterPrograms
   - Adds Department Settings (absorbs old Admin/Config)

3. **Renamed "Day Shifts" → "Duties":**
   - Reflects new DutyRole/DutyAssignment model

4. **New "Tasks" section:**
   - Smart task system for grant assignments

5. **OwnerHub isolation:**
   - Only visible with SystemAdmin grant
   - No duplication of technical features in Admin sidebar

### 8.3 Director Hub - Filtered View

Director Hub uses same sidebar navigation, but scoped to their grants:
- Show only Companies/Departments they have grants for
- Hierarchy tree filtered to their scope
- Same pages, just permission-filtered data

---

## 9. Email/ADFS/Game Configuration

### 9.1 Email Configuration - Global SMTP + Per-Company Templates

#### **EmailConfig Model (Global)**

```csharp
public class EmailConfig
{
    public int Id { get; set; }
    // No CompanyId - single global record

    public bool Enabled { get; set; }
    public string? EncryptedApiKey { get; set; }
    public string? ApiUrl { get; set; }
    public string? FromAddress { get; set; }  // Same for all companies

    public DateTime LastUpdated { get; set; }
    public string? LastUpdatedBy { get; set; }
}
```

#### **EmailTemplate Model (Per-Company)**

```csharp
public class EmailTemplate : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;  // "ShiftAssigned", "VacationApproved", etc.
    public string SubjectEn { get; set; } = string.Empty;
    public string SubjectHe { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;  // Supports {{variables}}
    public string BodyHe { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime LastUpdated { get; set; }
    public int LastUpdatedBy { get; set; }

    public Company Company { get; set; } = null!;
}
```

#### **Template Keys**

```csharp
public static class EmailTemplateKeys
{
    public const string SHIFT_ASSIGNED = "ShiftAssigned";
    public const string SHIFT_REMOVED = "ShiftRemoved";
    public const string VACATION_REQUEST_SUBMITTED = "VacationRequestSubmitted";
    public const string VACATION_REQUEST_APPROVED = "VacationRequestApproved";
    public const string VACATION_REQUEST_REJECTED = "VacationRequestRejected";
    public const string SWAP_REQUEST_RECEIVED = "SwapRequestReceived";
    public const string SWAP_REQUEST_APPROVED = "SwapRequestApproved";
    public const string SWAP_REQUEST_REJECTED = "SwapRequestRejected";
    public const string PASSWORD_RESET = "PasswordReset";
    public const string WELCOME_NEW_USER = "WelcomeNewUser";
    public const string DUTY_ASSIGNED = "DutyAssigned";
    public const string CHORE_ASSIGNED = "ChoreAssigned";
}
```

### 9.2 ADFS Configuration - Global Only

```csharp
public class GriffinConfig
{
    public int Id { get; set; }
    // No CompanyId - single global record

    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? TokenConsumerUrl { get; set; }
    public bool AutoProvisionUsers { get; set; } = true;
    public UserRole DefaultProvisionedRole { get; set; } = UserRole.Employee;
    public int TimeoutSeconds { get; set; } = 10;

    public DateTime LastUpdated { get; set; }
    public string? LastUpdatedBy { get; set; }
}
```

### 9.3 Game Configuration - Global Only

```csharp
public class GameConfig
{
    public int Id { get; set; }
    // No CompanyId - single global record

    public bool Enabled { get; set; }
    public int GridSize { get; set; }
    public int PointsPer3Match { get; set; }
    public int PointsPer4Match { get; set; }
    public int PointsPer5PlusMatch { get; set; }
    public int MegaComboMultiplier { get; set; }
    public int MegaCombo3MatchMinLines { get; set; }
    public int MegaCombo4MatchMinLines { get; set; }
    public int MegaCombo5MatchMinLines { get; set; }
    public string Milestones { get; set; } = "1000,2500,5000,7500,10000,15000,20000";

    public DateTime LastUpdated { get; set; }
    public string? LastUpdatedBy { get; set; }
}
```

### 9.4 Configuration Summary

| Configuration | Scope | Model | Notes |
|---------------|-------|-------|-------|
| **Email SMTP** | Global | `EmailConfig` (no CompanyId) | API URL, API Key, From Address |
| **Email Templates** | Per-Company | `EmailTemplate` (with CompanyId) | Subject/Body in EN/HE with {{variables}} |
| **Griffin ADFS** | Global | `GriffinConfig` (no CompanyId) | All companies use same OAuth |
| **Game Settings** | Global | `GameConfig` (no CompanyId) | All companies use same game rules |

---

## 10. Seed Data Structure

### 10.1 appsettings.json Configuration

```json
{
  "HierarchySeed": {
    "Project": {
      "Name": "Shifty",
      "DisplayName": "שיפטי"
    },
    "Area": {
      "Name": "190",
      "DisplayName": "190"
    },
    "Molecules": [
      { "Name": "Oren", "DisplayName": "אורן" }
    ],
    "Departments": [
      {
        "MoleculeName": "Oren",
        "Name": "Defence and Manuver",
        "DisplayName": "הגנה ותמרון"
      }
    ],
    "Companies": [
      { "DepartmentName": "Defence and Manuver", "Name": "Radio", "DisplayName": "טקטי" },
      { "DepartmentName": "Defence and Manuver", "Name": "North", "DisplayName": "צפון" },
      { "DepartmentName": "Defence and Manuver", "Name": "City", "DisplayName": "העיר" },
      { "DepartmentName": "Defence and Manuver", "Name": "Hir", "DisplayName": "ח'י\"ר" }
    ],
    "JobTypeTemplates": [
      {
        "Key": "BR",
        "NameEnglish": "BR",
        "NameHebrew": "ב\"ר",
        "IsOnCallBased": false,
        "ManagementPattern": "Hierarchical",
        "Color": "#1565C0"
      },
      {
        "Key": "PRODUCER",
        "NameEnglish": "Producer",
        "NameHebrew": "אלחוטן",
        "IsOnCallBased": false,
        "ManagementPattern": "Hierarchical",
        "Color": "#00695C"
      },
      {
        "Key": "HAKAM",
        "NameEnglish": "Hakam",
        "NameHebrew": "חק\"מ",
        "IsOnCallBased": true,
        "ManagementPattern": "DirectDepartment",
        "Color": "#E65100"
      }
    ],
    "DutyRoleTemplates": [
      {
        "Key": "ON_DUTY_LEAD",
        "NameEnglish": "On-Duty Lead",
        "NameHebrew": "מוביל תורנות",
        "RequiresTimeBlocks": false,
        "DefaultScope": "Molecule",
        "EligibilityRuleJson": "{\"MinimumRank\": \"Officer\", \"MinimumGrantLevel\": \"Department\"}"
      },
      {
        "Key": "HAKAM_ON_CALL",
        "NameEnglish": "Hakam On-Call",
        "NameHebrew": "חק\"מ בכוננות",
        "JobTypeKey": "HAKAM",
        "RequiresTimeBlocks": true,
        "DefaultStartTime": "08:00",
        "DefaultEndTime": "20:00",
        "DefaultScope": "Molecule"
      }
    ],
    "EmailTemplateDefaults": [
      {
        "Key": "ShiftAssigned",
        "SubjectEn": "You've been assigned to a shift",
        "SubjectHe": "שובצת למשמרת",
        "BodyEn": "Hi {{UserName}},\n\nYou've been assigned to {{ShiftName}} on {{Date}} from {{StartTime}} to {{EndTime}}.\n\nThank you.",
        "BodyHe": "שלום {{UserName}},\n\nשובצת ל{{ShiftName}} בתאריך {{Date}} משעה {{StartTime}} עד {{EndTime}}.\n\nתודה."
      }
    ]
  }
}
```

### 10.2 Seed Script Logic

```csharp
public async Task SeedHierarchyAsync()
{
    var config = _configuration.GetSection("HierarchySeed");

    // 1. Create Project
    var project = new Project
    {
        Name = config["Project:Name"]!,
        DisplayName = config["Project:DisplayName"]!,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = ownerId
    };
    _db.Projects.Add(project);
    await _db.SaveChangesAsync();

    // 2. Create Area
    var area = new Area
    {
        ProjectId = project.Id,
        Name = config["Area:Name"]!,
        DisplayName = config["Area:DisplayName"]!,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = ownerId
    };
    _db.Areas.Add(area);
    await _db.SaveChangesAsync();

    // 3. Create Molecules
    var molecules = config.GetSection("Molecules").Get<List<MoleculeSeedData>>();
    foreach (var molData in molecules)
    {
        var molecule = new Molecule
        {
            AreaId = area.Id,
            Name = molData.Name,
            DisplayName = molData.DisplayName,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ownerId
        };
        _db.Molecules.Add(molecule);
    }
    await _db.SaveChangesAsync();

    // 4. Create Departments
    var departments = config.GetSection("Departments").Get<List<DepartmentSeedData>>();
    foreach (var deptData in departments)
    {
        var molecule = _db.Molecules.First(m => m.Name == deptData.MoleculeName);
        var department = new Department
        {
            MoleculeId = molecule.Id,
            Name = deptData.Name,
            DisplayName = deptData.DisplayName,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ownerId
        };
        _db.Departments.Add(department);
    }
    await _db.SaveChangesAsync();

    // 5. Create Companies
    var companies = config.GetSection("Companies").Get<List<CompanySeedData>>();
    foreach (var compData in companies)
    {
        var department = _db.Departments.First(d => d.Name == compData.DepartmentName);
        var company = new Company
        {
            DepartmentId = department.Id,
            Name = compData.Name,
            DisplayName = compData.DisplayName,
            Slug = compData.Name.ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ownerId
        };
        _db.Companies.Add(company);
    }
    await _db.SaveChangesAsync();

    // 6. Create JobTypes (per Department)
    var jobTypeTemplates = config.GetSection("JobTypeTemplates").Get<List<JobTypeTemplate>>();
    var department = _db.Departments.First();
    foreach (var template in jobTypeTemplates)
    {
        var jobType = new JobType
        {
            DepartmentId = department.Id,
            Name = template.NameEnglish,
            DisplayName = template.NameHebrew,
            ManagementPattern = Enum.Parse<ManagementPattern>(template.ManagementPattern),
            IsOnCallBased = template.IsOnCallBased,
            Color = template.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ownerId
        };
        _db.JobTypes.Add(jobType);
    }
    await _db.SaveChangesAsync();

    // 7. Create DutyRoles
    var dutyRoleTemplates = config.GetSection("DutyRoleTemplates").Get<List<DutyRoleTemplate>>();
    foreach (var template in dutyRoleTemplates)
    {
        int? jobTypeId = null;
        if (!string.IsNullOrEmpty(template.JobTypeKey))
        {
            var jobTypeTemplate = jobTypeTemplates.First(jt => jt.Key == template.JobTypeKey);
            jobTypeId = _db.JobTypes.First(jt => jt.Name == jobTypeTemplate.NameEnglish).Id;
        }

        var dutyRole = new DutyRole
        {
            Name = template.NameEnglish,
            DisplayName = template.NameHebrew,
            JobTypeId = jobTypeId,
            RequiresTimeBlocks = template.RequiresTimeBlocks,
            DefaultStartTime = template.DefaultStartTime.HasValue ? TimeOnly.Parse(template.DefaultStartTime) : null,
            DefaultEndTime = template.DefaultEndTime.HasValue ? TimeOnly.Parse(template.DefaultEndTime) : null,
            DefaultScope = Enum.Parse<ScopeType>(template.DefaultScope),
            EligibilityRuleJson = template.EligibilityRuleJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ownerId
        };
        _db.DutyRoles.Add(dutyRole);
    }
    await _db.SaveChangesAsync();

    // 8. Seed Email Templates for each Company
    var emailDefaults = config.GetSection("EmailTemplateDefaults").Get<List<EmailTemplateDefault>>();
    foreach (var company in _db.Companies)
    {
        foreach (var template in emailDefaults)
        {
            var emailTemplate = new EmailTemplate
            {
                CompanyId = company.Id,
                TemplateKey = template.Key,
                SubjectEn = template.SubjectEn,
                SubjectHe = template.SubjectHe,
                BodyEn = template.BodyEn,
                BodyHe = template.BodyHe,
                IsActive = true,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = ownerId
            };
            _db.EmailTemplates.Add(emailTemplate);
        }
    }
    await _db.SaveChangesAsync();

    // 9. Create Global Configurations
    var emailConfig = new EmailConfig
    {
        Enabled = false,
        FromAddress = "noreply@shiftmanager.mil",
        LastUpdated = DateTime.UtcNow
    };
    _db.EmailConfigs.Add(emailConfig);

    var adfsConfig = new GriffinConfig
    {
        Enabled = false,
        AutoProvisionUsers = true,
        DefaultProvisionedRole = UserRole.Employee,
        TimeoutSeconds = 10,
        LastUpdated = DateTime.UtcNow
    };
    _db.GriffinConfigs.Add(adfsConfig);

    var gameConfig = new GameConfig
    {
        Enabled = true,
        GridSize = 6,
        PointsPer3Match = 40,
        PointsPer4Match = 100,
        PointsPer5PlusMatch = 200,
        MegaComboMultiplier = 2,
        MegaCombo3MatchMinLines = 0,
        MegaCombo4MatchMinLines = 2,
        MegaCombo5MatchMinLines = 0,
        Milestones = "1000,2500,5000,7500,10000,15000,20000",
        LastUpdated = DateTime.UtcNow
    };
    _db.GameConfigs.Add(gameConfig);

    await _db.SaveChangesAsync();
}
```

---

## 11. Migration Strategy

### 11.1 Data Migration Steps

**Step 1: Create new hierarchy tables**
```sql
CREATE TABLE Projects (...);
CREATE TABLE Areas (...);
CREATE TABLE Molecules (...);
CREATE TABLE Departments (...);
-- Companies table already exists, add DepartmentId column
ALTER TABLE Companies ADD DepartmentId INT NOT NULL DEFAULT 1;
```

**Step 2: Seed hierarchy from appsettings.json**
- Run seed script to create Project → Area → Molecule → Department → Company structure

**Step 3: Update AppUser table**
```sql
ALTER TABLE AppUsers ADD DepartmentId INT NOT NULL DEFAULT 1;
ALTER TABLE AppUsers ADD MoleculeId INT NOT NULL DEFAULT 1;
ALTER TABLE AppUsers ADD AreaId INT NOT NULL DEFAULT 1;
ALTER TABLE AppUsers ADD ProjectId INT NOT NULL DEFAULT 1;
ALTER TABLE AppUsers ADD PrimaryJobTypeId INT NULL;
ALTER TABLE AppUsers ADD Rank INT NOT NULL DEFAULT 0; -- Turai
```

**Step 4: Migrate existing users**
```csharp
// For each existing user, populate hierarchy fields from their Company
foreach (var user in _db.Users)
{
    var company = await _db.Companies
        .Include(c => c.Department)
            .ThenInclude(d => d.Molecule)
                .ThenInclude(m => m.Area)
                    .ThenInclude(a => a.Project)
        .FirstAsync(c => c.Id == user.CompanyId);

    user.DepartmentId = company.DepartmentId;
    user.MoleculeId = company.Department.MoleculeId;
    user.AreaId = company.Department.Molecule.AreaId;
    user.ProjectId = company.Department.Molecule.Area.ProjectId;

    // Assign default rank
    user.Rank = MilitaryRank.Turai;
}
await _db.SaveChangesAsync();
```

**Step 5: Create Grants from old Roles**
```csharp
foreach (var user in _db.Users)
{
    var grants = new List<Grant>();

    switch (user.Role)
    {
        case UserRole.Owner:
            grants.Add(new Grant
            {
                UserId = user.Id,
                ScopeType = ScopeType.Project,
                ScopeId = 1,
                Action = GrantAction.SystemAdmin,
                GrantedBy = user.Id,
                GrantedAt = DateTime.UtcNow
            });
            break;

        case UserRole.Director:
            grants.Add(new Grant
            {
                UserId = user.Id,
                ScopeType = ScopeType.Department,
                ScopeId = user.DepartmentId,
                Action = GrantAction.ManageUsers,
                GrantedBy = ownerId,
                GrantedAt = DateTime.UtcNow
            });
            break;

        case UserRole.Manager:
            grants.Add(new Grant
            {
                UserId = user.Id,
                ScopeType = ScopeType.Company,
                ScopeId = user.CompanyId,
                Action = GrantAction.Edit,
                GrantedBy = ownerId,
                GrantedAt = DateTime.UtcNow
            });
            break;

        case UserRole.Employee:
            grants.Add(new Grant
            {
                UserId = user.Id,
                ScopeType = ScopeType.Company,
                ScopeId = user.CompanyId,
                Action = GrantAction.View,
                GrantedBy = ownerId,
                GrantedAt = DateTime.UtcNow
            });
            break;
    }

    _db.Grants.AddRange(grants);
}
await _db.SaveChangesAsync();
```

**Step 6: Migrate ShiftTypes to Department-scoped**
```sql
-- ShiftTypes were Company-scoped, now Department-scoped
UPDATE ShiftTypes SET DepartmentId = (
    SELECT DepartmentId FROM Companies WHERE Companies.Id = ShiftTypes.CompanyId
);
ALTER TABLE ShiftTypes DROP COLUMN CompanyId;
```

**Step 7: Create ShiftTypeJobTypeAssignments**
```csharp
// Initially, assign all ShiftTypes to all JobTypes (migration default)
// User can adjust later via Blueprints page
foreach (var shiftType in _db.ShiftTypes)
{
    var department = await _db.Departments.FindAsync(shiftType.DepartmentId);
    var jobTypes = await _db.JobTypes.Where(jt => jt.DepartmentId == department.Id).ToListAsync();

    foreach (var jobType in jobTypes)
    {
        _db.ShiftTypeJobTypeAssignments.Add(new ShiftTypeJobTypeAssignment
        {
            ShiftTypeId = shiftType.Id,
            JobTypeId = jobType.Id,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = ownerId
        });
    }
}
await _db.SaveChangesAsync();
```

**Step 8: Migrate OnDuty to DutyAssignments**
```csharp
// OnDuty → DutyAssignment
foreach (var onDuty in _db.OnDuties)
{
    // Determine DutyRole based on Type
    int dutyRoleId = onDuty.Type == OnDutyType.Hakam
        ? hakamOnCallRoleId
        : onDutyLeadRoleId;

    var assignment = new DutyAssignment
    {
        DutyRoleId = dutyRoleId,
        UserId = onDuty.UserId,
        ScopeType = ScopeType.Molecule,  // Assume Molecule scope
        ScopeId = await GetMoleculeIdForUser(onDuty.UserId),
        Date = onDuty.Date,
        StartTime = null,  // Legacy OnDuty had no time blocks
        EndTime = null,
        CreatedBy = ownerId,
        CreatedAt = onDuty.CreatedAt,
        CanceledAt = onDuty.CanceledAt,
        CanceledBy = onDuty.CanceledBy
    };

    _db.DutyAssignments.Add(assignment);
}
await _db.SaveChangesAsync();
```

**Step 9: Migrate AppConfig RestHours/WeeklyCap to Department-scoped**
```csharp
// Old: AppConfig with CompanyId
// New: DepartmentConfig table (or keep AppConfig but use DepartmentId)

foreach (var config in _db.Configs.Where(c => c.Key == "RestHours" || c.Key == "WeeklyHoursCap"))
{
    var company = await _db.Companies.FindAsync(config.CompanyId);

    // Check if Department already has this config
    var existingDeptConfig = await _db.Configs
        .FirstOrDefaultAsync(c => c.DepartmentId == company.DepartmentId && c.Key == config.Key);

    if (existingDeptConfig == null)
    {
        // Create Department-level config
        _db.Configs.Add(new AppConfig
        {
            DepartmentId = company.DepartmentId,
            Key = config.Key,
            Value = config.Value
        });
    }

    // Delete old Company-level config
    _db.Configs.Remove(config);
}
await _db.SaveChangesAsync();
```

**Step 10: Migrate Game/Email/ADFS to global**
```csharp
// Extract Game settings from first company's AppConfig
var gameSettings = await _db.Configs
    .Where(c => c.CompanyId == firstCompanyId && c.Key.StartsWith("Game"))
    .ToListAsync();

var gameConfig = new GameConfig
{
    Enabled = GetBoolValue(gameSettings, "GameEnabled", true),
    GridSize = GetIntValue(gameSettings, "GameGridSize", 6),
    PointsPer3Match = GetIntValue(gameSettings, "GamePointsPer3Match", 40),
    // ... etc
    LastUpdated = DateTime.UtcNow
};
_db.GameConfigs.Add(gameConfig);

// Delete all Game configs from AppConfig
_db.Configs.RemoveRange(_db.Configs.Where(c => c.Key.StartsWith("Game")));

await _db.SaveChangesAsync();
```

**Step 11: Delete all existing data except admin@local**
```csharp
// ⚠️ DESTRUCTIVE OPERATION - delete all data except Owner user
var ownerUser = await _db.Users.FirstAsync(u => u.Email == "admin@local");

_db.Users.RemoveRange(_db.Users.Where(u => u.Id != ownerUser.Id));
_db.ShiftInstances.RemoveRange(_db.ShiftInstances);
_db.Requests.RemoveRange(_db.Requests);
_db.Chores.RemoveRange(_db.Chores);
_db.OnDuties.RemoveRange(_db.OnDuties);
_db.Companies.RemoveRange(_db.Companies);
// ... delete all entity data

await _db.SaveChangesAsync();

// Run seed script to create fresh hierarchy
await SeedHierarchyAsync();
```

### 11.2 UI Migration

**Step 1: Update all permission checks**
```csharp
// Old:
if (User.IsInRole("Manager"))

// New:
if (await _grantService.HasGrantAsync(userId, GrantAction.Edit, scopeType, scopeId))
```

**Step 2: Update CompanyContext to HierarchyContext**
```csharp
// Old:
var companyId = _companyContext.GetCompanyIdOrThrow();

// New:
var companyId = _hierarchyContext.CompanyId ?? throw new InvalidOperationException();
var departmentId = _hierarchyContext.DepartmentId ?? throw new InvalidOperationException();
```

**Step 3: Add hierarchy navigation UI**
- Organizational Structure page (`/Admin/Structure`)
- JobTypes page (`/Admin/JobTypes`)
- Grants page (`/Admin/Grants`)
- Tasks page (`/Admin/Tasks`)

**Step 4: Update calendar navigation**
- Add JobType filtering
- Add multi-level scope (Company, Department, Molecule)

---

## End of Design Document

**Status:** ✅ Complete - Ready for Implementation
**Next Steps:**
1. Review design with stakeholders
2. Create implementation plan
3. Begin Phase 1: Database schema updates
4. Implement hierarchy seeding
5. Build grant system
6. Migrate UI

**Document Version:** 1.0
**Last Updated:** 2026-01-24
