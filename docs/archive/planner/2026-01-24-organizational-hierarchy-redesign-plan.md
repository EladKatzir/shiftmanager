# Organizational Hierarchy Redesign - Implementation Plan
**Date Created:** 2026-01-24
**Priority:** CRITICAL - All future features depend on this foundation
**Status:** Active Development

---

## Executive Summary

This plan documents the complete redesign of ShiftManager's organizational hierarchy from a single-level "Company" model to a six-level hierarchical structure with job-based permissions, explicit grants, and a unified duty system.

### What's Changing

**FROM (Current):**
```
Company (single level)
└── Users
    └── Role enum (Owner/Manager/Director/Employee)
    └── Department (free text)
    └── JobTitle (free text)
```

**TO (New):**
```
Project (top level)
└── Area
    └── Molecule
        └── Department
            └── Company
                └── Users
                    └── JobType entity (with certifications)
                    └── Grants (ScopeType + JobType + Actions)
```

### Core Principles

1. **Organizational placement ≠ Permissions**
   - Where you belong (Company) is separate from what you can do (Grants)

2. **Job-based, not role-based**
   - No more Manager/Director roles
   - Leadership = JobType + Scope (Job Lead, Job Director)

3. **Explicit, not implicit**
   - Permissions via Grant records, not role inference
   - Calendars generated as Scope + JobType

4. **Duty system separated**
   - Responsibility Duties (temporary leadership, e.g., On-Duty Lead)
   - Coverage Duties (on-call service, e.g., Hakam On-Call)

---

## Part 1: Database Schema Changes

### **1.1 New Organizational Entities**

#### **Project** *(legacy name: owner level)*
Top-level organizational container.

```csharp
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;          // "IDF Operations"
    public string? DisplayName { get; set; }                   // Localized display name
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }                         // Owner user ID
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Area> Areas { get; set; } = new List<Area>();
    public AppUser? CreatedByUser { get; set; }
}
```

**Notes:**
- Only one Project should exist initially (current "management company" becomes Project)
- Owner role operates at Project scope
- No CompanyId (this IS the top level)

---

#### **Area** *(legacy name: base / "190" concept)*
Regional or functional division within a Project.

```csharp
public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }                         // FK to Project
    public string Name { get; set; } = string.Empty;           // "Northern Operations"
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Project? Project { get; set; }
    public ICollection<Molecule> Molecules { get; set; } = new List<Molecule>();
    public AppUser? CreatedByUser { get; set; }
}
```

**Notes:**
- Only one Area should exist initially (user specified: "only one area should exist for the very far future")
- Circle (friend list) scoped to Area (can choose friends from any Molecule in your Area)

---

#### **Molecule** *(legacy name: molecule)*
Operational group within an Area (key security boundary).

```csharp
public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }                            // FK to Area
    public string Name { get; set; } = string.Empty;           // "Ella", "Fight", "Defense"
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Area? Area { get; set; }
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<Chore> Chores { get; set; } = new List<Chore>(); // ✅ Chores scoped to Molecule
    public AppUser? CreatedByUser { get; set; }
}
```

**Notes:**
- **Molecule boundary rule:** Users cannot receive EDIT grants outside their Molecule (enforced by Grant validation)
- Chores are Molecule-scoped (not Company-scoped)
- Duty Assignments (e.g., On-Duty Lead) typically at Molecule scope

---

#### **Department** *(legacy name: department - now entity, not text)*
Functional unit within a Molecule (e.g., "Defence Department", "Logistics Department").

```csharp
public class Department
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }                        // FK to Molecule
    public string Name { get; set; } = string.Empty;           // "Defence", "Logistics"
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Molecule? Molecule { get; set; }
    public ICollection<Company> Companies { get; set; } = new List<Company>();
    public ICollection<JobType> JobTypes { get; set; } = new List<JobType>(); // ✅ JobTypes defined at Department level
    public AppUser? CreatedByUser { get; set; }
}
```

**Notes:**
- JobTypes are Department-level entities
- Job Directors operate at Department scope
- Department Job Rollup Calendars aggregate all Companies in Department

---

#### **Company** *(legacy name: company - now 5th level)*
Real operational unit where employees belong (e.g., "Defence North", "Defence South").

```csharp
public class Company // Existing entity, but now has DepartmentId FK
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }                      // ✅ NEW: FK to Department
    public string Name { get; set; } = string.Empty;           // "Defence North"
    public string? Slug { get; set; }
    public string? DisplayName { get; set; }
    public string? SettingsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Department? Department { get; set; }                // ✅ NEW
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<ShiftInstance> ShiftInstances { get; set; } = new List<ShiftInstance>();
    public AppUser? CreatedByUser { get; set; }
}
```

**Notes:**
- Company STILL scopes shifts (ShiftInstance.CompanyId remains)
- Users belong to exactly ONE Company (AppUser.CompanyId remains)
- Existing Company global query filter remains in place
- Job Leads operate at Company scope

---

### **1.2 JobType Entity** *(legacy name: job title / informal job name)*

JobTypes are the foundation of the calendar system and permission model.

```csharp
public class JobType
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }                      // FK to Department
    public string Name { get; set; } = string.Empty;           // "Recorder", "Analyst"
    public string? DisplayName { get; set; }                   // Localized name
    public string? Description { get; set; }
    public string? Color { get; set; }                         // For calendar color coding
    public bool IsOnCallBased { get; set; } = false;           // True for Hakam (Coverage), false for Recorder (Shift)
    public bool IsActive { get; set; } = true;

    // Attributes (JSON or separate tables)
    public string? CertificationsJson { get; set; }            // ✅ Moved from User profile
    // Example: ["Advanced Recording Tool", "Night Vision Certified"]

    public string? SkillRequirementsJson { get; set; }
    // Example: ["Minimum 6 months experience", "Security clearance"]

    // Navigation
    public Department? Department { get; set; }
    public ICollection<ShiftInstance> ShiftInstances { get; set; } = new List<ShiftInstance>();
    public ICollection<Grant> Grants { get; set; } = new List<Grant>();
}
```

**Notes:**
- ~30 JobTypes total across entire system
- Companies in same Department generally share same JobTypes (but different employee counts)
- Certifications moved from User profile to JobType definition (display-only, not enforcement)
- IsOnCallBased distinguishes shift-based (Recorder) from on-call (Hakam)

---

### **1.3 Grant System** *(legacy name: implied role power)*

Grants explicitly define "who can do what, where, for how long."

```csharp
public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }                            // Who receives the grant

    // Scope (where this grant applies)
    public ScopeType ScopeType { get; set; }                   // Project/Area/Molecule/Department/Company
    public int ScopeId { get; set; }                           // ID of the scope entity

    // Job (which JobType this grant covers)
    public int? JobTypeId { get; set; }                        // Null = all JobTypes in scope

    // Actions (what they can do)
    public GrantAction Action { get; set; }                    // View, Edit, Assign, Approve, etc.

    // Timebox (optional temporary access)
    public DateTime? StartDate { get; set; }                   // Grant active from
    public DateTime? EndDate { get; set; }                     // Grant expires at

    // Audit
    public int GrantedBy { get; set; }                         // Who granted this
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
    public int? RevokedBy { get; set; }

    // Navigation
    public AppUser? User { get; set; }
    public JobType? JobType { get; set; }
    public AppUser? GrantedByUser { get; set; }
    public AppUser? RevokedByUser { get; set; }
}

public enum ScopeType
{
    Project = 0,    // Entire project (Owner level)
    Area = 1,       // Regional/functional area
    Molecule = 2,   // Operational group
    Department = 3, // Functional department (Job Director level)
    Company = 4     // Operational unit (Job Lead level)
}

public enum GrantAction
{
    View = 0,       // Read-only access to calendars
    Edit = 1,       // Create/update/delete assignments
    Assign = 2,     // Assign users to shifts (subset of Edit)
    Approve = 3,    // Approve time-off requests, swaps
    Configure = 4   // Modify shift types, programs, settings
}
```

**Examples:**
```csharp
// James = Job Lead (Recorder) in Defence North
Grant {
    UserId = JamesId,
    ScopeType = ScopeType.Company,
    ScopeId = DefenceNorthId,
    JobTypeId = RecorderId,
    Action = GrantAction.Edit
}

// Shon = Job Director (Recorder) in Defence Department
Grant {
    UserId = ShonId,
    ScopeType = ScopeType.Department,
    ScopeId = DefenceDeptId,
    JobTypeId = RecorderId,
    Action = GrantAction.Edit
}

// Shon temporarily edits Recorders in Fight Molecule (if allowed)
Grant {
    UserId = ShonId,
    ScopeType = ScopeType.Molecule,
    ScopeId = FightMoleculeId,
    JobTypeId = RecorderId,
    Action = GrantAction.Edit,
    StartDate = DateTime.Parse("2026-02-01"),
    EndDate = DateTime.Parse("2026-02-28") // 1 month temporary access
}

// Amit views Analysts in Defence (read-only)
Grant {
    UserId = AmitId,
    ScopeType = ScopeType.Department,
    ScopeId = DefenceDeptId,
    JobTypeId = AnalystId,
    Action = GrantAction.View
}
```

**Validation Rules:**
1. **Molecule boundary:** Users cannot receive Edit grants outside their home Molecule (enforced in GrantService)
2. **Hierarchical scope:** A grant at Department level implies access to all Companies within that Department
3. **Action hierarchy:** Edit implies Assign implies View
4. **Timebox validation:** EndDate must be > StartDate

---

### **1.4 Duty System** *(legacy name: on-duty)*

Separates Responsibility Duties (temporary leadership) from Coverage Duties (on-call service).

#### **Duty Role** *(legacy name: lead)*
A named responsibility that someone temporarily holds.

```csharp
public class DutyRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;           // "On-Duty Lead", "Escalation Contact"
    public string? DisplayName { get; set; }
    public string? Description { get; set; }

    // Eligibility rules
    public string? EligibilityRuleJson { get; set; }           // JSON: {"MinimumRole": "Director", ...}
    // Example for "On-Duty Lead": Director+ eligibility

    // Scope this duty operates at
    public ScopeType DefaultScope { get; set; } = ScopeType.Molecule; // Most duties are Molecule-level

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<DutyAssignment> Assignments { get; set; } = new List<DutyAssignment>();
}
```

---

#### **Duty Assignment** *(legacy name: "X is on-duty lead today")*
A timeboxed record assigning a Duty Role to a person at a specific scope.

```csharp
public class DutyAssignment
{
    public int Id { get; set; }
    public int DutyRoleId { get; set; }                        // FK to DutyRole
    public int UserId { get; set; }                            // Who is on duty

    // Scope (where this duty applies)
    public ScopeType ScopeType { get; set; }                   // Typically Molecule
    public int ScopeId { get; set; }                           // MoleculeId

    // Time period
    public DateOnly Date { get; set; }                         // Duty date
    public TimeOnly? StartTime { get; set; }                   // Null = all day
    public TimeOnly? EndTime { get; set; }

    // Optional backup
    public int? BackupUserId { get; set; }

    // Audit
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CanceledAt { get; set; }                  // Soft delete
    public int? CanceledBy { get; set; }

    // Navigation
    public DutyRole? DutyRole { get; set; }
    public AppUser? User { get; set; }
    public AppUser? BackupUser { get; set; }
}
```

**Example:**
```csharp
// Shon is On-Duty Lead for Ella Molecule today
DutyAssignment {
    DutyRoleId = OnDutyLeadRoleId,
    UserId = ShonId,
    ScopeType = ScopeType.Molecule,
    ScopeId = EllaMoleculeId,
    Date = DateOnly.Parse("2026-01-24"),
    StartTime = null, // All day
    EndTime = null
}
```

---

#### **Coverage Block** *(legacy name: hakam on-duty shift)*
A timeboxed record for on-call coverage (JobType-based).

```csharp
public class CoverageBlock
{
    public int Id { get; set; }
    public int JobTypeId { get; set; }                         // FK to JobType (e.g., Hakam)
    public int UserId { get; set; }                            // Who is providing coverage

    // Scope (where coverage applies)
    public ScopeType ScopeType { get; set; }                   // Typically Molecule
    public int ScopeId { get; set; }

    // Time period
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }                    // 08:00
    public TimeOnly EndTime { get; set; }                      // 20:00

    // Audit
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CanceledAt { get; set; }                  // Soft delete
    public int? CanceledBy { get; set; }

    // Navigation
    public JobType? JobType { get; set; }
    public AppUser? User { get; set; }
}
```

**Example:**
```csharp
// Amit provides Hakam on-call coverage for Ella Molecule 08:00-20:00
CoverageBlock {
    JobTypeId = HakamJobTypeId,
    UserId = AmitId,
    ScopeType = ScopeType.Molecule,
    ScopeId = EllaMoleculeId,
    Date = DateOnly.Parse("2026-01-24"),
    StartTime = TimeOnly.Parse("08:00"),
    EndTime = TimeOnly.Parse("20:00")
}
```

---

### **1.5 Updated Existing Models**

#### **AppUser** *(updated)*
```csharp
public class AppUser : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }                         // ✅ KEEP (still belongs to one Company)

    // Job information
    public int? PrimaryJobTypeId { get; set; }                 // ✅ NEW: FK to JobType (main job)
    public string? JobTitle { get; set; }                      // ✅ KEEP: Free text (e.g., "Senior Recorder")

    // Org placement (redundant for performance)
    public int MoleculeId { get; set; }                        // ✅ NEW: Derived from Company → Department → Molecule
    public int DepartmentId { get; set; }                      // ✅ NEW: Derived from Company → Department
    public int AreaId { get; set; }                            // ✅ NEW: Derived from Molecule → Area
    public int ProjectId { get; set; }                         // ✅ NEW: Derived from Area → Project

    // Legacy fields
    public string? Department { get; set; }                    // ⚠️ DEPRECATED: Remove after migration
    public UserRole Role { get; set; } = UserRole.Employee;    // ⚠️ DEPRECATED: Remove after Grant migration

    // Profile (remove certifications - moved to JobType)
    public string? PreferredName { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? HireDate { get; set; }
    public string? Skills { get; set; }                        // Keep personal skills (different from JobType requirements)
    // public string? Certifications { get; set; }              // ❌ REMOVED (moved to JobType)

    // Navigation
    public Company? Company { get; set; }
    public JobType? PrimaryJobType { get; set; }               // ✅ NEW
    public ICollection<Grant> Grants { get; set; } = new List<Grant>();  // ✅ NEW
}
```

---

#### **ShiftInstance** *(updated)*
```csharp
public class ShiftInstance
{
    public int Id { get; set; }
    public int CompanyId { get; set; }                         // ✅ KEEP (shifts are Company-scoped)
    public int ShiftTypeId { get; set; }                       // ✅ KEEP
    public int JobTypeId { get; set; }                         // ✅ NEW: FK to JobType
    public DateOnly WorkDate { get; set; }
    public int StaffingRequired { get; set; }
    public bool IsDetached { get; set; } = false;
    public int? OriginalProgramId { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public ShiftType? ShiftType { get; set; }
    public JobType? JobType { get; set; }                      // ✅ NEW
}
```

**Notes:**
- ShiftInstance now has JobTypeId to support "Company Job Calendars" (e.g., "Defence North — Recorder")
- ShiftType still defines time ranges (Morning, Afternoon, Night)
- JobType defines the functional role (Recorder, Analyst)

---

#### **Chore** *(updated)*
```csharp
public class Chore // No longer IBelongsToCompany
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }                        // ✅ CHANGED: Now Molecule-scoped (was CompanyId)
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string Title { get; set; }
    public string? Notes { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public int? CanceledBy { get; set; }

    // Navigation
    public Molecule? Molecule { get; set; }                    // ✅ NEW
    public AppUser? User { get; set; }
}
```

**Notes:**
- Chores now scoped to Molecule (not Company)
- Need to update global query filter to scope by Molecule instead of Company
- User specified: "chores are defined per molecule"

---

#### **Circle** *(renamed from TeamCalendar)*
```csharp
public class Circle // Legacy name: TeamCalendar
{
    public int Id { get; set; }
    public int OwnerId { get; set; }                           // User who created this Circle
    public string Name { get; set; } = string.Empty;           // "My Friends", "Watch List"
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? Owner { get; set; }
    public ICollection<CircleMembership> Memberships { get; set; } = new List<CircleMembership>();
}

public class CircleMembership
{
    public int Id { get; set; }
    public int CircleId { get; set; }                          // FK to Circle
    public int UserId { get; set; }                            // Friend/member
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Circle? Circle { get; set; }
    public AppUser? User { get; set; }
}
```

**Notes:**
- Renamed from TeamCalendar to Circle (clarifies "social" not "organizational")
- Read-only view of friends' assignments
- Can add friends from any Molecule in your Area (user specified: "only one area should exist for the very far future")
- User specified: "we already have a page that handles the ui for this" (just need to rename/refactor)

---

### **1.6 Global Query Filters Update**

Update `AppDbContext.OnModelCreating`:

```csharp
// ✅ KEEP existing Company-scoped filters (these still work)
modelBuilder.Entity<ShiftType>().HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());
modelBuilder.Entity<ShiftInstance>().HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());
modelBuilder.Entity<ShiftAssignment>().HasQueryFilter(e => e.ShiftInstance.CompanyId == _tenantResolver.GetCurrentTenantId());
modelBuilder.Entity<AppUser>().HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());

// ✅ UPDATE Chore filter (now Molecule-scoped, not Company-scoped)
modelBuilder.Entity<Chore>().HasQueryFilter(e => e.Molecule.Department.Companies.Any(c => c.Id == _tenantResolver.GetCurrentTenantId()));
// This allows viewing chores for entire Molecule when viewing any Company in that Molecule's Department

// ✅ NEW filters for hierarchical entities (optional - may want these public)
// modelBuilder.Entity<Department>().HasQueryFilter(...); // Decide if needed
// modelBuilder.Entity<Molecule>().HasQueryFilter(...);   // Decide if needed

// ❌ REMOVE DirectorCompany (no longer used - replaced by Grant system)
// DirectorCompany table will be deleted

// ✅ KEEP OnDuty/DutyAssignment/CoverageBlock WITHOUT filters (these are global/cross-company)
```

---

## Part 2: Migration Strategy

### **2.1 Data Cleanup (Before Migration)**

**User specified:** "we will delete all existing data except the owner user (admin@local) and it's company"

```sql
-- Step 1: Identify the admin user and management company
DECLARE @AdminUserId INT = (SELECT Id FROM AppUsers WHERE Email = 'admin@local');
DECLARE @ManagementCompanyId INT = (SELECT CompanyId FROM AppUsers WHERE Id = @AdminUserId);

-- Step 2: Delete all other data (in correct FK order to avoid violations)

-- Assignments first (depends on everything)
DELETE FROM ShiftAssignments WHERE ShiftInstanceId IN (SELECT Id FROM ShiftInstances WHERE CompanyId != @ManagementCompanyId);

-- Time off, swaps, notifications
DELETE FROM TimeOffRequests WHERE UserId IN (SELECT Id FROM AppUsers WHERE CompanyId != @ManagementCompanyId);
DELETE FROM SwapRequests WHERE RequestedBy IN (SELECT Id FROM AppUsers WHERE CompanyId != @ManagementCompanyId);
DELETE FROM UserNotifications WHERE UserId IN (SELECT Id FROM AppUsers WHERE CompanyId != @ManagementCompanyId);

-- Shifts, programs, blueprints
DELETE FROM ShiftInstances WHERE CompanyId != @ManagementCompanyId;
DELETE FROM ShiftPrograms WHERE CompanyId != @ManagementCompanyId;
DELETE FROM MasterPrograms WHERE CompanyId != @ManagementCompanyId;
DELETE FROM ShiftTypes WHERE CompanyId != @ManagementCompanyId;

-- Chores, OnDuty (all of them - will be recreated)
DELETE FROM Chores; -- All chores (will recreate with Molecule scope)
DELETE FROM OnDuties; -- All OnDuty records (will be replaced by Duty System)

-- Users (except admin)
DELETE FROM AppUsers WHERE Id != @AdminUserId;

-- Companies (except management company)
DELETE FROM Companies WHERE Id != @ManagementCompanyId;

-- Audit logs, config (clean slate)
DELETE FROM AuditLogs;
DELETE FROM ProfileChangeAudits;
DELETE FROM RoleAssignmentAudits;

-- TeamCalendar (will be renamed to Circle)
DELETE FROM TeamCalendars;

-- DirectorCompany (will be replaced by Grants)
DELETE FROM DirectorCompanies;

-- Step 3: Clean up management company data
-- Delete shifts/programs/types within management company
DELETE FROM ShiftAssignments WHERE ShiftInstanceId IN (SELECT Id FROM ShiftInstances WHERE CompanyId = @ManagementCompanyId);
DELETE FROM ShiftInstances WHERE CompanyId = @ManagementCompanyId;
DELETE FROM ShiftPrograms WHERE CompanyId = @ManagementCompanyId;
DELETE FROM MasterPrograms WHERE CompanyId = @ManagementCompanyId;
DELETE FROM ShiftTypes WHERE CompanyId = @ManagementCompanyId;
```

---

### **2.2 Schema Migration Order**

Execute migrations in this order to avoid FK violations:

#### **Migration 1: Create Top-Level Hierarchy**
```bash
dotnet ef migrations add CreateProjectAreaMolecule
```

1. Create **Project** table
2. Create **Area** table (FK to Project)
3. Create **Molecule** table (FK to Area)
4. Create **Department** table (FK to Molecule)
5. Add **DepartmentId** column to **Company** table (FK to Department)

#### **Migration 2: Create JobType System**
```bash
dotnet ef migrations add CreateJobTypeSystem
```

1. Create **JobType** table (FK to Department)
2. Add **JobTypeId** column to **ShiftInstance** table (FK to JobType)
3. Add **PrimaryJobTypeId** column to **AppUser** table (FK to JobType)

#### **Migration 3: Create Grant System**
```bash
dotnet ef migrations add CreateGrantSystem
```

1. Create **Grant** table (FK to AppUser, JobType)
2. Add indexes: (UserId, ScopeType, ScopeId), (JobTypeId), (GrantedAt)

#### **Migration 4: Create Duty System**
```bash
dotnet ef migrations add CreateDutySystem
```

1. Create **DutyRole** table
2. Create **DutyAssignment** table (FK to DutyRole, AppUser)
3. Create **CoverageBlock** table (FK to JobType, AppUser)

#### **Migration 5: Update Chore Scoping**
```bash
dotnet ef migrations add ChoresMoleculeScope
```

1. Add **MoleculeId** column to **Chore** table (FK to Molecule)
2. Drop **CompanyId** column from **Chore** table
3. Update global query filter in AppDbContext

#### **Migration 6: Rename TeamCalendar to Circle**
```bash
dotnet ef migrations add RenameTeamCalendarToCircle
```

1. Rename **TeamCalendars** table to **Circles**
2. Rename **TeamCalendarUsers** table to **CircleMemberships**

#### **Migration 7: Add Hierarchical Columns to AppUser**
```bash
dotnet ef migrations add AppUserHierarchy
```

1. Add **MoleculeId** column to **AppUser** (FK to Molecule)
2. Add **DepartmentId** column to **AppUser** (FK to Department)
3. Add **AreaId** column to **AppUser** (FK to Area)
4. Add **ProjectId** column to **AppUser** (FK to Project)

#### **Migration 8: Deprecate Legacy Columns**
```bash
dotnet ef migrations add DeprecateLegacyFields
```

1. Make **AppUser.Role** column nullable (will be removed later)
2. Make **AppUser.Department** column nullable (now using DepartmentId FK)
3. Drop **DirectorCompany** table (replaced by Grants)
4. Drop **OnDuties** table (replaced by DutyAssignments + CoverageBlocks)

---

### **2.3 Seed Data Script**

After migrations, populate initial hierarchy:

```csharp
public class SeedHierarchyData
{
    public async Task SeedAsync(AppDbContext db)
    {
        // Step 1: Create Project (convert management company to Project)
        var project = new Project
        {
            Name = "IDF Operations", // Or use existing company name
            DisplayName = "IDF Operations",
            Description = "Top-level project",
            CreatedBy = 1, // admin@local user ID
            IsActive = true
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // Step 2: Create Area (only one initially)
        var area = new Area
        {
            ProjectId = project.Id,
            Name = "Central Operations",
            DisplayName = "Central Operations",
            Description = "Primary operational area",
            CreatedBy = 1,
            IsActive = true
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        // Step 3: Create Molecules (user will define these)
        var ellaMolecule = new Molecule
        {
            AreaId = area.Id,
            Name = "Ella",
            DisplayName = "Ella Molecule",
            CreatedBy = 1,
            IsActive = true
        };
        var fightMolecule = new Molecule
        {
            AreaId = area.Id,
            Name = "Fight",
            DisplayName = "Fight Molecule",
            CreatedBy = 1,
            IsActive = true
        };
        db.Molecules.AddRange(ellaMolecule, fightMolecule);
        await db.SaveChangesAsync();

        // Step 4: Create Departments
        var defenceDept = new Department
        {
            MoleculeId = ellaMolecule.Id,
            Name = "Defence",
            DisplayName = "Defence Department",
            CreatedBy = 1,
            IsActive = true
        };
        var logisticsDept = new Department
        {
            MoleculeId = ellaMolecule.Id,
            Name = "Logistics",
            DisplayName = "Logistics Department",
            CreatedBy = 1,
            IsActive = true
        };
        db.Departments.AddRange(defenceDept, logisticsDept);
        await db.SaveChangesAsync();

        // Step 5: Update existing management company
        var managementCompany = await db.Companies.FirstAsync();
        managementCompany.DepartmentId = defenceDept.Id;
        managementCompany.Name = "Defence North"; // Rename
        await db.SaveChangesAsync();

        // Step 6: Create JobTypes (~30 total, user will define)
        var jobTypes = new List<JobType>
        {
            new JobType { DepartmentId = defenceDept.Id, Name = "Recorder", IsOnCallBased = false },
            new JobType { DepartmentId = defenceDept.Id, Name = "Analyst", IsOnCallBased = false },
            new JobType { DepartmentId = defenceDept.Id, Name = "Hakam", IsOnCallBased = true }, // On-call coverage
            // ... add remaining ~27 JobTypes
        };
        db.JobTypes.AddRange(jobTypes);
        await db.SaveChangesAsync();

        // Step 7: Update admin user
        var adminUser = await db.Users.FirstAsync(u => u.Email == "admin@local");
        adminUser.MoleculeId = ellaMolecule.Id;
        adminUser.DepartmentId = defenceDept.Id;
        adminUser.AreaId = area.Id;
        adminUser.ProjectId = project.Id;
        adminUser.PrimaryJobTypeId = null; // Admin doesn't need JobType
        await db.SaveChangesAsync();

        // Step 8: Create Owner grant for admin
        var ownerGrant = new Grant
        {
            UserId = adminUser.Id,
            ScopeType = ScopeType.Project,
            ScopeId = project.Id,
            JobTypeId = null, // All JobTypes
            Action = GrantAction.Configure, // Highest permission
            GrantedBy = adminUser.Id,
            GrantedAt = DateTime.UtcNow
        };
        db.Grants.Add(ownerGrant);
        await db.SaveChangesAsync();

        // Step 9: Create default Duty Roles
        var onDutyLeadRole = new DutyRole
        {
            Name = "On-Duty Lead",
            DisplayName = "On-Duty Lead",
            Description = "Daily escalation leader for Molecule",
            EligibilityRuleJson = "{\"MinimumGrantLevel\": \"Department\"}", // Director+ eligibility
            DefaultScope = ScopeType.Molecule,
            IsActive = true
        };
        db.DutyRoles.Add(onDutyLeadRole);
        await db.SaveChangesAsync();
    }
}
```

---

## Part 3: Permission Service Rewrite

### **3.1 GrantService**

Replaces role-based permission checks with grant-based checks.

```csharp
public interface IGrantService
{
    // Query grants
    Task<List<Grant>> GetUserGrantsAsync(int userId);
    Task<List<Grant>> GetActiveGrantsAsync(int userId); // Filter by timebox

    // Permission checks
    Task<bool> CanViewAsync(int userId, ScopeType scopeType, int scopeId, int? jobTypeId = null);
    Task<bool> CanEditAsync(int userId, ScopeType scopeType, int scopeId, int? jobTypeId = null);
    Task<bool> CanAssignAsync(int userId, ScopeType scopeType, int scopeId, int? jobTypeId = null);
    Task<bool> CanConfigureAsync(int userId, ScopeType scopeType, int scopeId, int? jobTypeId = null);

    // Grant management
    Task<Grant> CreateGrantAsync(int userId, ScopeType scopeType, int scopeId, int? jobTypeId, GrantAction action, int grantedBy, DateTime? startDate = null, DateTime? endDate = null);
    Task RevokeGrantAsync(int grantId, int revokedBy);

    // Validation
    Task ValidateMoleculeBoundaryAsync(int userId, int targetMoleculeId, GrantAction action); // Throws if violates boundary rule
    Task<bool> CanGrantAsync(int granterId, Grant proposedGrant); // Check if granter has authority
}

public class GrantService : IGrantService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public async Task<bool> CanEditAsync(int userId, ScopeType scopeType, int scopeId, int? jobTypeId = null)
    {
        var activeGrants = await GetActiveGrantsAsync(userId);

        foreach (var grant in activeGrants)
        {
            // Check if grant action is Edit or higher
            if (grant.Action < GrantAction.Edit) continue;

            // Check if JobType matches (null = all JobTypes)
            if (jobTypeId.HasValue && grant.JobTypeId.HasValue && grant.JobTypeId != jobTypeId) continue;

            // Check if scope matches or is hierarchically above
            if (ScopeContains(grant.ScopeType, grant.ScopeId, scopeType, scopeId))
            {
                return true;
            }
        }

        return false;
    }

    private bool ScopeContains(ScopeType grantScope, int grantScopeId, ScopeType requestedScope, int requestedScopeId)
    {
        // Project contains everything
        if (grantScope == ScopeType.Project) return true;

        // Same scope and ID
        if (grantScope == requestedScope && grantScopeId == requestedScopeId) return true;

        // Hierarchical checks (Area contains Molecules, Department contains Companies, etc.)
        // TODO: Implement hierarchical scope resolution

        return false;
    }

    public async Task ValidateMoleculeBoundaryAsync(int userId, int targetMoleculeId, GrantAction action)
    {
        if (action != GrantAction.Edit && action != GrantAction.Assign) return; // Boundary only applies to Edit/Assign

        var user = await _db.Users.FindAsync(userId);
        if (user.MoleculeId != targetMoleculeId)
        {
            throw new MoleculeBoundaryViolationException("Cannot grant Edit/Assign permissions outside user's home Molecule");
        }
    }
}
```

---

### **3.2 Authorization Policies Update**

Replace role-based policies with grant-based policies:

```csharp
// Program.cs - Authorization configuration

// ❌ OLD: Role-based policies
// options.AddPolicy("IsManagerOrAdmin", policy => policy.RequireRole("Manager", "Owner", "Director"));

// ✅ NEW: Grant-based policies
options.AddPolicy("CanEdit", policy =>
    policy.AddRequirements(new GrantRequirement(GrantAction.Edit)));

options.AddPolicy("CanAssign", policy =>
    policy.AddRequirements(new GrantRequirement(GrantAction.Assign)));

options.AddPolicy("CanConfigure", policy =>
    policy.AddRequirements(new GrantRequirement(GrantAction.Configure)));

// ✅ NEW: Requirement handler
public class GrantRequirement : IAuthorizationRequirement
{
    public GrantAction RequiredAction { get; }
    public GrantRequirement(GrantAction action) => RequiredAction = action;
}

public class GrantAuthorizationHandler : AuthorizationHandler<GrantRequirement>
{
    private readonly IGrantService _grantService;
    private readonly IHttpContextAccessor _httpContext;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GrantRequirement requirement)
    {
        var userId = int.Parse(context.User.FindFirst(ClaimTypes.NameIdentifier).Value);

        // Extract scope from route/query parameters
        var (scopeType, scopeId, jobTypeId) = ExtractScopeFromContext();

        bool hasPermission = requirement.RequiredAction switch
        {
            GrantAction.View => await _grantService.CanViewAsync(userId, scopeType, scopeId, jobTypeId),
            GrantAction.Edit => await _grantService.CanEditAsync(userId, scopeType, scopeId, jobTypeId),
            GrantAction.Assign => await _grantService.CanAssignAsync(userId, scopeType, scopeId, jobTypeId),
            GrantAction.Configure => await _grantService.CanConfigureAsync(userId, scopeType, scopeId, jobTypeId),
            _ => false
        };

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
```

---

## Part 4: Calendar Generation Logic

### **4.1 Calendar Naming Pattern**

**Format:** `{Scope} — {JobType}` *(or)* `{Scope} — {DutyRole/Coverage}`

**Examples:**

#### Company Job Calendars
- **Defence North — Recorder** (Company-scope shift calendar for Recorders in Defence North)
- **Defence North — Analyst** (Company-scope shift calendar for Analysts in Defence North)
- **Defence South — Recorder**

#### Department Job Rollup Calendars
- **Defence Dept — Recorder** (Aggregate of all Recorder shifts across Defence North + Defence South)
- **Defence Dept — Analyst**

#### Molecule Duty Calendars
- **Ella Molecule — On-Duty Lead** (Responsibility duty calendar)
- **Ella Molecule — Hakam On-Call** (Coverage duty calendar)

#### Personal Calendar
- **My Shifts** (Current user's assigned shifts across all JobTypes)

---

### **4.2 CalendarService**

```csharp
public interface ICalendarService
{
    // List available calendars for a user
    Task<List<CalendarViewModel>> GetAvailableCalendarsAsync(int userId);

    // Load calendar data
    Task<CalendarDataViewModel> LoadCalendarAsync(string calendarId, DateOnly startDate, DateOnly endDate);

    // Calendar CRUD
    Task<string> CreateCompanyJobCalendarAsync(int companyId, int jobTypeId);
    Task<string> CreateDepartmentRollupCalendarAsync(int departmentId, int jobTypeId);
    Task<string> CreateMoleculeDutyCalendarAsync(int moleculeId, int dutyRoleId);
}

public class CalendarViewModel
{
    public string CalendarId { get; set; }      // "company-1-job-5"
    public string DisplayName { get; set; }     // "Defence North — Recorder"
    public CalendarType Type { get; set; }      // CompanyJob, DepartmentRollup, MoleculeDuty, Personal
    public ScopeType Scope { get; set; }
    public int ScopeId { get; set; }
    public int? JobTypeId { get; set; }
    public int? DutyRoleId { get; set; }
    public bool CanEdit { get; set; }           // Based on user's grants
    public bool CanView { get; set; }
}

public enum CalendarType
{
    CompanyJob,        // Shift calendar for specific Company + JobType
    DepartmentRollup,  // Aggregated view of all Companies in Department for a JobType
    MoleculeDuty,      // Duty calendar (On-Duty Lead, On-Call coverage)
    Personal,          // "My Shifts" calendar
    Circle             // Friend/social view
}

public class CalendarService : ICalendarService
{
    public async Task<List<CalendarViewModel>> GetAvailableCalendarsAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Company)
                .ThenInclude(c => c.Department)
            .Include(u => u.Grants)
            .FirstAsync(u => u.Id == userId);

        var calendars = new List<CalendarViewModel>();

        // 1. Company Job Calendars (for user's Company)
        var companyJobTypes = await _db.JobTypes
            .Where(jt => jt.DepartmentId == user.DepartmentId)
            .ToListAsync();

        foreach (var jobType in companyJobTypes)
        {
            var canView = await _grantService.CanViewAsync(userId, ScopeType.Company, user.CompanyId, jobType.Id);
            var canEdit = await _grantService.CanEditAsync(userId, ScopeType.Company, user.CompanyId, jobType.Id);

            if (canView)
            {
                calendars.Add(new CalendarViewModel
                {
                    CalendarId = $"company-{user.CompanyId}-job-{jobType.Id}",
                    DisplayName = $"{user.Company.Name} — {jobType.Name}",
                    Type = CalendarType.CompanyJob,
                    Scope = ScopeType.Company,
                    ScopeId = user.CompanyId,
                    JobTypeId = jobType.Id,
                    CanEdit = canEdit,
                    CanView = canView
                });
            }
        }

        // 2. Department Rollup Calendars (if user has Department-level grants)
        var departmentGrants = user.Grants
            .Where(g => g.ScopeType == ScopeType.Department && g.ScopeId == user.DepartmentId)
            .ToList();

        foreach (var grant in departmentGrants)
        {
            if (grant.JobTypeId.HasValue)
            {
                var jobType = await _db.JobTypes.FindAsync(grant.JobTypeId.Value);
                var department = await _db.Departments.FindAsync(user.DepartmentId);

                calendars.Add(new CalendarViewModel
                {
                    CalendarId = $"department-{user.DepartmentId}-job-{jobType.Id}",
                    DisplayName = $"{department.Name} Dept — {jobType.Name}",
                    Type = CalendarType.DepartmentRollup,
                    Scope = ScopeType.Department,
                    ScopeId = user.DepartmentId,
                    JobTypeId = jobType.Id,
                    CanEdit = grant.Action >= GrantAction.Edit,
                    CanView = grant.Action >= GrantAction.View
                });
            }
        }

        // 3. Molecule Duty Calendars (On-Duty Lead, On-Call coverage)
        var moleculeDutyRoles = await _db.DutyRoles
            .Where(dr => dr.DefaultScope == ScopeType.Molecule && dr.IsActive)
            .ToListAsync();

        foreach (var dutyRole in moleculeDutyRoles)
        {
            calendars.Add(new CalendarViewModel
            {
                CalendarId = $"molecule-{user.MoleculeId}-duty-{dutyRole.Id}",
                DisplayName = $"{user.Molecule.Name} Molecule — {dutyRole.Name}",
                Type = CalendarType.MoleculeDuty,
                Scope = ScopeType.Molecule,
                ScopeId = user.MoleculeId,
                DutyRoleId = dutyRole.Id,
                CanEdit = await _grantService.CanEditAsync(userId, ScopeType.Molecule, user.MoleculeId),
                CanView = true // Duty calendars typically public within Molecule
            });
        }

        // 4. Personal "My Shifts" calendar (always available)
        calendars.Add(new CalendarViewModel
        {
            CalendarId = $"personal-{userId}",
            DisplayName = "My Shifts",
            Type = CalendarType.Personal,
            Scope = ScopeType.Company, // User's company scope
            ScopeId = user.CompanyId,
            CanEdit = false, // Personal calendar is read-only aggregate
            CanView = true
        });

        return calendars;
    }
}
```

---

## Part 5: UI Changes

### **5.1 Grant Request Flow**

**User specified:** "we need to figure a place for users to requests grants. it might require a full ui overhul of the application."

#### **Grant Request Page** (`Pages/Grants/Request.cshtml`)

```html
@page
@model GrantRequestModel
@{
    ViewData["Title"] = Localizer["RequestAccess"];
}

<div class="page-container">
    <div class="page-header">
        <h1 class="page-title"><loc key="RequestAccess">Request Access</loc></h1>
    </div>

    <div class="card">
        <div class="card-body">
            <form method="post">
                <div class="form-group">
                    <label class="form-label"><loc key="AccessScope">Access Scope</loc></label>
                    <select class="form-select" name="ScopeType" onchange="updateScopeOptions(this)">
                        <option value="Company"><loc key="Company">Company</loc></option>
                        <option value="Department"><loc key="Department">Department</loc></option>
                        <option value="Molecule"><loc key="Molecule">Molecule</loc></option>
                    </select>
                </div>

                <div class="form-group">
                    <label class="form-label"><loc key="SelectScope">Select Scope</loc></label>
                    <select class="form-select" name="ScopeId" id="scopeSelector">
                        <!-- Populated dynamically based on ScopeType -->
                    </select>
                </div>

                <div class="form-group">
                    <label class="form-label"><loc key="JobType">Job Type</loc></label>
                    <select class="form-select" name="JobTypeId">
                        @foreach (var jobType in Model.AvailableJobTypes)
                        {
                            <option value="@jobType.Id">@jobType.Name</option>
                        }
                    </select>
                </div>

                <div class="form-group">
                    <label class="form-label"><loc key="RequestedAction">Requested Permission</loc></label>
                    <select class="form-select" name="Action">
                        <option value="View"><loc key="ViewOnly">View Only</loc></option>
                        <option value="Edit"><loc key="EditAssignments">Edit Assignments</loc></option>
                    </select>
                </div>

                <div class="form-group">
                    <label class="form-label"><loc key="Justification">Justification</loc></label>
                    <textarea class="form-input" name="Justification" rows="4" required
                              loc-placeholder="ExplainWhy"
                              placeholder="Explain why you need this access..."></textarea>
                </div>

                <div class="form-group">
                    <label class="toggle-label">
                        <input type="checkbox" name="IsTemporary" onchange="toggleTimebox(this)" />
                        <span class="toggle-slider"></span>
                        <span><loc key="TemporaryAccess">Temporary Access</loc></span>
                    </label>
                </div>

                <div id="timeboxSection" style="display: none;">
                    <div class="form-row">
                        <div class="form-group">
                            <label class="form-label"><loc key="StartDate">Start Date</loc></label>
                            <input type="date" class="form-input" name="StartDate" />
                        </div>
                        <div class="form-group">
                            <label class="form-label"><loc key="EndDate">End Date</loc></label>
                            <input type="date" class="form-input" name="EndDate" />
                        </div>
                    </div>
                </div>

                <div class="form-actions">
                    <button type="submit" class="btn btn-primary">
                        <loc key="SubmitRequest">Submit Request</loc>
                    </button>
                    <a asp-page="/Index" class="btn btn-ghost">
                        <loc key="Cancel">Cancel</loc>
                    </a>
                </div>
            </form>
        </div>
    </div>

    <!-- Pending Requests Section -->
    <div class="card" style="margin-top: var(--space-xl);">
        <div class="card-header">
            <h3 class="card-title"><loc key="MyPendingRequests">My Pending Requests</loc></h3>
        </div>
        <div class="card-body">
            @if (!Model.PendingRequests.Any())
            {
                <div class="empty-state">
                    <span class="empty-state-icon">📋</span>
                    <p><loc key="NoPendingRequests">No pending requests</loc></p>
                </div>
            }
            else
            {
                <table class="data-table">
                    <thead>
                        <tr>
                            <th><loc key="Scope">Scope</loc></th>
                            <th><loc key="JobType">Job Type</loc></th>
                            <th><loc key="Action">Permission</loc></th>
                            <th><loc key="RequestedAt">Requested</loc></th>
                            <th><loc key="Status">Status</loc></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var request in Model.PendingRequests)
                        {
                            <tr>
                                <td>@request.ScopeDisplayName</td>
                                <td>@request.JobTypeName</td>
                                <td>@request.ActionDisplayName</td>
                                <td>@request.RequestedAt.ToString("MMM dd, yyyy")</td>
                                <td>
                                    <span class="badge badge-warning">
                                        <loc key="Pending">Pending</loc>
                                    </span>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>
    </div>
</div>
```

---

#### **Director/Owner Grant Approval Hub** (`Pages/Admin/GrantApprovals.cshtml`)

**User specified:** "request should be accepted in the director hub or owner hub (depending on role)"

```html
@page
@model GrantApprovalsModel
@{
    ViewData["Title"] = Localizer["GrantApprovals"];
}

<div class="page-container">
    <div class="page-header">
        <h1 class="page-title"><loc key="GrantApprovals">Grant Approvals</loc></h1>
        <div class="page-header-actions">
            <span class="badge badge-warning">@Model.PendingCount <loc key="Pending">Pending</loc></span>
        </div>
    </div>

    <div class="card">
        <div class="card-body">
            @if (!Model.PendingRequests.Any())
            {
                <div class="empty-state">
                    <span class="empty-state-icon">✅</span>
                    <p><loc key="NoGrantRequests">No pending grant requests</loc></p>
                </div>
            }
            else
            {
                @foreach (var request in Model.PendingRequests)
                {
                    <div class="grant-request-card">
                        <div class="grant-request-header">
                            <div class="user-info">
                                <div class="user-icon">@request.UserInitials</div>
                                <div>
                                    <h4>@request.UserName</h4>
                                    <span class="text-muted">@request.UserJobTitle • @request.UserCompanyName</span>
                                </div>
                            </div>
                            <span class="request-date">@request.RequestedAt.ToString("MMM dd")</span>
                        </div>

                        <div class="grant-request-body">
                            <div class="grant-details-grid">
                                <div class="grant-detail">
                                    <span class="detail-label"><loc key="RequestedScope">Scope</loc>:</span>
                                    <span class="detail-value">@request.ScopeDisplayName</span>
                                </div>
                                <div class="grant-detail">
                                    <span class="detail-label"><loc key="JobType">Job Type</loc>:</span>
                                    <span class="detail-value">@request.JobTypeName</span>
                                </div>
                                <div class="grant-detail">
                                    <span class="detail-label"><loc key="Permission">Permission</loc>:</span>
                                    <span class="detail-value">@request.ActionDisplayName</span>
                                </div>
                                @if (request.IsTemporary)
                                {
                                    <div class="grant-detail">
                                        <span class="detail-label"><loc key="Duration">Duration</loc>:</span>
                                        <span class="detail-value">
                                            @request.StartDate.ToString("MMM dd") - @request.EndDate.ToString("MMM dd, yyyy")
                                        </span>
                                    </div>
                                }
                            </div>

                            <div class="grant-justification">
                                <span class="detail-label"><loc key="Justification">Justification</loc>:</span>
                                <p>@request.Justification</p>
                            </div>

                            <!-- Molecule Boundary Warning -->
                            @if (request.ViolatesMoleculeBoundary)
                            {
                                <div class="alert alert-warning">
                                    <strong>⚠️ <loc key="MoleculeBoundaryWarning">Molecule Boundary Warning</loc></strong>
                                    <p>
                                        <loc key="CrossMoleculeEditWarning">
                                            This grant would allow editing outside the user's home Molecule (@request.UserMoleculeName).
                                            Cross-Molecule edit grants should be rare and carefully reviewed.
                                        </loc>
                                    </p>
                                </div>
                            }
                        </div>

                        <div class="grant-request-actions">
                            <form method="post" style="display: inline;">
                                <input type="hidden" name="RequestId" value="@request.Id" />
                                <button type="submit" asp-page-handler="Approve" class="btn btn-success">
                                    ✓ <loc key="Approve">Approve</loc>
                                </button>
                            </form>
                            <form method="post" style="display: inline;">
                                <input type="hidden" name="RequestId" value="@request.Id" />
                                <button type="submit" asp-page-handler="Reject" class="btn btn-danger">
                                    ✗ <loc key="Reject">Reject</loc>
                                </button>
                            </form>
                            <button class="btn btn-ghost" onclick="openNegotiateModal(@request.Id)">
                                💬 <loc key="Negotiate">Negotiate</loc>
                            </button>
                        </div>
                    </div>
                }
            }
        </div>
    </div>
</div>

<style>
.grant-request-card {
    background: var(--surface-soft);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    padding: var(--space-lg);
    margin-bottom: var(--space-md);
}

.grant-request-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: var(--space-md);
}

.user-info {
    display: flex;
    align-items: center;
    gap: var(--space-md);
}

.grant-details-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: var(--space-md);
    margin-bottom: var(--space-md);
}

.grant-detail {
    display: flex;
    flex-direction: column;
    gap: var(--space-xs);
}

.detail-label {
    font-size: 0.875rem;
    color: var(--text-muted);
    font-weight: 600;
}

.detail-value {
    font-size: 1rem;
    color: var(--text);
}

.grant-justification {
    padding: var(--space-md);
    background: var(--surface);
    border-radius: var(--radius-sm);
    margin-bottom: var(--space-md);
}

.grant-request-actions {
    display: flex;
    gap: var(--space-sm);
    justify-content: flex-end;
}
</style>
```

---

#### **User Grant Management Page** (`Pages/Admin/Users/Grants.cshtml`)

**User specified:** "should also make a managment place where you select a user and see/edit all its grants. maybe inside my/profile for seeing."

```html
@page "{userId:int}"
@model UserGrantsModel
@{
    ViewData["Title"] = $"{Model.UserName} - Grants";
}

<div class="page-container">
    <div class="page-header">
        <h1 class="page-title">@Model.UserName <span class="text-muted">• Grants</span></h1>
        <div class="page-header-actions">
            <button class="btn btn-primary" onclick="openAddGrantModal()">
                + <loc key="AddGrant">Add Grant</loc>
            </button>
        </div>
    </div>

    <!-- Active Grants -->
    <div class="card">
        <div class="card-header">
            <h3 class="card-title"><loc key="ActiveGrants">Active Grants</loc></h3>
        </div>
        <div class="card-body">
            @if (!Model.ActiveGrants.Any())
            {
                <div class="empty-state">
                    <span class="empty-state-icon">🔒</span>
                    <p><loc key="NoActiveGrants">No active grants</loc></p>
                </div>
            }
            else
            {
                <table class="data-table">
                    <thead>
                        <tr>
                            <th><loc key="Scope">Scope</loc></th>
                            <th><loc key="JobType">Job Type</loc></th>
                            <th><loc key="Permission">Permission</loc></th>
                            <th><loc key="Timeframe">Timeframe</loc></th>
                            <th><loc key="GrantedBy">Granted By</loc></th>
                            <th><loc key="Actions">Actions</loc></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var grant in Model.ActiveGrants)
                        {
                            <tr>
                                <td>@grant.ScopeDisplayName</td>
                                <td>@(grant.JobTypeName ?? "All Jobs")</td>
                                <td>
                                    <span class="badge badge-@GetBadgeColor(grant.Action)">
                                        @grant.ActionDisplayName
                                    </span>
                                </td>
                                <td>
                                    @if (grant.IsTemporary)
                                    {
                                        <span>@grant.StartDate.ToString("MMM dd") - @grant.EndDate.ToString("MMM dd, yyyy")</span>
                                    }
                                    else
                                    {
                                        <span class="text-muted"><loc key="Permanent">Permanent</loc></span>
                                    }
                                </td>
                                <td>@grant.GrantedByName</td>
                                <td>
                                    <form method="post" style="display: inline;">
                                        <input type="hidden" name="GrantId" value="@grant.Id" />
                                        <button type="submit" asp-page-handler="Revoke" class="btn-icon btn-danger"
                                                onclick="return confirm('Revoke this grant?')">
                                            🗑️
                                        </button>
                                    </form>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>
    </div>

    <!-- Revoked Grants (History) -->
    <div class="card" style="margin-top: var(--space-xl);">
        <div class="card-header">
            <h3 class="card-title"><loc key="GrantHistory">Grant History</loc></h3>
        </div>
        <div class="card-body">
            @if (!Model.RevokedGrants.Any())
            {
                <p class="text-muted"><loc key="NoHistory">No revoked grants</loc></p>
            }
            else
            {
                <table class="data-table">
                    <thead>
                        <tr>
                            <th><loc key="Scope">Scope</loc></th>
                            <th><loc key="JobType">Job Type</loc></th>
                            <th><loc key="Permission">Permission</loc></th>
                            <th><loc key="GrantedAt">Granted</loc></th>
                            <th><loc key="RevokedAt">Revoked</loc></th>
                            <th><loc key="RevokedBy">Revoked By</loc></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var grant in Model.RevokedGrants)
                        {
                            <tr class="revoked-row">
                                <td>@grant.ScopeDisplayName</td>
                                <td>@(grant.JobTypeName ?? "All Jobs")</td>
                                <td>@grant.ActionDisplayName</td>
                                <td>@grant.GrantedAt.ToString("MMM dd, yyyy")</td>
                                <td>@grant.RevokedAt?.ToString("MMM dd, yyyy")</td>
                                <td>@grant.RevokedByName</td>
                            </tr>
                        }
                    </tbody>
                </table>
            }
        </div>
    </div>
</div>

<style>
.revoked-row {
    opacity: 0.6;
    text-decoration: line-through;
}
</style>
```

---

### **5.2 Navigation Update**

Update `Pages/Shared/_Layout.cshtml` sidebar navigation:

```html
<!-- Admin Section -->
<div class="nav-section-header"><loc key="Administration">Administration</loc></div>

<a class="app-sidebar-nav-item" asp-page="/Admin/Users/Index">
    <span class="app-sidebar-nav-icon">👥</span>
    <span><loc key="Users">Users</loc></span>
</a>

<a class="app-sidebar-nav-item" asp-page="/Admin/GrantApprovals">
    <span class="app-sidebar-nav-icon">🔐</span>
    <span><loc key="GrantApprovals">Grant Approvals</loc></span>
    @if (Model.PendingGrantCount > 0)
    {
        <span class="nav-badge">@Model.PendingGrantCount</span>
    }
</a>

<a class="app-sidebar-nav-item" asp-page="/Admin/Hierarchy">
    <span class="app-sidebar-nav-icon">🏢</span>
    <span><loc key="OrganizationalStructure">Structure</loc></span>
</a>

<a class="app-sidebar-nav-item" asp-page="/Admin/JobTypes">
    <span class="app-sidebar-nav-icon">💼</span>
    <span><loc key="JobTypes">Job Types</loc></span>
</a>

<!-- User Section -->
<div class="nav-section-header"><loc key="MyAccount">My Account</loc></div>

<a class="app-sidebar-nav-item" asp-page="/Grants/Request">
    <span class="app-sidebar-nav-icon">🙋</span>
    <span><loc key="RequestAccess">Request Access</loc></span>
</a>

<a class="app-sidebar-nav-item" asp-page="/My/Profile">
    <span class="app-sidebar-nav-icon">⚙️</span>
    <span><loc key="MyProfile">My Profile</loc></span>
</a>
```

---

## Part 6: Testing Strategy

### **6.1 Unit Tests**

Create comprehensive unit tests for:

1. **GrantService**
   - Permission resolution (CanView, CanEdit, CanAssign)
   - Hierarchical scope checks (Department grant covers Companies)
   - Molecule boundary validation
   - Timebox expiry handling

2. **CalendarService**
   - Available calendars based on grants
   - Calendar naming generation (Scope + JobType)
   - Rollup calendar aggregation

3. **DutyAssignmentService**
   - Eligibility rule validation
   - Coverage block overlap detection
   - Duty assignment conflict detection

### **6.2 Integration Tests**

Test full workflows:

1. **Grant Request Flow**
   - User submits grant request
   - Director approves grant
   - Grant activates immediately
   - User can access new calendar

2. **Automatic Grant Creation**
   - Create new Company
   - Verify Job Lead grants auto-created for each JobType
   - Create new Department
   - Verify Job Director grants auto-created

3. **Molecule Boundary Enforcement**
   - Attempt cross-Molecule edit grant
   - Verify rejection
   - Verify error message

### **6.3 E2E Tests (Playwright)**

Critical user journeys:

1. **Job Lead Workflow**
   - Job Lead logs in
   - Sees only their Company + JobType calendar
   - Edits shift assignment
   - Verifies change persists

2. **Job Director Workflow**
   - Job Director logs in
   - Sees Department Rollup calendar
   - Views assignments across multiple Companies
   - Edits assignment in Company B (not their home company)
   - Verifies cross-company edit works

3. **Grant Request Workflow**
   - User requests temporary access to different JobType
   - Director approves with 1-month duration
   - User sees new calendar appear
   - 1 month passes (simulate via clock)
   - Calendar disappears (grant expired)

---

## Part 7: Rollout Plan

### **Phase 1: Schema & Migration** (Week 1-2)
- [ ] Create all 8 migrations
- [ ] Execute data cleanup script
- [ ] Run migrations on dev environment
- [ ] Seed initial hierarchy data
- [ ] Verify foreign keys and indexes

### **Phase 2: Grant System** (Week 3-4)
- [ ] Implement GrantService
- [ ] Update authorization policies
- [ ] Create Grant CRUD operations
- [ ] Build Grant Request UI
- [ ] Build Grant Approval Hub

### **Phase 3: Calendar System** (Week 5-6)
- [ ] Implement CalendarService
- [ ] Update calendar generation logic
- [ ] Create Company Job Calendar view
- [ ] Create Department Rollup Calendar view
- [ ] Test calendar permission filtering

### **Phase 4: Duty System** (Week 7-8)
- [ ] Implement Duty Assignment CRUD
- [ ] Implement Coverage Block CRUD
- [ ] Create Duty Board UI (replaces old On-Duty page)
- [ ] Migrate existing OnDuty data to DutyAssignments/CoverageBlocks
- [ ] Test eligibility rules

### **Phase 5: UI Overhaul** (Week 9-10)
- [ ] Update navigation sidebar
- [ ] Create hierarchy management page
- [ ] Create JobType management page
- [ ] Update user profile to show grants
- [ ] Update admin user list to show JobTypes

### **Phase 6: Testing & Polish** (Week 11-12)
- [ ] Unit tests (GrantService, CalendarService)
- [ ] Integration tests (full workflows)
- [ ] E2E tests (critical journeys)
- [ ] Performance testing (large datasets)
- [ ] Security audit (Molecule boundary, permission checks)

### **Phase 7: Documentation & Training** (Week 13-14)
- [ ] Write admin guide (how to manage grants)
- [ ] Write user guide (how to request access)
- [ ] Create video tutorials
- [ ] Update API documentation
- [ ] Prepare rollout announcement

---

## Success Criteria

### **Technical Validation**

✅ All migrations execute without errors
✅ Existing admin@local user can log in and access system
✅ Grants correctly enforce Molecule boundary rule
✅ Calendars generate with correct Scope + JobType naming
✅ Duty System separates Responsibility vs Coverage correctly
✅ Chores are Molecule-scoped (not Company-scoped)
✅ All unit tests pass (>90% coverage)
✅ All integration tests pass
✅ All E2E tests pass
✅ No performance degradation vs current system

### **User Acceptance**

✅ Job Leads can manage their Company + JobType calendars
✅ Job Directors can view/edit Department Rollup calendars
✅ Users can request grants and see pending status
✅ Directors can approve/reject grant requests
✅ Molecule boundary prevents accidental cross-Molecule grants
✅ Calendar list shows only calendars user has access to
✅ "My Shifts" personal calendar shows all user's assignments

---

## Next Steps

1. **Review this plan** - Confirm architectural decisions
2. **Prioritize phases** - Can we parallelize any work?
3. **Create tickets** - Break down each phase into implementable tasks
4. **Begin Phase 1** - Schema & Migration
5. **Daily standups** - Track progress, blockers, questions

---

**Plan Status:** Ready for Review & Implementation
**Estimated Total Effort:** 14 weeks (3.5 months with 1 developer)
**Risk Level:** High (complete architectural rewrite) - mitigated by comprehensive testing
**Recommendation:** Approve and begin Phase 1 immediately
