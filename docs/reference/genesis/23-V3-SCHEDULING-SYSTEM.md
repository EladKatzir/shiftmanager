# 23-V3-SCHEDULING-SYSTEM.md

**ShiftManager - Genesis Documentation**
**Document 23 of 23: V3 Scheduling System Enhancements**

---

## Table of Contents

1. [Overview](#overview)
2. [Shift Type Enhancements](#shift-type-enhancements)
3. [Shift Programs](#shift-programs)
4. [Master Programs](#master-programs)
5. [Shift Groupings](#shift-groupings)
6. [Tech Shift Types](#tech-shift-types)
7. [Setup Tasks](#setup-tasks)
8. [Scheduling Workflow](#scheduling-workflow)
9. [Grant Integration](#grant-integration)
10. [Database Schema](#database-schema)

---

## Overview

### V3 Scheduling Philosophy

ShiftManager V3 enhances the scheduling system to support the new organizational hierarchy:

- **Hierarchy-aware shifts** (shifts scoped to Molecule/JobType/ShiftGrouping)
- **Template-based scheduling** (Programs and MasterPrograms)
- **Tech department support** (specialized tech shift types)
- **Guided setup** (SetupTasks for onboarding)

### Key Changes from V2

| Aspect | V2 (Legacy) | V3 (Current) |
|--------|-------------|--------------|
| **Shift scope** | Company-only | Molecule + JobType + ShiftGrouping |
| **Shift types** | Generic (Morning/Noon/Night) | Type-specific (Alhut, Text, BR, Tech) |
| **Program support** | Single-shift Programs | MasterPrograms (multi-shift) |
| **Tech shifts** | Same as workforce | Specialized (Hanava, Delta, Support) |

---

## Shift Type Enhancements

### V3 ShiftType Model

**File:** `Models/ShiftType.cs`

```csharp
public class ShiftType : IBelongsToCompany
{
    // Standard shift type keys
    public const string KEY_MORNING = "MORNING";
    public const string KEY_MIDDLE = "MIDDLE";
    public const string KEY_AFTERNOON = "AFTERNOON";
    public const string KEY_NIGHT = "NIGHT";
    public const string KEY_OFFLINE = "OFFLINE";
    public const string KEY_EVENING = "EVENING";

    // Tech shift type keys
    public const string TECH_HANAVA = "HANAVA";
    public const string TECH_DELTA = "DELTA";
    public const string TECH_SUPPORT = "SUPPORT";
    public const string TECH_ONCALL = "ONCALL";

    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Key { get; set; } = string.Empty;

    // v3.0: Organizational hierarchy scope
    public int? MoleculeId { get; set; }
    public int? JobTypeId { get; set; }         // For workforce shifts
    public int? ShiftGroupingId { get; set; }   // For grouped shifts
    public string? TechShiftType { get; set; }  // For tech shifts

    public string? CustomName { get; set; }
    public string? NameKey { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    // Navigation
    public Molecule? Molecule { get; set; }
    public JobType? JobType { get; set; }
    public ShiftGrouping? ShiftGrouping { get; set; }
}
```

### Shift Type Categories

#### Workforce Shifts (JobType-based)

| Category | JobType | Shift Types | Scope |
|----------|---------|-------------|-------|
| Alhut | Alhut | Morning, Afternoon, Night | Company + JobType |
| Text | Text | Morning, Afternoon, Night | Company + JobType |
| BR | BR | Morning, Afternoon, Night | Molecule-wide |
| Hakam | Hakam | On-duty periods | Area-wide |

#### Tech Shifts (Department-based)

| TechShiftType | Description | Scope |
|---------------|-------------|-------|
| HANAVA | Engineering on-call | Department |
| DELTA | System monitoring | Department |
| SUPPORT | User support | Department |
| ONCALL | Emergency response | Department |

### Shift Type Scoping

```
Workforce Shift Type:
    CompanyId (required) - Which company owns the shift type
    + JobTypeId (optional) - Which job role uses this shift
    + ShiftGroupingId (optional) - Which grouping for scheduling

Tech Shift Type:
    CompanyId (required) - Company context
    + TechShiftType (required) - Type constant (HANAVA, DELTA, etc.)
```

---

## Shift Programs

### ShiftProgram Model

**Purpose:** Weekly template for generating shift instances. Defines WHEN a shift runs and DEFAULT staffing.

**File:** `Models/ShiftProgram.cs`

```csharp
public class ShiftProgram
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ShiftTypeId { get; set; }

    // v3.0: Organizational hierarchy scope (copied from ShiftType)
    public int? JobTypeId { get; set; }
    public int? ShiftGroupingId { get; set; }
    public string? TechShiftType { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DefaultStaffingRequired { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public int UpdatedBy { get; set; }

    // Navigation
    public ShiftType ShiftType { get; set; } = null!;
    public List<ProgramDay> ProgramDays { get; set; } = new();
    public JobType? JobType { get; set; }
    public ShiftGrouping? ShiftGrouping { get; set; }
}
```

### ProgramDay Model

**Purpose:** Defines which days of the week a Program runs, with optional staffing overrides.

**File:** `Models/ProgramDay.cs`

```csharp
public class ProgramDay
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }  // 0=Sunday, 6=Saturday
    public int? StaffingRequired { get; set; } // Override, or null for default

    public ShiftProgram Program { get; set; } = null!;
}
```

### Program Configuration Example

```
Program: "Alhut Morning - Weekdays"
├── ShiftType: Morning (Alhut)
├── JobType: Alhut
├── ShiftGrouping: Tzafon
├── DefaultStaffing: 3
└── Days:
    ├── Sunday: 3 staff
    ├── Monday: 3 staff
    ├── Tuesday: 3 staff
    ├── Wednesday: 3 staff
    └── Thursday: 4 staff (override for busy day)
```

---

## Master Programs

### MasterProgram Model

**Purpose:** Collection of Programs that form a complete weekly schedule. Apply multiple shift types at once.

**File:** `Models/MasterProgram.cs`

```csharp
public class MasterProgram
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public int UpdatedBy { get; set; }

    // Navigation
    public List<MasterProgramItem> Items { get; set; } = new();
}
```

### MasterProgramItem Model

**Purpose:** Join table linking MasterProgram to its constituent Programs.

**File:** `Models/MasterProgramItem.cs`

```csharp
public class MasterProgramItem
{
    public int Id { get; set; }
    public int MasterProgramId { get; set; }
    public int ProgramId { get; set; }
    public int SortOrder { get; set; } = 0;

    public MasterProgram MasterProgram { get; set; } = null!;
    public ShiftProgram Program { get; set; } = null!;
}
```

### MasterProgram Configuration Example

```
MasterProgram: "Tzafon Full Week"
├── Program: Alhut Morning - Weekdays
├── Program: Alhut Afternoon - Weekdays
├── Program: Alhut Night - Every Day
├── Program: Text Morning - Weekdays
├── Program: Text Afternoon - Weekdays
└── Program: BR All Day - Weekend

Apply this MasterProgram → Generates all shift instances for the week
```

### Use Cases

1. **Weekly Schedule Setup:** Apply entire week's shifts with one action
2. **Rotation Templates:** Different MasterPrograms for different rotation patterns
3. **Special Events:** Holiday schedules, exercise periods
4. **Quick Changes:** Swap out one MasterProgram for another

---

## Shift Groupings

### ShiftGrouping Model

**Purpose:** Groups companies within a Molecule for coordinated shift scheduling.

**File:** `Models/ShiftGrouping.cs`

```csharp
public class ShiftGrouping
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Molecule Molecule { get; set; } = null!;
    public List<ShiftGroupingCompany> Companies { get; set; } = new();
    public List<ShiftGroupingJobType> JobTypes { get; set; } = new();
}
```

### Grouping Relationships

```
ShiftGrouping: Tzafon (צפון)
├── MoleculeId: Oren
├── Companies:
│   ├── Tzafona
│   └── City
└── JobTypes:
    ├── Alhut
    └── Text

Meaning: Alhut and Text shifts for Tzafona + City are scheduled together as "Tzafon"
```

### Join Tables

**ShiftGroupingCompany:**
```csharp
public class ShiftGroupingCompany
{
    public int Id { get; set; }
    public int ShiftGroupingId { get; set; }
    public int CompanyId { get; set; }
}
```

**ShiftGroupingJobType:**
```csharp
public class ShiftGroupingJobType
{
    public int Id { get; set; }
    public int ShiftGroupingId { get; set; }
    public int JobTypeId { get; set; }
}
```

### Scheduling with Groupings

When assigning shifts:
1. Select ShiftGrouping (e.g., "Tzafon")
2. All employees from grouped companies are eligible
3. Shifts are tracked at the grouping level, not company level
4. Reporting aggregates by grouping

---

## Tech Shift Types

### Tech Shift Type Constants

```csharp
// ShiftType.cs
public const string TECH_HANAVA = "HANAVA";    // Engineering on-call
public const string TECH_DELTA = "DELTA";      // System monitoring
public const string TECH_SUPPORT = "SUPPORT";  // User support
public const string TECH_ONCALL = "ONCALL";    // Emergency response
```

### Tech Shift Characteristics

| Type | Description | Duration | Staffing |
|------|-------------|----------|----------|
| HANAVA | Engineering on-call | 8-12 hours | 1-2 per department |
| DELTA | System monitoring | 24-hour rotation | 1 per shift |
| SUPPORT | User support | Business hours | Variable |
| ONCALL | Emergency response | 24/7 coverage | 1 per area |

### Tech Shift Scoping

Tech shifts are scoped to **Departments** within **Tech Molecules**:

```
Shikma (Tech Molecule)
├── Department: Pie
│   ├── HANAVA shift program
│   └── ONCALL coverage
├── Department: Tao
│   ├── DELTA monitoring
│   └── SUPPORT schedule
└── Department: Yekeb
    └── DELTA monitoring
```

### Tech vs Workforce Comparison

| Aspect | Workforce Shifts | Tech Shifts |
|--------|-----------------|-------------|
| Scope | Company + JobType + ShiftGrouping | Department |
| Types | Morning, Afternoon, Night | HANAVA, DELTA, SUPPORT, ONCALL |
| Duration | Fixed (8 hours typical) | Variable (8-24 hours) |
| Staffing | Multiple per shift | Usually 1-2 |
| Rotation | Weekly | Daily or on-call |

---

## Setup Tasks

### SetupTask Model

**Purpose:** Guided onboarding tasks for setting up a new organizational unit.

**File:** `Models/SetupTask.cs`

```csharp
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

### SetupTaskType Enum

**File:** `Models/Support/SetupTaskType.cs`

```csharp
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
```

### SetupTaskStatus Enum

**File:** `Models/Support/SetupTaskStatus.cs`

```csharp
public enum SetupTaskStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Skipped = 3
}
```

### Setup Task Workflow

When a new Molecule/Company is created:

```
1. System generates SetupTasks:
   ├── AssignMoleculeAdmin (if new molecule)
   ├── AssignAlhutDirector
   ├── AssignTextDirector
   ├── AssignBRDirector (per company)
   ├── AssignAlhutLead (per company)
   ├── AssignTextLead (per company)
   ├── AssignAssigners
   ├── SetupShiftGroupings
   ├── SetupDutyPrograms
   └── SetupShiftBlueprints

2. Tasks assigned to AreaAdmin/MoleculeAdmin

3. Admin completes tasks in sequence:
   - Assign roles to users
   - Configure shift groupings
   - Create programs

4. System marks tasks as Completed

5. Organization ready for scheduling
```

### Task Dependencies

```
AssignMoleculeAdmin
    ↓ (must be first)
AssignAlhutDirector / AssignTextDirector
    ↓
AssignBRDirector / AssignAlhutLead / AssignTextLead
    ↓
SetupShiftGroupings
    ↓
SetupShiftBlueprints / SetupDutyPrograms
```

---

## Scheduling Workflow

### Complete Scheduling Flow

```
1. SETUP (One-time)
   ├── Create organizational hierarchy (Project → Area → Molecule → Company)
   ├── Define JobTypes at Area level
   ├── Set up ShiftGroupings per Molecule
   ├── Assign roles (Directors, Leads, Assigners)
   └── Create ShiftTypes and Programs

2. PLANNING (Weekly/Monthly)
   ├── Select MasterProgram for the period
   ├── Apply to generate ShiftInstances
   └── Adjust staffing as needed

3. ASSIGNMENT (Daily/Weekly)
   ├── View shifts requiring assignment
   ├── Filter by Molecule/Company/JobType/ShiftGrouping
   ├── Assign eligible users to shifts
   └── Handle conflicts and constraints

4. EXECUTION (Ongoing)
   ├── Users view their schedules
   ├── Swap requests processed
   ├── Time-off requests approved/denied
   └── Actual attendance tracked

5. REPORTING (Periodic)
   ├── Shift coverage reports
   ├── Hours worked by user/company/jobtype
   └── Compliance and audit
```

### Filter Chain for Shift Assignment

```
All ShiftInstances
    ↓ Filter by Molecule
Available shifts in Molecule
    ↓ Filter by ShiftGrouping (optional)
Shifts for specific grouping
    ↓ Filter by JobType
Shifts for specific job role
    ↓ Filter by Date Range
Shifts needing assignment
    ↓ Match eligible users
Users with appropriate grants and JobType
    ↓ Check constraints
Valid assignments (no conflicts)
    ↓ Assign
ShiftAssignment created
```

---

## Grant Integration

### Scheduling-Related Grants

| Grant | Scope | Purpose |
|-------|-------|---------|
| ViewShifts | Company | View shift schedule |
| ViewAllShifts | Molecule | View shifts across molecule |
| AssignAlhutShifts | Company | Assign Alhut job type shifts |
| AssignTextShifts | Company | Assign Text job type shifts |
| AssignBRShifts | Molecule | Assign BR shifts molecule-wide |
| AssignTechShifts | Department | Assign tech department shifts |
| EditShiftPrograms | Company | Modify shift programs |
| CreateShiftPrograms | Company | Create new programs |
| ManageShiftGroupings | Molecule | Configure shift groupings |

### Permission Check Example

```csharp
// Check if user can assign Alhut shifts in a specific company
var canAssign = await _grantService.HasGrantWithScopeAsync(
    userId,
    "AssignAlhutShifts",
    companyId: targetCompanyId,
    jobTypeId: alhutJobTypeId
);

if (!canAssign)
{
    // Check if user has molecule-wide grant
    canAssign = await _grantService.HasGrantWithScopeAsync(
        userId,
        "AssignAlhutShifts",
        moleculeId: targetMoleculeId
    );
}
```

### Role-Based Scheduling Access

| Role | Scheduling Permissions |
|------|----------------------|
| Owner | All shifts across project |
| AreaAdmin | All shifts in area |
| MoleculeAdmin | All shifts in molecule |
| AlhutDirector | Alhut shifts in molecule |
| TextDirector | Text shifts in molecule |
| BRDirector | BR shifts in company |
| AlhutLead | Alhut shifts in company |
| TextLead | Text shifts in company |
| DepartmentLead | Tech shifts in department |
| Assigner | Chores only (not shifts) |

---

## Database Schema

### V3 Scheduling Tables

| Table | Purpose | Key Columns |
|-------|---------|-------------|
| ShiftTypes | Shift definitions | Id, CompanyId, Key, MoleculeId, JobTypeId, ShiftGroupingId, TechShiftType |
| ShiftPrograms | Weekly templates | Id, CompanyId, ShiftTypeId, JobTypeId, ShiftGroupingId, TechShiftType |
| ProgramDays | Day-of-week config | Id, ProgramId, DayOfWeek, StaffingRequired |
| MasterPrograms | Program collections | Id, CompanyId, Name, Description |
| MasterProgramItems | Program membership | Id, MasterProgramId, ProgramId, SortOrder |
| ShiftGroupings | Company groupings | Id, MoleculeId, Name |
| ShiftGroupingCompany | Grouping membership | ShiftGroupingId, CompanyId |
| ShiftGroupingJobType | Grouping applicability | ShiftGroupingId, JobTypeId |
| SetupTasks | Onboarding tasks | Id, Type, MoleculeId, CompanyId, Status |

### Foreign Key Relationships

```sql
-- Shift type scoping
ShiftTypes.MoleculeId → Molecules.Id
ShiftTypes.JobTypeId → JobTypes.Id
ShiftTypes.ShiftGroupingId → ShiftGroupings.Id

-- Program hierarchy
ShiftPrograms.ShiftTypeId → ShiftTypes.Id
ShiftPrograms.JobTypeId → JobTypes.Id
ShiftPrograms.ShiftGroupingId → ShiftGroupings.Id
ProgramDays.ProgramId → ShiftPrograms.Id

-- Master programs
MasterProgramItems.MasterProgramId → MasterPrograms.Id
MasterProgramItems.ProgramId → ShiftPrograms.Id

-- Shift groupings
ShiftGroupings.MoleculeId → Molecules.Id
ShiftGroupingCompany.ShiftGroupingId → ShiftGroupings.Id
ShiftGroupingCompany.CompanyId → Companies.Id
ShiftGroupingJobType.ShiftGroupingId → ShiftGroupings.Id
ShiftGroupingJobType.JobTypeId → JobTypes.Id

-- Setup tasks
SetupTasks.MoleculeId → Molecules.Id
SetupTasks.CompanyId → Companies.Id
SetupTasks.JobTypeId → JobTypes.Id
SetupTasks.AssignedToUserId → AppUsers.Id
```

---

## Related Documents

- **[21-V3-ORGANIZATIONAL-HIERARCHY.md](21-V3-ORGANIZATIONAL-HIERARCHY.md)** - Hierarchy that shifts are scoped to
- **[22-V3-GRANT-AUTHORIZATION.md](22-V3-GRANT-AUTHORIZATION.md)** - Scheduling permissions
- **[14-WORKFLOWS-AND-BUSINESS-LOGIC.md](14-WORKFLOWS-AND-BUSINESS-LOGIC.md)** - Shift assignment workflows
- **[03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md)** - Complete database schema

---

**Document Version:** 1.0
**Created:** 2026-01-29
**Codebase Version:** V3.0
