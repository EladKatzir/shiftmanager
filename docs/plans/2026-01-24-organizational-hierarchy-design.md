# Organizational Hierarchy Redesign - Complete Design Document

**Date:** 2026-01-24
**Status:** ✅ FINALIZED - Ready for Implementation
**Version:** 2.0

---

## Executive Summary

This document describes the complete redesign of ShiftManager's organizational model from a single-level "Company" system to a 6-level hierarchy with explicit grant-based permissions (Own/Give capabilities), separated duty types, and JobType-based scheduling.

**Current State:**
- Single organizational level: Company
- Role-based permissions: Owner/Manager/Director/Employee enum
- Free-text JobTitle and Department fields
- Confused OnDuty system (mixes Responsibility vs Coverage)
- Company-scoped calendars only

**Target State:**
- 6-level hierarchy: Project → Area → Molecule → Department → Company → User
- Grant-based permissions with Own/Give capabilities (26 permissions + SystemAdmin)
- JobType entity with ManagementPattern attribute + Primary JobType + hat switching
- Separated Duty system (Responsibility vs Coverage) with Molecule-scoped DutyPrograms
- Multi-level calendars with JobType filtering
- Department-scoped Blueprints/Programs with REQUIRED CompanyId+JobTypeId
- Settings: Department defaults + Company overrides

---

## Table of Contents

1. [Organizational Hierarchy](#1-organizational-hierarchy)
2. [Grant System](#2-grant-system)
3. [JobType Entity & Hat Switching](#3-jobtype-entity--hat-switching)
4. [Duty System](#4-duty-system)
5. [Programs & Blueprints](#5-programs--blueprints)
6. [Settings System](#6-settings-system)
7. [Calendar System](#7-calendar-system)
8. [Smart Task System](#8-smart-task-system)
9. [Admin UI Organization](#9-admin-ui-organization)
10. [Email/ADFS/Game Configuration](#10-emailadfsgame-configuration)
11. [Seed Data Structure](#11-seed-data-structure)
12. [Migration Strategy](#12-migration-strategy)

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

| Level | Purpose | Configuration Scope | Example |
|-------|---------|---------------------|---------|
| **Project** | Top-level organizational unit | SystemAdmin only | Shifty |
| **Area** | Area Admin boundary | Cross-Molecule management | 190 |
| **Molecule** | Operational boundary for chores, duties, DutyRoles, DutyPrograms | Molecule-scoped configs | Oren |
| **Department** | Policy container for Blueprints, Programs, MasterPrograms, Settings | Department-scoped configs | Defence and Manuver |
| **Company** | Team/unit with shared calendar, Settings overrides | Company-scoped instances | Radio, North, City, Hir |
| **User** | Individual employee with Primary JobType | N/A | Individual person |

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
    public List<DutyRole> DutyRoles { get; set; } = new();  // ✅ Molecule-scoped
    public List<DutyProgram> DutyPrograms { get; set; } = new();  // ✅ Molecule-scoped
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
    public List<ShiftType> ShiftTypes { get; set; } = new();  // ✅ Department-scoped Blueprints
    public List<ShiftProgram> ShiftPrograms { get; set; } = new();  // ✅ Department-scoped
    public List<MasterProgram> MasterPrograms { get; set; } = new();  // ✅ Department-scoped
    public DepartmentSettings Settings { get; set; } = null!;  // ✅ Department defaults
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
    public CompanySettings Settings { get; set; } = null!;  // ✅ Company overrides
}

public class AppUser
{
    public int Id { get; set; }

    // Primary organizational association
    public int CompanyId { get; set; }
    public int PrimaryJobTypeId { get; set; }  // ✅ REQUIRED: Primary JobType

    // Cached hierarchy (for performance) - populated from Company navigation
    // These are NOT user input, they are derived from CompanyId
    // Claims will store: UserId + CompanyId (not full hierarchy)

    // Job & Rank
    public MilitaryRank Rank { get; set; }

    // ... existing fields (Email, PasswordHash, etc.)

    // DEPRECATED (will be removed after migration)
    public string? Department { get; set; }  // Old free-text field
    public UserRole Role { get; set; }       // Replaced by Grants

    // Navigation
    public Company Company { get; set; } = null!;
    public JobType PrimaryJobType { get; set; } = null!;
    public List<UserJobType> JobTypes { get; set; } = new();  // ✅ Many-to-many
    public List<Grant> Grants { get; set; } = new();
}

// ✅ NEW: Many-to-many for multi-JobType users
public class UserJobType
{
    public int UserId { get; set; }
    public int JobTypeId { get; set; }

    public User User { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
}
```

### 1.4 User Claims Structure

**Q11 Answer: UserId + CompanyId (current context)**

```csharp
// Claims set at login (in JWT/cookie)
new Claim("UserId", user.Id.ToString()),
new Claim("CompanyId", user.CompanyId.ToString())

// Session value (changes when user switches hats)
HttpContext.Session.SetInt32("ActiveJobTypeId", user.PrimaryJobTypeId);

// Hierarchy derived on-demand via navigation:
var department = user.Company.Department;
var molecule = user.Company.Department.Molecule;
var area = user.Company.Department.Molecule.Area;
var project = user.Company.Department.Molecule.Area.Project;
```

---

## 2. Grant System

### 2.1 Grant Model

**COMPLETE REDESIGN:** Replaced hierarchical GrantAction enum with explicit permissions + Own/Give capabilities.

```csharp
public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }

    // Hierarchical scope (nullable to support multiple levels)
    public int? ProjectId { get; set; }
    public int? AreaId { get; set; }
    public int? MoleculeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }  // Optional filter

    // Permission
    public GrantPermission Permission { get; set; }

    // ✅ Own/Give Capabilities
    public bool CanOwn { get; set; }   // User can perform the action
    public bool CanGive { get; set; }  // User can grant this permission to others

    // Audit
    public int? GrantedByUserId { get; set; }
    public DateTime GrantedAt { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public User? GrantedByUser { get; set; }
    public Project? Project { get; set; }
    public Area? Area { get; set; }
    public Molecule? Molecule { get; set; }
    public Department? Department { get; set; }
    public Company? Company { get; set; }
    public JobType? JobType { get; set; }
}

public enum GrantPermission
{
    // === OPERATIONAL ACTIONS (5) ===
    AssignShifts,
    AssignChores,
    AssignDuties,
    ApproveVacations,
    ApproveSwaps,

    // === USER MANAGEMENT (5) ===
    ViewUsers,
    CreateUsers,
    EditUsers,
    DeleteUsers,
    AssignJobTypes,

    // === CONFIGURATION MANAGEMENT (4) ===
    ManageBlueprints,     // Create/Edit/Delete Blueprints
    ManagePrograms,       // Create/Edit/Delete Programs
    ManageMasterPrograms,
    EditSettings,         // RestHours, WeeklyCap, Company overrides

    // === VIEWING PERMISSIONS (5) ===
    ViewShiftCalendar,
    ViewChoreCalendar,
    ViewDutyCalendar,
    ViewVacationCalendar,
    ViewAnalytics,

    // === GRANT MANAGEMENT (1) ===
    ManageGrants,         // Own = view grants, Give = create grants

    // === HIERARCHY MANAGEMENT (1) ===
    EditHierarchy,        // Edit Companies, Departments, Molecules, Areas

    // === EMAIL & COMMUNICATION (3) ===
    ViewEmailTemplates,
    EditEmailTemplates,
    SendEmails,

    // === SYSTEM ADMINISTRATION (1) ===
    SystemAdmin           // ✅ SPECIAL: Full system access (covers all 26 permissions)
}
```

**Total: 26 permissions + 1 SystemAdmin**

### 2.2 Grant Rules

| Rule | Behavior |
|------|----------|
| **Scope Hierarchy** | Project > Area > Molecule > Department > Company |
| **CanGive Scope** | Can grant same scope or narrower (dropdown prompt) |
| **Grant Composition** | Additive - multiple grants combine |
| **SystemAdmin** | Single grant covers all 26 permissions (Own + Give) |
| **Auto-Grants** | Stored as explicit grants (e.g., AssignShifts → ViewShiftCalendar) |
| **No Enforcement** | Cross-molecule grants allowed (trust high-level admins) |
| **Explicit Only** | No permission hierarchy - each grant is explicit |

### 2.3 Auto-Grant Rules

**Q12 Answer: Stored as explicit grants (separate database rows)**

```csharp
private static readonly Dictionary<GrantPermission, GrantPermission> AutoGrants = new()
{
    { GrantPermission.AssignShifts, GrantPermission.ViewShiftCalendar },
    { GrantPermission.AssignChores, GrantPermission.ViewChoreCalendar },
    { GrantPermission.AssignDuties, GrantPermission.ViewDutyCalendar },
    { GrantPermission.ApproveVacations, GrantPermission.ViewVacationCalendar },
    { GrantPermission.EditUsers, GrantPermission.ViewUsers },
    { GrantPermission.ManageGrants, GrantPermission.ViewUsers }
};

// When granting AssignShifts, automatically create ViewShiftCalendar grant
public async Task CreateGrantWithAutoGrantsAsync(Grant primaryGrant)
{
    _db.Grants.Add(primaryGrant);

    // Check if auto-grant needed
    if (AutoGrants.TryGetValue(primaryGrant.Permission, out var autoPermission))
    {
        var autoGrant = new Grant
        {
            UserId = primaryGrant.UserId,
            ProjectId = primaryGrant.ProjectId,
            AreaId = primaryGrant.AreaId,
            MoleculeId = primaryGrant.MoleculeId,
            DepartmentId = primaryGrant.DepartmentId,
            CompanyId = primaryGrant.CompanyId,
            JobTypeId = primaryGrant.JobTypeId,
            Permission = autoPermission,
            CanOwn = true,  // Auto-grant is always Own
            CanGive = false,  // Auto-grant cannot delegate
            GrantedByUserId = primaryGrant.GrantedByUserId,
            GrantedAt = primaryGrant.GrantedAt,
            Notes = $"Auto-granted from {primaryGrant.Permission}"
        };

        _db.Grants.Add(autoGrant);
    }

    await _db.SaveChangesAsync();
}
```

### 2.4 Grant Checking Logic

```csharp
public class GrantService
{
    // Check if user can perform action
    public async Task<bool> CanOwnAsync(int userId, GrantPermission permission,
        int? projectId = null, int? areaId = null, int? moleculeId = null,
        int? departmentId = null, int? companyId = null, int? jobTypeId = null)
    {
        // ✅ SPECIAL CASE: SystemAdmin covers everything
        var hasSystemAdmin = await _db.Grants
            .AnyAsync(g => g.UserId == userId
                && g.Permission == GrantPermission.SystemAdmin
                && g.CanOwn);
        if (hasSystemAdmin) return true;

        // Regular grant check
        var grants = await _db.Grants
            .Where(g => g.UserId == userId && g.Permission == permission && g.CanOwn)
            .ToListAsync();

        foreach (var grant in grants)
        {
            if (GrantCoversScope(grant, projectId, areaId, moleculeId, departmentId, companyId, jobTypeId))
                return true;
        }

        return false;
    }

    // Check if user can grant permission to others
    public async Task<bool> CanGiveAsync(int userId, GrantPermission permission,
        int? projectId = null, int? areaId = null, int? moleculeId = null,
        int? departmentId = null, int? companyId = null, int? jobTypeId = null)
    {
        // ✅ SPECIAL CASE: SystemAdmin can grant everything
        var hasSystemAdmin = await _db.Grants
            .AnyAsync(g => g.UserId == userId
                && g.Permission == GrantPermission.SystemAdmin
                && g.CanGive);
        if (hasSystemAdmin) return true;

        // Regular grant check
        var grants = await _db.Grants
            .Where(g => g.UserId == userId && g.Permission == permission && g.CanGive)
            .ToListAsync();

        foreach (var grant in grants)
        {
            if (GrantCoversScope(grant, projectId, areaId, moleculeId, departmentId, companyId, jobTypeId))
                return true;
        }

        return false;
    }

    // Check if grant covers requested scope
    private bool GrantCoversScope(Grant grant,
        int? projectId, int? areaId, int? moleculeId,
        int? departmentId, int? companyId, int? jobTypeId)
    {
        // Project-level grant covers everything
        if (grant.ProjectId.HasValue)
            return true;

        // Area-level grant covers areas and below
        if (grant.AreaId.HasValue)
        {
            if (areaId.HasValue && grant.AreaId != areaId) return false;
            if (moleculeId.HasValue && !IsInArea(moleculeId.Value, grant.AreaId.Value)) return false;
            // ... check department/company too
        }

        // Similar logic for Molecule, Department, Company levels

        // JobType filter (cross-scope)
        if (grant.JobTypeId.HasValue && jobTypeId.HasValue && grant.JobTypeId != jobTypeId)
            return false;

        return true;
    }
}
```

### 2.5 Example Grants

**Employee (BR at North Company):**
```csharp
new Grant { UserId = emp.Id, Permission = GrantPermission.ViewShiftCalendar, CompanyId = northId, JobTypeId = brId, CanOwn = true, CanGive = false }
new Grant { UserId = emp.Id, Permission = GrantPermission.ViewChoreCalendar, MoleculeId = orenId, CanOwn = true, CanGive = false }
new Grant { UserId = emp.Id, Permission = GrantPermission.ViewDutyCalendar, MoleculeId = orenId, CanOwn = true, CanGive = false }
new Grant { UserId = emp.Id, Permission = GrantPermission.ViewVacationCalendar, CompanyId = northId, JobTypeId = brId, CanOwn = true, CanGive = false }
new Grant { UserId = emp.Id, Permission = GrantPermission.ViewUsers, CompanyId = northId, CanOwn = true, CanGive = false }
```
**Total: ~5 grants**

**Company Job Lead (BR Lead at North):**
```csharp
// Shifts: Department-wide
new Grant { UserId = lead.Id, Permission = GrantPermission.AssignShifts, DepartmentId = defenceId, JobTypeId = brId, CanOwn = true, CanGive = true }
new Grant { UserId = lead.Id, Permission = GrantPermission.ViewShiftCalendar, DepartmentId = defenceId, JobTypeId = brId, CanOwn = true, CanGive = false } // Auto-grant

// Chores: Molecule-wide
new Grant { UserId = lead.Id, Permission = GrantPermission.AssignChores, MoleculeId = orenId, CanOwn = true, CanGive = true }

// Vacations: Company-only
new Grant { UserId = lead.Id, Permission = GrantPermission.ApproveVacations, CompanyId = northId, JobTypeId = brId, CanOwn = true, CanGive = true }

// User Management: Company-only
new Grant { UserId = lead.Id, Permission = GrantPermission.CreateUsers, CompanyId = northId, JobTypeId = brId, CanOwn = true, CanGive = true }
new Grant { UserId = lead.Id, Permission = GrantPermission.EditUsers, CompanyId = northId, JobTypeId = brId, CanOwn = true, CanGive = true }

// Configuration: Department-scoped
new Grant { UserId = lead.Id, Permission = GrantPermission.ManageBlueprints, DepartmentId = defenceId, CanOwn = true, CanGive = true }
new Grant { UserId = lead.Id, Permission = GrantPermission.ManagePrograms, DepartmentId = defenceId, CanOwn = true, CanGive = true }
new Grant { UserId = lead.Id, Permission = GrantPermission.EditSettings, CompanyId = northId, CanOwn = true, CanGive = true }

// Grants: Company-only
new Grant { UserId = lead.Id, Permission = GrantPermission.ManageGrants, CompanyId = northId, JobTypeId = brId, CanOwn = true, CanGive = true }
```
**Total: ~22 grants** (demonstrates mixed scopes)

**Owner (SystemAdmin):**
```csharp
// ✅ SINGLE GRANT covers all 26 permissions
new Grant
{
    UserId = owner.Id,
    Permission = GrantPermission.SystemAdmin,
    ProjectId = shiftyId,
    CanOwn = true,
    CanGive = true
}
```
**Total: 1 grant**

---

## 3. JobType Entity & Hat Switching

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
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public List<AppUser> PrimaryUsers { get; set; } = new();  // Users with this as PrimaryJobType
    public List<UserJobType> Users { get; set; } = new();     // All users with this JobType
    public List<Grant> Grants { get; set; } = new();
}

public enum ManagementPattern
{
    Hierarchical,      // BR, Producer: Department Director → Company Leads
    DirectDepartment   // Hakam: Department Director only, no Company Leads
}
```

### 3.2 Hat Switching System

**Q14 Answers:**
- Q14a: A (Primary + Secondary JobTypes)
- Q14b: A (Filter by JobType when assigning)
- Q14c: A (User switches hats - acts in one JobType at a time)

**Q6 Answer: A (Hat switching is UI filter only, not permission filter)**

```csharp
// User model includes Primary JobType
public class AppUser
{
    public int PrimaryJobTypeId { get; set; }  // REQUIRED
    public JobType PrimaryJobType { get; set; } = null!;
    public List<UserJobType> JobTypes { get; set; } = new();  // Many-to-many
}

// ActiveJobTypeId stored in Session (Q9 Answer: Session, not Claims)
HttpContext.Session.SetInt32("ActiveJobTypeId", user.PrimaryJobTypeId);  // Default to primary

// Hat switching endpoint
public async Task<IActionResult> OnPostSwitchJobTypeAsync(int jobTypeId)
{
    // Verify user has this JobType
    var hasJobType = await _db.UserJobTypes
        .AnyAsync(ujt => ujt.UserId == CurrentUserId && ujt.JobTypeId == jobTypeId);

    if (!hasJobType) return Forbid();

    HttpContext.Session.SetInt32("ActiveJobTypeId", jobTypeId);
    return Ok();
}

// UI: Top navigation hat selector
var activeJobTypeId = HttpContext.Session.GetInt32("ActiveJobTypeId") ?? User.PrimaryJobTypeId;
```

**Hat switching affects UI filtering, NOT permissions:**
```csharp
// Calendar page - UI filtering
public async Task OnGetAsync()
{
    var activeJobTypeId = HttpContext.Session.GetInt32("ActiveJobTypeId") ?? User.PrimaryJobTypeId;

    // ✅ Filter shifts DISPLAYED by active hat
    Shifts = await _db.ShiftInstances
        .Include(si => si.ShiftType)
        .Where(si => si.ShiftType.JobTypeId == activeJobTypeId)  // UI filter
        .ToListAsync();
}

// Assignment action - Permission check IGNORES ActiveJobTypeId
public async Task<IActionResult> OnPostAssignShiftAsync(int shiftInstanceId, int userId)
{
    // ✅ Check grant WITHOUT filtering by ActiveJobTypeId
    // User can assign ANY JobType they have grants for (even while wearing different hat)
    var canAssign = await _grantService.CanOwnAsync(
        userId: CurrentUserId,
        permission: GrantPermission.AssignShifts,
        departmentId: shiftInstance.ShiftType.DepartmentId
        // ❌ NOT filtering by jobTypeId - user can assign any JobType they have grants for
    );

    if (!canAssign) return Forbid();

    shiftInstance.AssignedToUserId = userId;
    await _db.SaveChangesAsync();
    return Ok();
}
```

**UI Example:**
```html
<div class="user-context">
    <span>Acting as:</span>
    <select id="activeJobType" onchange="switchJobType(this.value)">
        <option value="1" selected>🎖️ BR (Primary)</option>
        <option value="2">📻 Producer</option>
    </select>
</div>
```

---

## 4. Duty System

### 4.1 DutyRole Model - Molecule-Scoped

**Q8 Answer: B (Molecule-scoped)**

```csharp
public class DutyRole
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }  // ✅ Molecule-scoped

    public string Name { get; set; } = string.Empty;  // "On-Duty Lead", "Hakam On-Call"
    public string DisplayName { get; set; } = string.Empty;
    public int? JobTypeId { get; set; }  // null = all jobs, set = job-specific
    public bool RequiresTimeBlocks { get; set; }  // false = all-day, true = time-specific
    public TimeOnly? DefaultStartTime { get; set; }
    public TimeOnly? DefaultEndTime { get; set; }
    public string? EligibilityRuleJson { get; set; }
    public bool RequiresOfficerRank { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public JobType? JobType { get; set; }
    public List<DutyAssignment> Assignments { get; set; } = new();
    public List<DutyProgram> Programs { get; set; } = new();  // ✅ NEW
}
```

### 4.2 DutyProgram Model - Molecule-Scoped

**Q1 Correction: DutyProgram is Molecule-scoped (NOT Department-scoped like ShiftProgram)**

**Q5 Answer: A (Create DutyProgram for automatic rotation)**
**Q7c Answer: YES (DutyCalendar supports Programs)**

```csharp
public class DutyProgram
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }       // ✅ Molecule-scoped (different from ShiftProgram)
    public int DutyRoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DutyProgramFrequency Frequency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public DutyRole DutyRole { get; set; } = null!;
    public List<DutyProgramItem> Items { get; set; } = new();
}

public class DutyProgramItem
{
    public int Id { get; set; }
    public int DutyProgramId { get; set; }
    public int Order { get; set; }        // Rotation order (1, 2, 3...)
    public int UserId { get; set; }       // Who is assigned in this rotation slot

    public DutyProgram DutyProgram { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum DutyProgramFrequency
{
    Daily,
    Weekly,
    Biweekly,
    Monthly
}
```

**Example:**
```csharp
// Hakam On-Call weekly rotation for Oren Molecule
new DutyProgram
{
    MoleculeId = orenId,
    DutyRoleId = hakamOnCallId,
    Name = "Hakam Weekly Rotation",
    Frequency = DutyProgramFrequency.Weekly,
    Items = new List<DutyProgramItem>
    {
        new() { Order = 1, UserId = userA.Id },  // Week 1
        new() { Order = 2, UserId = userB.Id },  // Week 2
        new() { Order = 3, UserId = userC.Id }   // Week 3 (cycles back to userA)
    }
}
```

### 4.3 DutyAssignment Model

```csharp
public class DutyAssignment
{
    public int Id { get; set; }
    public int DutyRoleId { get; set; }
    public int UserId { get; set; }
    public int? BackupUserId { get; set; }

    // Date & Time
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }  // null if RequiresTimeBlocks = false
    public TimeOnly? EndTime { get; set; }

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

### 4.4 Responsibility vs Coverage Duties

**Responsibility Duties (all-day, RequiresTimeBlocks = false):**
```csharp
new DutyRole
{
    MoleculeId = orenId,
    Name = "On-Duty Lead",
    DisplayName = "מוביל תורנות",
    JobTypeId = null,  // All JobTypes eligible
    RequiresTimeBlocks = false,  // All-day duty
    RequiresOfficerRank = true
}
```

**Coverage Duties (time-specific, RequiresTimeBlocks = true):**
```csharp
new DutyRole
{
    MoleculeId = orenId,
    Name = "Hakam On-Call",
    DisplayName = "חק\"מ בכוננות",
    JobTypeId = hakamId,  // Hakam only
    RequiresTimeBlocks = true,  // Time-specific
    DefaultStartTime = new TimeOnly(8, 0),
    DefaultEndTime = new TimeOnly(20, 0)
}
```

---

## 5. Programs & Blueprints

### 5.1 ShiftType (Blueprints) - Department-Scoped

**Q1 Clarification:** Blueprints/Programs/MasterPrograms are **DEPARTMENT-scoped** (not Molecule).

**Q3 Answer: REQUIRED** - CompanyId and JobTypeId are REQUIRED fields (not nullable).

```csharp
public class ShiftType  // Blueprint
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }  // ✅ PRIMARY SCOPE: Department
    public int CompanyId { get; set; }     // ✅ REQUIRED (not nullable)
    public int JobTypeId { get; set; }     // ✅ REQUIRED (not nullable)

    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;  // Localization key
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
    public List<ShiftInstance> ShiftInstances { get; set; } = new();
}
```

**Example Blueprint:**
```csharp
new ShiftType
{
    DepartmentId = defenceId,
    CompanyId = northId,      // REQUIRED: "North"
    JobTypeId = producerId,   // REQUIRED: "Producer"
    Key = "MORNING_NORTH_PRODUCER",
    NameKey = "ShiftType_MORNING_NORTH_PRODUCER_Name",  // "Morning - North Defence - Producer" / "בוקר - הגנה צפון - אלחוטן"
    Start = new TimeOnly(8, 0),
    End = new TimeOnly(16, 0)
}
```

**No generic blueprints** - every blueprint MUST have both CompanyId AND JobTypeId.

### 5.2 ShiftProgram - Department-Scoped

```csharp
public class ShiftProgram
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }  // ✅ Department-scoped
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public List<ShiftProgramItem> Items { get; set; } = new();
}

public class ShiftProgramItem
{
    public int Id { get; set; }
    public int ShiftProgramId { get; set; }
    public int ShiftTypeId { get; set; }  // References Blueprint
    public DayOfWeek DayOfWeek { get; set; }
    public int Quantity { get; set; }  // How many instances of this shift on this day

    public ShiftProgram ShiftProgram { get; set; } = null!;
    public ShiftType ShiftType { get; set; } = null!;
}
```

### 5.3 MasterProgram - Department-Scoped

**Q16 Answer: Department-scoped (same as Programs/Blueprints)**

```csharp
public class MasterProgram
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }  // ✅ Department-scoped
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    // Navigation
    public Department Department { get; set; } = null!;
    public List<MasterProgramItem> Items { get; set; } = new();
}
```

### 5.4 Cascade Deletion Rules

**Q6 Answer: C (Allow deletion, auto-remove from Programs - cascade)**
**Q5 Answer: Delete ShiftInstances too (CASCADE DELETE)**
**Q10 Answer: YES (Delete ProgramItems entirely, not set to NULL)**

```csharp
// In AppDbContext.OnModelCreating
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ✅ CASCADE DELETE: Deleting Blueprint deletes all ShiftInstances
    modelBuilder.Entity<ShiftInstance>()
        .HasOne(si => si.ShiftType)
        .WithMany(st => st.ShiftInstances)
        .HasForeignKey(si => si.ShiftTypeId)
        .OnDelete(DeleteBehavior.Cascade);  // ✅ Delete instances

    // ✅ CASCADE DELETE: Deleting Blueprint deletes all ProgramItems
    modelBuilder.Entity<ShiftProgramItem>()
        .HasOne(spi => spi.ShiftType)
        .WithMany()
        .HasForeignKey(spi => spi.ShiftTypeId)
        .OnDelete(DeleteBehavior.Cascade);  // ✅ Delete program items
}
```

**Deletion Flow:**
```csharp
public async Task<IActionResult> OnPostDeleteBlueprintAsync(int shiftTypeId, bool confirmed)
{
    if (!confirmed)
    {
        // Show confirmation modal
        var instanceCount = await _db.ShiftInstances.CountAsync(si => si.ShiftTypeId == shiftTypeId);
        var programItemCount = await _db.ShiftProgramItems.CountAsync(spi => spi.ShiftTypeId == shiftTypeId);

        return new JsonResult(new
        {
            requiresConfirmation = true,
            message = $"This will DELETE {instanceCount} shift assignments and remove from {programItemCount} programs. Continue?",
            instanceCount,
            programItemCount
        });
    }

    var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);

    // ✅ EF Core cascades automatically - both ShiftInstances AND ProgramItems deleted
    _db.ShiftTypes.Remove(shiftType);
    await _db.SaveChangesAsync();

    return RedirectToPage(new { success = "Blueprint deleted (cascade deleted instances and program items)" });
}
```

---

## 6. Settings System

### 6.1 Department Defaults + Company Overrides

**Q2 Answer: B (Department defaults + Company overrides)**

```csharp
public class DepartmentSettings
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }

    // Department-wide defaults
    public int DefaultRestHours { get; set; } = 11;
    public int DefaultWeeklyCap { get; set; } = 60;

    public Department Department { get; set; } = null!;
}

public class CompanySettings
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    // Optional company-specific overrides (NULL = use department default)
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }

    public Company Company { get; set; } = null!;
}
```

### 6.2 Settings Service

```csharp
public class SettingsService
{
    public async Task<int> GetRestHoursAsync(int companyId)
    {
        var company = await _db.Companies
            .Include(c => c.Settings)
            .Include(c => c.Department.Settings)
            .FirstAsync(c => c.Id == companyId);

        // Company override takes precedence
        if (company.Settings?.RestHoursOverride.HasValue == true)
            return company.Settings.RestHoursOverride.Value;

        // Otherwise use department default
        return company.Department.Settings.DefaultRestHours;
    }

    public async Task<int> GetWeeklyCapAsync(int companyId)
    {
        var company = await _db.Companies
            .Include(c => c.Settings)
            .Include(c => c.Department.Settings)
            .FirstAsync(c => c.Id == companyId);

        if (company.Settings?.WeeklyCapOverride.HasValue == true)
            return company.Settings.WeeklyCapOverride.Value;

        return company.Department.Settings.DefaultWeeklyCap;
    }
}
```

### 6.3 Settings UI

**Department Director sees:**
```
Department Settings - Defence Department
────────────────────────────────────────
Default Rest Hours:  [11] hours
Default Weekly Cap:  [60] hours

[Save Department Defaults]
```

**Company Job Lead sees:**
```
Company Settings - North Company
────────────────────────────────────────
Rest Hours:  ● Use department default (11 hours)
             ○ Override: [__] hours

Weekly Cap:  ● Use department default (60 hours)
             ○ Override: [__] hours

[Save Company Settings]
```

---

## 7. Calendar System

### 7.1 Calendar Scoping

**Q7a Answer: A (VacationCalendar = Company + JobType)**
**Q7b Answer: NO (ChoreCalendar does NOT support Programs)**
**Q7c Answer: YES (DutyCalendar supports Programs)**

| Calendar Type | Scope | Supports Programs | Notes |
|---------------|-------|-------------------|-------|
| **Shift Calendar** | Department + JobType | YES (ShiftProgram) | Department-scoped blueprints |
| **Chore Calendar** | Molecule | ❌ NO | Manual assignment only |
| **Duty Calendar** | Molecule | ✅ YES (DutyProgram) | Automatic rotation |
| **Vacation Calendar** | Company + JobType | N/A | Shows approved vacations |

### 7.2 Chore Model - Molecule-Scoped

**Q10 Answer: A (No CompanyId filter - truly Molecule-wide)**

```csharp
public class Chore
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }  // ✅ Molecule-scoped (no CompanyId filter)

    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int? AssignedToUserId { get; set; }

    // Audit
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public User? AssignedToUser { get; set; }
}
```

**No company filtering** - chores are truly molecule-wide.

---

## 8. Smart Task System

(No changes from original design - retained for completeness)

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
    public string? AlternativeCandidatesJson { get; set; }

    // Grant details (for grant-creation tasks)
    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }
    public GrantPermission? Permission { get; set; }
    public bool? CanOwn { get; set; }
    public bool? CanGive { get; set; }

    // Task assignment
    public int AssignedTo { get; set; }
    public TaskStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum TaskType
{
    AssignMoleculeAdmin,
    AssignJobDirector,
    AssignJobLead,
    SetupDutyPrograms
}
```

---

## 9. Admin UI Organization

### 9.1 Owner Company Selector

**Q3 Answer: A (Top navigation bar + clear indication + can access global settings while in company context)**

**Q7 Answer: YES (Owner can access global settings while in Company Mode)**

```html
<nav class="top-navbar">
    <div class="navbar-brand">📊 ShiftManager</div>

    <!-- ✅ Owner context (only for SystemAdmin) -->
    @if (HasSystemAdmin)
    {
        <div class="owner-context">
            <span class="context-label">Managing:</span>
            <select class="company-selector" onchange="selectCompany(this.value)">
                <option value="0">🌐 Global Settings</option>
                <optgroup label="Defence Department">
                    <option value="1" selected>North Company</option>
                    <option value="2">Radio Company</option>
                    <option value="3">City Company</option>
                    <option value="4">Hir Company</option>
                </optgroup>
            </select>

            @if (CurrentCompanyId == 0)
            {
                <span class="context-badge global">Global Mode</span>
            }
            else
            {
                <span class="context-badge company">
                    Company Mode: @CurrentCompanyName
                    <span class="global-access-note">(Global settings accessible)</span>
                </span>
            }
        </div>
    }

    <div class="navbar-user"><!-- user dropdown --></div>
</nav>
```

**Behavior:**
- **Global Mode (CompanyId = 0):** Owner sees only global settings pages
- **Company Mode (CompanyId > 0):** Owner sees company data AND can still access global settings (shown in sidebar under "🌐 Global Settings" section)

### 9.2 Sidebar Navigation

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

📅 Scheduling (Q1 Answer: B - in Admin section, not Owner)
  ├─ 📘 Blueprints
  ├─ 📅 Programs
  ├─ 🎯 Master Programs
  └─ ⚙️ Department Settings (RestHours, WeeklyCap)

🎯 Operations
  ├─ 📊 Scheduled Shifts
  ├─ 🗂️ Chores
  └─ 🎯 Duties

🔔 Tasks

🔧 Owner Administration (if SystemAdmin grant)
  🌍 Global Settings
    ├─ 📧 Email Configuration (SMTP)
    ├─ 🔐 Griffin ADFS
    ├─ 🎮 Game Configuration
    ├─ 🚩 Feature Flags
    ├─ 🌐 Language Management
    ├─ 💾 Database Console
    └─ 🏥 System Health

  (If in Company Mode, global settings shown here + company data above)
```

---

## 10. Email/ADFS/Game Configuration

(No changes from original design)

### 10.1 Email: Global SMTP + Per-Company Templates

```csharp
public class EmailConfig  // ✅ Global (no CompanyId)
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string? EncryptedApiKey { get; set; }
    public string? ApiUrl { get; set; }
    public string? FromAddress { get; set; }  // Same for all companies
}

public class EmailTemplate  // ✅ Per-Company
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string SubjectEn { get; set; } = string.Empty;
    public string SubjectHe { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;
    public string BodyHe { get; set; } = string.Empty;
}
```

### 10.2 ADFS & Game: Global Only

```csharp
public class GriffinConfig  // ✅ Global (no CompanyId)
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? TokenConsumerUrl { get; set; }
    public bool AutoProvisionUsers { get; set; }
}

public class GameConfig  // ✅ Global (no CompanyId)
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public int GridSize { get; set; }
    public int PointsPer3Match { get; set; }
    // ... etc
}
```

---

## 11. Seed Data Structure

### 11.1 Bootstrap Sequence

**Q4 Answer: A (Single SystemAdmin grant)**
**Q15 Answer: B (User + SystemAdmin grant - auto-bootstrap)**

```csharp
// After database wipe, seed script auto-creates:

// 1. admin@local user
var admin = new User
{
    Id = 1,
    Username = "admin@local",
    Email = "admin@local",
    CompanyId = 1,  // First company created
    PrimaryJobTypeId = 1,  // First JobType created
    Rank = MilitaryRank.RavAluf,
    PasswordHash = "<hashed_Easteregg>"
};
_db.Users.Add(admin);
await _db.SaveChangesAsync();

// 2. ✅ AUTO-CREATE SystemAdmin grant (Q15: auto-bootstrap)
var systemAdminGrant = new Grant
{
    UserId = admin.Id,
    ProjectId = shiftyProject.Id,
    Permission = GrantPermission.SystemAdmin,
    CanOwn = true,
    CanGive = true,
    GrantedByUserId = admin.Id,  // Self-granted
    GrantedAt = DateTime.UtcNow,
    Notes = "Bootstrap admin user"
};
_db.Grants.Add(systemAdminGrant);
await _db.SaveChangesAsync();
```

**After wipe:**
- ✅ admin@local user exists
- ✅ admin@local has SystemAdmin grant (can do everything)
- ✅ No manual SQL needed

### 11.2 Seed Hierarchy

```csharp
// Create hierarchy from appsettings.json
var shifty = new Project { Name = "Shifty", DisplayName = "שיפטי" };
var area190 = new Area { ProjectId = shifty.Id, Name = "190", DisplayName = "190" };
var oren = new Molecule { AreaId = area190.Id, Name = "Oren", DisplayName = "אורן" };
var defence = new Department { MoleculeId = oren.Id, Name = "Defence and Manuver", DisplayName = "הגנה ותמרון" };

// Companies
var north = new Company { DepartmentId = defence.Id, Name = "North", DisplayName = "צפון" };
var radio = new Company { DepartmentId = defence.Id, Name = "Radio", DisplayName = "טקטי" };
var city = new Company { DepartmentId = defence.Id, Name = "City", DisplayName = "העיר" };
var hir = new Company { DepartmentId = defence.Id, Name = "Hir", DisplayName = "ח'י\"ר" };

// JobTypes (Department-scoped)
var br = new JobType { DepartmentId = defence.Id, Name = "BR", DisplayName = "ב\"ר", ManagementPattern = Hierarchical };
var producer = new JobType { DepartmentId = defence.Id, Name = "Producer", DisplayName = "אלחוטן", ManagementPattern = Hierarchical };
var hakam = new JobType { DepartmentId = defence.Id, Name = "Hakam", DisplayName = "חק\"מ", ManagementPattern = DirectDepartment, IsOnCallBased = true };

// DutyRoles (Molecule-scoped)
var onDutyLead = new DutyRole
{
    MoleculeId = oren.Id,
    Name = "On-Duty Lead",
    DisplayName = "מוביל תורנות",
    RequiresTimeBlocks = false,
    RequiresOfficerRank = true
};

var hakamOnCall = new DutyRole
{
    MoleculeId = oren.Id,
    Name = "Hakam On-Call",
    DisplayName = "חק\"מ בכוננות",
    JobTypeId = hakam.Id,
    RequiresTimeBlocks = true,
    DefaultStartTime = new TimeOnly(8, 0),
    DefaultEndTime = new TimeOnly(20, 0)
};

// Department Settings
var defenceSettings = new DepartmentSettings
{
    DepartmentId = defence.Id,
    DefaultRestHours = 11,
    DefaultWeeklyCap = 60
};

// Company Settings (no overrides initially)
var northSettings = new CompanySettings { CompanyId = north.Id };
var radioSettings = new CompanySettings { CompanyId = radio.Id };

// Global Configs
var emailConfig = new EmailConfig { Enabled = false, FromAddress = "noreply@shiftmanager.mil" };
var adfsConfig = new GriffinConfig { Enabled = false };
var gameConfig = new GameConfig { Enabled = true, GridSize = 6, PointsPer3Match = 40, /* ... */ };
```

---

## 12. Migration Strategy

### 12.1 Migration Phases

**Phase 1: Schema Updates**
1. Create new tables: Projects, Areas, Molecules, Departments
2. Alter Companies table: Add DepartmentId
3. Alter Users table: Add PrimaryJobTypeId, remove Role enum
4. Create Grants table
5. Create UserJobTypes table (many-to-many)
6. Create DutyRole, DutyProgram, DutyProgramItem tables
7. Create DepartmentSettings, CompanySettings tables
8. Alter ShiftType: Change CompanyId → DepartmentId, Add REQUIRED CompanyId + JobTypeId
9. Remove IBelongsToCompany from EmailConfig, GriffinConfig, GameConfig

**Phase 2: Data Wipe & Reseed**
1. ⚠️ DELETE all data except admin@local user
2. Run seed script (hierarchy + JobTypes + DutyRoles + Settings + Global configs)
3. Auto-create admin@local SystemAdmin grant

**Phase 3: UI Migration**
1. Replace all `User.IsInRole()` checks with `_grantService.CanOwnAsync()`
2. Add HierarchyContext service
3. Add hat switching UI (top nav + session storage)
4. Update calendar filtering (JobType-aware)
5. Add grant management UI
6. Add organizational structure management UI

**Phase 4: Testing**
1. Test SystemAdmin can access everything
2. Test hat switching affects UI but not permissions
3. Test cascade deletion (Blueprint → ShiftInstances + ProgramItems)
4. Test Department defaults + Company overrides
5. Test DutyProgram automatic rotation
6. Test auto-grants creation
7. Test Owner company selector + global settings access

---

## End of Design Document

**Status:** ✅ FINALIZED - All contradictions resolved - Ready for Implementation

**Next Steps:**
1. ✅ Design complete
2. Write detailed implementation plan (separate session)
3. Begin database migrations
4. Implement grant system
5. Build UI components
6. Test & deploy

**Document Version:** 2.0 (Finalized)
**Last Updated:** 2026-01-24
**Changes from v1.0:**
- ✅ DutyProgram changed to Molecule-scoped (was incorrectly Department)
- ✅ Settings now Department defaults + Company overrides (was Department-only)
- ✅ Blueprint CompanyId/JobTypeId changed to REQUIRED (was optional)
- ✅ Cascade deletion for ShiftInstances clarified (deletes instances)
- ✅ Hat switching clarified as UI filter only (not permission filter)
- ✅ Owner company selector can access global settings while in company mode
- ✅ DutyRole confirmed as Molecule-scoped
- ✅ Grant system completely redesigned (26 permissions + Own/Give capabilities)
- ✅ Bootstrap sequence clarified (admin@local auto-gets SystemAdmin grant)
- ✅ ChoreCalendar does NOT support Programs (manual only)
- ✅ DutyCalendar DOES support Programs (automatic rotation)
- ✅ VacationCalendar scoped to Company + JobType
- ✅ User claims simplified to UserId + CompanyId
- ✅ ActiveJobTypeId stored in Session (not Claims)
