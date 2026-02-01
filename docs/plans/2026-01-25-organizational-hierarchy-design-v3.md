# Organizational Hierarchy Redesign - Complete Design Document

**Date:** 2026-01-25
**Status:** ✅ FINALIZED - Ready for Implementation
**Version:** 3.0

---

## Executive Summary

This document describes the complete redesign of ShiftManager's organizational model based on real-world military structure mapping. Key changes from v2.0:

- **Hierarchy corrected**: JobType is a USER attribute, not a hierarchical level
- **Workforce vs Tech molecules**: Different structures for different molecule types
- **Grant-based permissions**: 107 built-in grants with dynamic creation capability
- **Role templates**: 11 default roles with configurable auto-grants
- **Circle/Friends system**: Cross-molecule visibility for connected users

---

## Table of Contents

1. [Organizational Hierarchy](#1-organizational-hierarchy)
2. [Molecule Types](#2-molecule-types)
3. [JobTypes](#3-jobtypes)
4. [Role System](#4-role-system)
5. [Grant System](#5-grant-system)
6. [Shift System](#6-shift-system)
7. [Duty System](#7-duty-system)
8. [Chore System](#8-chore-system)
9. [Vacation System](#9-vacation-system)
10. [Circle/Friends System](#10-circlefriends-system)
11. [Settings System](#11-settings-system)
12. [Database Schema](#12-database-schema)
13. [Seed Data](#13-seed-data)
14. [Migration Strategy](#14-migration-strategy)

---

## 1. Organizational Hierarchy

### 1.1 Real-World Structure

```
Project: Shifty (שיפטי)
└── Area: 190
    ├── Molecule: Oren (אורן) [Workforce]
    │   ├── Company: Tzafona (צפונה)
    │   │   ├── User: Alice (JobType: Alhut)
    │   │   ├── User: Bob (JobType: BR)
    │   │   ├── User: Carol (JobType: Text)
    │   │   └── User: David (JobType: Hakam)
    │   ├── Company: Hir (חיר)
    │   ├── Company: Camps (מחנות)
    │   ├── Company: City (העיר)
    │   └── Company: Radio (טקטי)
    │
    ├── Molecule: Ella (אלה) [Workforce]
    │   ├── Company: Hitazmut (התעצמות)
    │   ├── Company: GAP (גא"פ)
    │   └── Company: Yeadim (יעדים)
    │
    ├── Molecule: Harava (ערבה) [Workforce]
    │   └── Company: Element (אלמנט)
    │
    ├── Molecule: Shaked (שקד) [Workforce]
    │   ├── Company: Inside (פנים)
    │   └── Company: Out (חוץ)
    │
    ├── Molecule: Gefen (גפן) [Workforce]
    │   ├── Company: Hamasa (חמסה)
    │   ├── Company: Kabah (קבה"ח)
    │   └── Company: Matot (מטות)
    │
    ├── Molecule: Shikma (שקמה) [Tech]
    │   ├── Department: Pie (פאי)
    │   ├── Department: Tao (טאו)
    │   ├── Department: Yekev (יקב)
    │   ├── Department: Snir (שניר)
    │   ├── Department: Arbel (ארבל)
    │   └── Department: Samapkam (סמפקמה)
    │
    ├── Molecule: NOC (נגדים) [Helper]
    │
    └── Molecule: Shiklut (שיקלוט) [Helper]
```

### 1.2 Key Insight: JobType is a User Attribute

**CRITICAL CHANGE FROM v2.0:**

- JobType is NOT a hierarchical level between Molecule and Company
- JobType is an attribute of each USER
- A Company contains users of ALL JobTypes (Alhut, BR, Text, Hakam)
- The same physical Company (e.g., "Tzafona") has users with different JobTypes

**Example:**
```
Company: Tzafona (led by BR Director)
├── User: Alice, JobType=Alhut
├── User: Bob, JobType=BR
├── User: Carol, JobType=Text
└── User: David, JobType=Hakam
```

### 1.3 Hierarchy Levels

| Level | Purpose | Contains |
|-------|---------|----------|
| **Project** | Top-level (Shifty) | Areas |
| **Area** | Administrative boundary (190) | Molecules |
| **Molecule** | Operational unit (Oren, Shikma) | Companies OR Departments |
| **Company** | Team unit (Tzafona) - Workforce only | Users |
| **Department** | Tech unit (Pie, Tao) - Tech only | Users |
| **User** | Individual with Company + JobType | - |

---

## 2. Molecule Types

### 2.1 Three Molecule Types

| Type | Examples | Structure | JobTypes |
|------|----------|-----------|----------|
| **Workforce** | Oren, Ella, Gefen, Harava, Shaked | Companies | Alhut, BR, Text, Hakam |
| **Tech** | Shikma | Departments | None (single job) |
| **Helper** | Shiklut, NOC | Flat (future-ready) | None |

### 2.2 Workforce Molecules

- Have Companies (Tzafona, Hir, etc.)
- Users have JobTypes (Alhut, BR, Text, Hakam)
- Shifts organized by JobType
- BR Director is company boss

### 2.3 Tech Molecule (Shikma)

- Have Departments instead of Companies (Pie, Tao, Yekev, Snir, Arbel, Samapkam)
- No JobTypes - everyone is the same job
- Shifts: Hanava, Delta, Yekev, Moviltech
- Department eligibility rules:
  - Pie + Tao → Hanava or Delta
  - Samapkam → Delta only
  - Yekev → Yekev shift
  - Moviltech → Department heads only
- **Special**: Snir manages Hakam shifts area-wide

### 2.4 Helper Molecules (Shiklut, NOC)

- Flat structure (no Companies or Departments)
- On-call/availability based
- Shiklut has chores
- Future-ready for shifts

---

## 3. JobTypes

### 3.1 JobType Definitions (Workforce Only)

| JobType | Hebrew | Shift Model | Managed By |
|---------|--------|-------------|------------|
| **Alhut** | אלחוט | Company groupings | Alhut Lead/Director |
| **BR** | ב"ר | Molecule-wide (3 per shift) | BR Director + Molecule Admin |
| **Text** | טקסט | Company groupings (same as Alhut) | Text Lead/Director |
| **Hakam** | חק"ם | Area-wide (1+1 backup) | Snir (special grant) |

### 3.1.1 JobType Assignment Rules

| Rule | Behavior |
|------|----------|
| **One JobType per user** | Each user has exactly ONE JobType |
| **No hat switching** | Users don't switch between JobTypes |
| **Admin can change** | Admin can permanently change a user's JobType |
| **Change triggers** | Changing JobType may require reassigning shifts/grants |

**No Hat Switching System** - When a user needs to change JobType (e.g., Alhut → BR), admin edits the user record directly. The old JobType's grants are removed, new JobType's grants are added.

### 3.2 Shift Groupings (Alhut & Text Only)

Configurable by Molecule Admin. Example for Oren:

| Grouping Name | Companies Included | JobTypes |
|---------------|-------------------|----------|
| Tzafon | Tzafona + City | Alhut, Text |
| Darom | Camps + Hir | Alhut, Text |
| Tacti | Radio | Alhut, Text |

**BR does NOT use groupings** - shifts are molecule-wide.

### 3.3 Database Schema

```csharp
public class JobType
{
    public int Id { get; set; }
    public int AreaId { get; set; }  // Area-scoped (same JobTypes across molecules)
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Area Area { get; set; } = null!;
}

public class ShiftGrouping
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;  // "Tzafon", "Darom"
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<ShiftGroupingCompany> Companies { get; set; } = new();
    public List<ShiftGroupingJobType> JobTypes { get; set; } = new();
}

public class ShiftGroupingCompany
{
    public int ShiftGroupingId { get; set; }
    public int CompanyId { get; set; }
}

public class ShiftGroupingJobType
{
    public int ShiftGroupingId { get; set; }
    public int JobTypeId { get; set; }
}
```

---

## 4. Role System

### 4.1 Role Templates (11 Total)

| # | Role | Scope Level | Count | Notes |
|---|------|-------------|-------|-------|
| 1 | **Employee** | Implicit | All users | Default, no explicit assignment |
| 2 | **Assigner** | Molecule | 2 per molecule | Chores only, usually regular employee |
| 3 | **Alhut Lead** | Company+JobType | 1 per company | Alhut users + shifts molecule-wide |
| 4 | **Text Lead** | Company+JobType | 1 per company | Text users + shifts molecule-wide |
| 5 | **BR Director** | Company | 1 per company | Company boss, BR+Hakam vacations/users |
| 6 | **Alhut Director** | Molecule+JobType | 1 per molecule | All Alhut users + shifts |
| 7 | **Text Director** | Molecule+JobType | 1 per molecule | All Text users + shifts |
| 8 | **Department Lead** | Department | 1 per dept | Tech molecule only |
| 9 | **Molecule Admin** | Molecule | 1 per molecule | Full molecule authority |
| 10 | **Area Admin** | Area | varies | God mode except project technical |
| 11 | **Owner** | Project | 1 | SystemAdmin grant |

### 4.2 Role Assignment Schema

```csharp
public class RoleTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "BRDirector", "AlhutLead"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public RoleScopeLevel ScopeLevel { get; set; }
    public bool IsSystem { get; set; }  // Built-in vs custom
    public bool IsActive { get; set; } = true;
}

public enum RoleScopeLevel
{
    Implicit,    // Employee - no assignment needed
    Company,     // BR Director
    CompanyJobType,  // Alhut Lead, Text Lead
    Department,  // Department Lead (tech)
    Molecule,    // Molecule Admin, Assigner
    MoleculeJobType,  // Alhut Director, Text Director
    Area,        // Area Admin
    Project      // Owner
}

public class UserRole
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
    public DateTime AssignedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public User User { get; set; } = null!;
    public RoleTemplate RoleTemplate { get; set; } = null!;
}
```

### 4.3 Role Combination Rules

| Rule | Behavior |
|------|----------|
| Employee + Assigner | ✅ Allowed (common case) |
| Lead + Assigner | ✅ Allowed (unusual) |
| Director + Molecule Admin | ❌ Not allowed (redundant) |
| Lead + Director (same JobType) | ❌ Not allowed (auto-remove Lead) |
| Cross-molecule roles | ✅ Allowed (edge case) |

### 4.4 Role Promotion/Demotion

| Action | System Behavior |
|--------|-----------------|
| Lead → Director (same JobType) | Auto-remove Lead role |
| Director removed | Configurable (Employee or Lead) |
| Multiple roles | Assigner only combinable with others |

---

## 5. Grant System

### 5.1 Grant Philosophy

**Grant-based, not role-based:**
- Instead of "BR Director can assign BR shifts"
- We have: Grant `AssignBRShifts` is **auto-given** to BR Director
- Every permission is a grant that can be added/removed independently

### 5.2 Grant Schema

```csharp
public class GrantType
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "AssignAlhutShifts"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public GrantCategory Category { get; set; }
    public GrantScopeLevel DefaultScope { get; set; }
    public bool IsSystem { get; set; }  // true = built-in, false = custom
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
}

public enum GrantCategory
{
    Shift,
    Duty,
    Chore,
    Vacation,
    Swap,
    UserManagement,
    GrantManagement,
    Hierarchy,
    Settings,
    Analytics,
    Email,
    System,
    Custom
}

public enum GrantScopeLevel
{
    Self,
    Company,
    Department,
    Molecule,
    Area,
    Project
}

public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GrantTypeId { get; set; }

    // Hierarchical Scope
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
    public DateTime GrantedAt { get; set; }
    public string? Notes { get; set; }
    public bool IsAutoGrant { get; set; }  // true = from role template

    // Navigation
    public User User { get; set; } = null!;
    public GrantType GrantType { get; set; } = null!;
}
```

### 5.3 Complete Grant List (107 Built-in)

#### Shift Grants (Workforce) - 17 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewAlhutShiftCalendar` | Molecule | Alhut users, Leads+, Directors+, Admin+ |
| `ViewTextShiftCalendar` | Molecule | Text users, Leads+, Directors+, Admin+ |
| `ViewBRShiftCalendar` | Molecule | BR users, BR Director+, Admin+ |
| `ViewHakamShiftCalendar` | Area | Hakam users, Snir grant holders |
| `AssignAlhutShifts` | Molecule | Alhut Lead, Alhut Director, Molecule Admin+ |
| `AssignTextShifts` | Molecule | Text Lead, Text Director, Molecule Admin+ |
| `AssignBRShifts` | Molecule | BR Director, Molecule Admin+ |
| `AssignHakamShifts` | Area | Snir users (manual grant) |
| `ManageAlhutBlueprints` | Molecule | Alhut Director, Molecule Admin+ |
| `ManageTextBlueprints` | Molecule | Text Director, Molecule Admin+ |
| `ManageBRBlueprints` | Molecule | BR Director, Molecule Admin+ |
| `ManageHakamBlueprints` | Area | Area Admin+ |
| `ManageAlhutPrograms` | Molecule | Alhut Director, Molecule Admin+ |
| `ManageTextPrograms` | Molecule | Text Director, Molecule Admin+ |
| `ManageBRPrograms` | Molecule | Molecule Admin+ |
| `ManageHakamPrograms` | Area | Area Admin+ |
| `ManageShiftGroupings` | Molecule | Molecule Admin+ |

#### Workforce Eligibility Grants - 4 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `CanBeAssignedAlhutShifts` | Molecule | Users with JobType=Alhut |
| `CanBeAssignedTextShifts` | Molecule | Users with JobType=Text |
| `CanBeAssignedBRShifts` | Molecule | Users with JobType=BR |
| `CanBeAssignedHakamShifts` | Area | Users with JobType=Hakam |

#### Shift Grants (Tech/Shikma) - 16 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewHanavaCalendar` | Molecule | Pie, Tao users |
| `ViewDeltaCalendar` | Molecule | Pie, Tao, Samapkam users |
| `ViewYekevCalendar` | Molecule | Yekev users |
| `ViewMoviltechCalendar` | Molecule | Department heads |
| `AssignHanavaShifts` | Molecule | Department Lead, Molecule Admin+ |
| `AssignDeltaShifts` | Molecule | Department Lead, Molecule Admin+ |
| `AssignYekevShifts` | Molecule | Department Lead, Molecule Admin+ |
| `AssignMoviltechShifts` | Molecule | Molecule Admin+ |
| `ManageHanavaBlueprints` | Molecule | Molecule Admin+ |
| `ManageDeltaBlueprints` | Molecule | Molecule Admin+ |
| `ManageYekevBlueprints` | Molecule | Molecule Admin+ |
| `ManageMoviltechBlueprints` | Molecule | Molecule Admin+ |
| `ManageHanavaPrograms` | Molecule | Molecule Admin+ |
| `ManageDeltaPrograms` | Molecule | Molecule Admin+ |
| `ManageYekevPrograms` | Molecule | Molecule Admin+ |
| `ManageMoviltechPrograms` | Molecule | Molecule Admin+ |

#### Tech Eligibility Grants - 4 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `CanBeAssignedHanava` | User | Pie, Tao department users |
| `CanBeAssignedDelta` | User | Pie, Tao, Samapkam department users |
| `CanBeAssignedYekev` | User | Yekev department users |
| `CanBeAssignedMoviltech` | User | Department heads |

#### Helper Molecule Grants (Future-Ready) - 10 grants

| Grant | Default Scope | Description |
|-------|---------------|-------------|
| `ViewShiklutCalendar` | Molecule | View Shiklut on-call calendar |
| `AssignShiklutOnCall` | Molecule | Assign Shiklut on-call |
| `CanBeShiklutOnCall` | Molecule | Eligible for Shiklut on-call |
| `ManageShiklutBlueprints` | Molecule | Manage Shiklut templates |
| `ManageShiklutPrograms` | Molecule | Manage Shiklut auto-scheduling |
| `ViewNOCCalendar` | Molecule | View NOC on-call calendar |
| `AssignNOCOnCall` | Molecule | Assign NOC on-call |
| `CanBeNOCOnCall` | Molecule | Eligible for NOC on-call |
| `ManageNOCBlueprints` | Molecule | Manage NOC templates |
| `ManageNOCPrograms` | Molecule | Manage NOC auto-scheduling |

#### Duty Grants (Katzin On-Call) - 5 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewKatzinCalendar` | Area | Director-level+ |
| `AssignKatzinOnCall` | Area | Director-level+, Area Admin+ |
| `CanBeKatzinOnCall` | Area | All Director-level users |
| `ManageKatzinBlueprints` | Area | Area Admin+ |
| `ManageKatzinPrograms` | Area | Area Admin+ |

#### Chore Grants - 3 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewChoreCalendar` | Molecule | All users in molecule |
| `AssignChores` | Molecule | Assigner role, Leads+, Molecule Admin+ |
| `ManageChoreTypes` | Molecule | Molecule Admin+ |

#### Vacation Grants - 8 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewOwnVacations` | Self | All users |
| `RequestVacation` | Self | All users |
| `ViewCompanyVacations` | Company | All users in company |
| `ViewMoleculeVacations` | Molecule | Molecule Admin+ |
| `ViewAreaVacations` | Area | Area Admin+ |
| `ApproveVacations` | Company+JobType | Leads, Directors, BR Director (BR+Hakam) |
| `EditVacations` | Company+JobType | Leads+, Directors+ |
| `DeleteVacations` | Company+JobType | Molecule Admin+ |

#### Swap Grants - 3 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `RequestSwap` | Self | All users |
| `ViewSwapRequests` | Company | Leads+ |
| `ApproveSwaps` | Company+JobType | Leads+, Directors+ |

#### User Management Grants - 10 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewOwnProfile` | Self | All users |
| `EditOwnProfile` | Self | All users |
| `ViewCompanyUsers` | Company | All users in company |
| `ViewMoleculeUsers` | Molecule | Molecule Admin+ |
| `ViewAreaUsers` | Area | Area Admin+ |
| `CreateUsers` | Company+JobType | Leads+, BR Director (BR+Hakam) |
| `EditUsers` | Company+JobType | Leads+, BR Director (BR+Hakam) |
| `DeleteUsers` | Molecule | Molecule Admin+ |
| `AssignJobType` | Molecule | Molecule Admin+ |
| `TransferUser` | Molecule | BR Director (BR+Hakam), Molecule Admin+ |

#### Grant Management - 2 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewGrants` | varies | Molecule Admin+ |
| `ManageGrants` | varies | Molecule Admin+, Area Admin+ |

#### Hierarchy Management - 6 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewHierarchy` | varies | All users (own scope) |
| `ManageCompanies` | Molecule | Molecule Admin+ |
| `ManageMolecules` | Area | Area Admin+ |
| `ManageAreas` | Project | Owner |
| `ManageDepartments` | Molecule | Molecule Admin+ (tech only) |
| `ManageJobTypes` | Area | Area Admin+ |

#### Settings Grants - 5 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewSettings` | varies | Leads+ |
| `EditCompanySettings` | Company | BR Director, Molecule Admin+ |
| `EditMoleculeSettings` | Molecule | Molecule Admin+ |
| `EditAreaSettings` | Area | Area Admin+ |
| `EditProjectSettings` | Project | Owner |

#### Analytics Grants - 5 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewOwnAnalytics` | Self | All users |
| `ViewCompanyAnalytics` | Company | Leads+, BR Director+ |
| `ViewMoleculeAnalytics` | Molecule | Molecule Admin+ |
| `ViewAreaAnalytics` | Area | Area Admin+ |
| `ViewFairnessStats` | Molecule | Leads+, Molecule Admin+ |

#### Email/Communication Grants - 4 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `ViewEmailTemplates` | Molecule | Molecule Admin+ |
| `EditEmailTemplates` | Molecule | Molecule Admin+ |
| `SendEmails` | Molecule | Leads+, Molecule Admin+ |
| `SendMassEmails` | Area | Area Admin+ |

#### System Grants - 5 grants

| Grant | Default Scope | Auto-Given To |
|-------|---------------|---------------|
| `SystemAdmin` | Project | Owner only |
| `ManageADFS` | Project | Owner only |
| `ManageIntegrations` | Project | Owner only |
| `ViewAuditLogs` | varies | Molecule Admin+ |
| `ManageBackups` | Project | Owner only |

### 5.4 Grant Count Summary

| Category | Count |
|----------|-------|
| Shift (Workforce) | 17 |
| Workforce Eligibility | 4 |
| Shift (Tech) | 16 |
| Tech Eligibility | 4 |
| Helper Molecules | 10 |
| Duty (Katzin) | 5 |
| Chore | 3 |
| Vacation | 8 |
| Swap | 3 |
| User Management | 10 |
| Grant Management | 2 |
| Hierarchy | 6 |
| Settings | 5 |
| Analytics | 5 |
| Email | 4 |
| System | 5 |
| **TOTAL** | **107** |

### 5.5 Auto-Grant Configuration

```csharp
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
    SameAsRole,        // Grant scope = role scope
    ExpandToMolecule,  // Expand to molecule (shift assignment)
    ExpandToArea,      // Expand to area (Katzin, Hakam)
    Custom             // Explicit scope
}
```

### 5.6 Dynamic Grant Creation

Owner can create new grant types via UI:

```csharp
// Owner creates custom grant
var customGrant = new GrantType
{
    Key = "ManageSpecialProject",
    NameKey = "Grant_ManageSpecialProject",
    Category = GrantCategory.Custom,
    DefaultScope = GrantScopeLevel.Molecule,
    IsSystem = false,
    CreatedByUserId = ownerId
};
```

---

## 6. Shift System

### 6.1 Shift Models by JobType

| JobType | Model | Who Assigns | Eligibility |
|---------|-------|-------------|-------------|
| **Alhut** | Company groupings | Alhut Lead, Director, Admin | JobType=Alhut |
| **Text** | Company groupings | Text Lead, Director, Admin | JobType=Text |
| **BR** | Molecule-wide (3 per shift) | BR Director, Molecule Admin | JobType=BR |
| **Hakam** | Area-wide (1+1 backup) | Snir grant holders | JobType=Hakam |

### 6.2 Default Shift Windows

| Shift | Start | End |
|-------|-------|-----|
| Morning | 08:00 | 16:00 |
| Afternoon | 16:00 | 00:00 |
| Night | 00:00 | 08:00 |

Custom shift windows can be added by JobType Lead for their JobType in their company.

### 6.3 Tech Shifts (Shikma)

| Shift Type | Eligibility |
|------------|-------------|
| Hanava | Pie + Tao departments |
| Delta | Pie + Tao + Samapkam departments |
| Yekev | Yekev department |
| Moviltech | Department heads only |

### 6.4 Blueprint Schema

```csharp
public class ShiftBlueprint
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }        // null for tech shifts
    public int? ShiftGroupingId { get; set; }  // null for BR/Hakam
    public string? TechShiftType { get; set; } // "Hanava", "Delta", etc. for tech

    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public JobType? JobType { get; set; }
    public ShiftGrouping? ShiftGrouping { get; set; }
}
```

---

## 7. Duty System

### 7.1 Two Duty Types

| Duty | Scope | Managed By | Eligibility |
|------|-------|------------|-------------|
| **Hakam On-Call** | Area-wide | Snir (ManageHakamShifts grant) | Hakam users |
| **Katzin On-Call** | Area-wide | Director-level+, Area Admin+ | Director-level users |

### 7.2 Katzin On-Call

- Every night (including weekends)
- 1 person minimum, can be more
- Manual assignment with option for programs
- All director-level users are eligible

### 7.3 Duty Schema

```csharp
public class DutyType
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Key { get; set; } = string.Empty;  // "HakamOnCall", "KatzinOnCall"
    public string NameKey { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class DutyAssignment
{
    public int Id { get; set; }
    public int DutyTypeId { get; set; }
    public int UserId { get; set; }
    public int? BackupUserId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    // Audit
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DutyProgram
{
    public int Id { get; set; }
    public int AreaId { get; set; }  // Area-scoped for both Hakam and Katzin
    public int DutyTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DutyProgramFrequency Frequency { get; set; }
    public bool IsActive { get; set; } = true;

    public List<DutyProgramItem> Items { get; set; } = new();
}
```

---

## 8. Chore System

### 8.1 Chore Scope

- **Molecule-wide**: One Excel per molecule, organized by JobType
- **Assigned by**: Assigner (2 per molecule) OR Leads+ OR Molecule Admin+
- **No programs**: Manual assignment only

### 8.2 Who Can Assign

| Role | Can Assign Chores |
|------|-------------------|
| Assigner | ✅ Anyone in molecule |
| Alhut Lead | ✅ Anyone in molecule |
| Text Lead | ✅ Anyone in molecule |
| BR Director | ✅ Anyone in molecule |
| Director | ✅ Anyone in molecule |
| Molecule Admin | ✅ Anyone in molecule |

### 8.3 Chore Schema

```csharp
public class ChoreType
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class ChoreAssignment
{
    public int Id { get; set; }
    public int ChoreTypeId { get; set; }
    public int MoleculeId { get; set; }
    public int? AssignedToUserId { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }

    // Audit
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

## 9. Vacation System

### 9.1 Vacation Approval Matrix

| Employee JobType | Approved By |
|------------------|-------------|
| Alhut | Alhut Lead (same company) OR Alhut Director OR higher |
| Text | Text Lead (same company) OR Text Director OR higher |
| BR | BR Director (same company) OR higher |
| Hakam | BR Director (company boss) OR higher |

### 9.2 Visibility

- **Own vacations**: Always visible to self
- **Company vacations**: Everyone in company can see all company vacations
- **Cross-molecule**: Via Circle/Friends only

### 9.3 Vacation Schema

```csharp
public class VacationRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public VacationStatus Status { get; set; }
    public string? Reason { get; set; }

    // Approval
    public int? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalNotes { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; }
}

public enum VacationStatus
{
    Pending,
    Approved,
    Rejected,
    Canceled
}
```

---

## 10. Circle/Friends System

### 10.1 Purpose

Allow cross-molecule visibility for connected users.

### 10.2 Rules

| Aspect | Behavior |
|--------|----------|
| Relationship type | Mutual (both must accept) |
| What friends can see | Shifts (friend's only), Vacations, Chores, Profile |
| Max friends | No limit |
| Admin management | Admins can manage on behalf of users |

### 10.3 Schema

```csharp
public class UserFriendship
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FriendId { get; set; }
    public FriendshipStatus Status { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}

public enum FriendshipStatus
{
    Pending,
    Accepted,
    Rejected
}
```

---

## 11. Settings System

### 11.1 Settings Hierarchy

Settings can be defined at multiple levels with lower levels overriding higher:

| Level | Example Settings |
|-------|------------------|
| **Area** | Default rest hours, weekly cap |
| **Molecule** | Override area defaults |
| **Company** | Override molecule defaults |

### 11.2 Schema

```csharp
public class AreaSettings
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int DefaultRestHours { get; set; } = 11;
    public int DefaultWeeklyCap { get; set; } = 60;
}

public class MoleculeSettings
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }
}

public class CompanySettings
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }
}
```

---

## 12. Database Schema

### 12.1 Entity Relationship Summary

```
Project (1) ──< Area (n)
Area (1) ──< Molecule (n)
Area (1) ──< JobType (n)         // JobTypes are Area-scoped

Molecule (1) ──< Company (n)     // Workforce molecules
Molecule (1) ──< Department (n)  // Tech molecules
Molecule (1) ──< ShiftGrouping (n)
Molecule (1) ──< ChoreType (n)

Company (1) ──< User (n)
Department (1) ──< User (n)

User (n) ──< UserRole (n)
User (n) ──< Grant (n)
User (n) ──< UserFriendship (n)

User (1) ── JobType (FK)         // Each user has one JobType (workforce)

ShiftGrouping (n) ──< ShiftGroupingCompany (n)
ShiftGrouping (n) ──< ShiftGroupingJobType (n)

RoleTemplate (1) ──< RoleTemplateGrant (n)
RoleTemplate (1) ──< UserRole (n)

GrantType (1) ──< Grant (n)
GrantType (1) ──< RoleTemplateGrant (n)
```

### 12.2 Core Entities

```csharp
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public List<Area> Areas { get; set; } = new();
}

public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Project Project { get; set; } = null!;
    public List<Molecule> Molecules { get; set; } = new();
    public List<JobType> JobTypes { get; set; } = new();
    public AreaSettings Settings { get; set; } = null!;
}

public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MoleculeType Type { get; set; }
    public bool IsActive { get; set; } = true;

    public Area Area { get; set; } = null!;
    public List<Company> Companies { get; set; } = new();       // Workforce
    public List<Department> Departments { get; set; } = new();  // Tech
    public List<ShiftGrouping> ShiftGroupings { get; set; } = new();
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public MoleculeSettings? Settings { get; set; }
}

public enum MoleculeType
{
    Workforce,
    Tech,
    Helper
}

public class Company
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Molecule Molecule { get; set; } = null!;
    public List<User> Users { get; set; } = new();
    public CompanySettings? Settings { get; set; }
}

public class Department
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Molecule Molecule { get; set; } = null!;
    public List<User> Users { get; set; } = new();
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Organizational (one of these is set)
    public int? CompanyId { get; set; }     // Workforce molecules
    public int? DepartmentId { get; set; }  // Tech molecules

    // JobType (workforce only)
    public int? JobTypeId { get; set; }

    // Rank
    public MilitaryRank Rank { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public Department? Department { get; set; }
    public JobType? JobType { get; set; }
    public List<UserRole> Roles { get; set; } = new();
    public List<Grant> Grants { get; set; } = new();
}
```

---

## 13. Seed Data

### 13.1 Bootstrap Sequence

```csharp
// 1. Create Project
var shifty = new Project { Name = "Shifty", DisplayName = "שיפטי" };

// 2. Create Area
var area190 = new Area { ProjectId = shifty.Id, Name = "190", DisplayName = "190" };

// 3. Create JobTypes (Area-scoped)
var alhut = new JobType { AreaId = area190.Id, Name = "Alhut", DisplayName = "אלחוט" };
var br = new JobType { AreaId = area190.Id, Name = "BR", DisplayName = "ב\"ר" };
var text = new JobType { AreaId = area190.Id, Name = "Text", DisplayName = "טקסט" };
var hakam = new JobType { AreaId = area190.Id, Name = "Hakam", DisplayName = "חק\"ם" };

// 4. Create Workforce Molecules + Companies
var oren = new Molecule { AreaId = area190.Id, Name = "Oren", DisplayName = "אורן", Type = MoleculeType.Workforce };
var tzafona = new Company { MoleculeId = oren.Id, Name = "Tzafona", DisplayName = "צפונה" };
var hir = new Company { MoleculeId = oren.Id, Name = "Hir", DisplayName = "חיר" };
// ... more companies

// 5. Create Tech Molecule + Departments
var shikma = new Molecule { AreaId = area190.Id, Name = "Shikma", DisplayName = "שקמה", Type = MoleculeType.Tech };
var pie = new Department { MoleculeId = shikma.Id, Name = "Pie", DisplayName = "פאי" };
var tao = new Department { MoleculeId = shikma.Id, Name = "Tao", DisplayName = "טאו" };
// ... more departments

// 6. Create Helper Molecules
var shiklut = new Molecule { AreaId = area190.Id, Name = "Shiklut", DisplayName = "שיקלוט", Type = MoleculeType.Helper };
var noc = new Molecule { AreaId = area190.Id, Name = "NOC", DisplayName = "נגדים", Type = MoleculeType.Helper };

// 7. Create Shift Groupings (Oren example)
var tzafon = new ShiftGrouping { MoleculeId = oren.Id, Name = "Tzafon", DisplayName = "צפון" };
// Add companies: Tzafona, City
// Add JobTypes: Alhut, Text

// 8. Create admin user with SystemAdmin grant
var admin = new User { Username = "admin@local", CompanyId = tzafona.Id, JobTypeId = br.Id };
var systemAdminGrant = new Grant
{
    UserId = admin.Id,
    GrantTypeId = systemAdminGrantType.Id,
    ProjectId = shifty.Id,
    CanOwn = true,
    CanGive = true,
    Notes = "Bootstrap admin"
};
```

---

## 14. Migration Strategy

### 14.1 Migration Phases

**Phase 1: Schema Creation**
1. Create new tables (Area, Molecule, Department, ShiftGrouping, etc.)
2. Create GrantType table with 107 built-in grants
3. Create RoleTemplate table with 11 role templates
4. Create RoleTemplateGrant table with auto-grant mappings
5. Create UserRole, Grant tables

**Phase 2: Data Migration**
1. Map existing Companies to new structure
2. Assign JobTypes to workforce users
3. Create Molecules and assign Companies
4. Create initial grants based on existing roles

**Phase 3: UI Updates**
1. Update navigation for new hierarchy
2. Add grant management UI
3. Add role assignment UI
4. Update calendar views for JobType filtering
5. Add shift grouping configuration
6. Add Circle/Friends UI

**Phase 4: Testing**
1. Test all 107 grants work correctly
2. Test role auto-grants
3. Test shift assignment by JobType
4. Test cross-molecule visibility (Circle)
5. Test tech molecule shifts
6. Test vacation approval chain

---

## 15. Programs & Automatic Scheduling

### 15.1 ShiftProgram Schema

Programs automate shift scheduling by defining recurring patterns.

```csharp
public class ShiftProgram
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }           // null for tech shifts
    public int? ShiftGroupingId { get; set; }     // null for BR/Hakam/tech
    public string? TechShiftType { get; set; }    // "Hanava", "Delta", etc.

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public JobType? JobType { get; set; }
    public ShiftGrouping? ShiftGrouping { get; set; }
    public List<ShiftProgramItem> Items { get; set; } = new();
}

public class ShiftProgramItem
{
    public int Id { get; set; }
    public int ShiftProgramId { get; set; }
    public int ShiftBlueprintId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int Quantity { get; set; }  // How many shifts of this type on this day

    public ShiftProgram ShiftProgram { get; set; } = null!;
    public ShiftBlueprint ShiftBlueprint { get; set; } = null!;
}
```

### 15.2 MasterProgram Schema

MasterPrograms are templates for quickly creating programs.

```csharp
public class MasterProgram
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public JobType? JobType { get; set; }
    public List<MasterProgramItem> Items { get; set; } = new();
}

public class MasterProgramItem
{
    public int Id { get; set; }
    public int MasterProgramId { get; set; }
    public int ShiftBlueprintId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int DefaultQuantity { get; set; }

    public MasterProgram MasterProgram { get; set; } = null!;
    public ShiftBlueprint ShiftBlueprint { get; set; } = null!;
}
```

### 15.3 Program Generation

```csharp
// Generate shifts from program for a date range
public async Task GenerateShiftsFromProgramAsync(int programId, DateOnly startDate, DateOnly endDate)
{
    var program = await _db.ShiftPrograms
        .Include(p => p.Items)
        .ThenInclude(i => i.ShiftBlueprint)
        .FirstAsync(p => p.Id == programId);

    for (var date = startDate; date <= endDate; date = date.AddDays(1))
    {
        var dayItems = program.Items.Where(i => i.DayOfWeek == date.DayOfWeek);

        foreach (var item in dayItems)
        {
            for (int i = 0; i < item.Quantity; i++)
            {
                var shift = new ShiftInstance
                {
                    ShiftBlueprintId = item.ShiftBlueprintId,
                    Date = date,
                    StartTime = item.ShiftBlueprint.Start,
                    EndTime = item.ShiftBlueprint.End,
                    Status = ShiftStatus.Open
                };
                _db.ShiftInstances.Add(shift);
            }
        }
    }

    await _db.SaveChangesAsync();
}
```

---

## 16. Calendar System

### 16.1 Calendar Types and Scoping

| Calendar | Scope | Filters | Supports Programs |
|----------|-------|---------|-------------------|
| **Shift Calendar** | Molecule | JobType, ShiftGrouping | ✅ Yes |
| **Chore Calendar** | Molecule | None (all users) | ❌ No |
| **Duty Calendar** | Area | DutyType (Hakam, Katzin) | ✅ Yes |
| **Vacation Calendar** | Company + Circle | JobType (optional) | N/A |
| **Tech Shift Calendar** | Molecule | TechShiftType, Department | ✅ Yes |

### 16.2 Calendar Visibility Rules

#### Shift Calendar (Workforce)
```csharp
// Who sees what in shift calendar
public async Task<List<ShiftInstance>> GetVisibleShiftsAsync(int userId)
{
    var user = await GetUserWithContextAsync(userId);

    // User sees shifts for their JobType in their molecule
    var shifts = await _db.ShiftInstances
        .Include(s => s.ShiftBlueprint)
        .Where(s => s.ShiftBlueprint.MoleculeId == user.Company.MoleculeId)
        .Where(s => s.ShiftBlueprint.JobTypeId == user.JobTypeId)
        .ToListAsync();

    // Plus shifts of friends (from Circle)
    var friendShifts = await GetFriendShiftsAsync(userId);

    return shifts.Concat(friendShifts).ToList();
}
```

#### Chore Calendar
```csharp
// Everyone in molecule sees all chores
public async Task<List<ChoreAssignment>> GetVisibleChoresAsync(int userId)
{
    var user = await GetUserWithContextAsync(userId);

    return await _db.ChoreAssignments
        .Where(c => c.MoleculeId == user.Company.MoleculeId)
        .ToListAsync();
}
```

#### Vacation Calendar
```csharp
// Company vacations + friend vacations
public async Task<List<VacationRequest>> GetVisibleVacationsAsync(int userId)
{
    var user = await GetUserWithContextAsync(userId);

    // All vacations in same company
    var companyVacations = await _db.VacationRequests
        .Where(v => v.User.CompanyId == user.CompanyId)
        .Where(v => v.Status == VacationStatus.Approved)
        .ToListAsync();

    // Plus friend vacations
    var friendVacations = await GetFriendVacationsAsync(userId);

    return companyVacations.Concat(friendVacations).ToList();
}
```

### 16.3 ShiftInstance Schema

```csharp
public class ShiftInstance
{
    public int Id { get; set; }
    public int ShiftBlueprintId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // Assignment
    public int? AssignedToUserId { get; set; }
    public ShiftStatus Status { get; set; }

    // Audit
    public int? AssignedByUserId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public ShiftBlueprint ShiftBlueprint { get; set; } = null!;
    public User? AssignedToUser { get; set; }
}

public enum ShiftStatus
{
    Open,       // Not assigned
    Assigned,   // Assigned to user
    Completed,  // Shift completed
    Canceled    // Shift canceled
}
```

---

## 17. Smart Task System

### 17.1 Purpose

Onboarding tasks guide administrators through setup when new entities are created.

### 17.2 Task Types

| Task Type | Triggered When | Assigned To | Action |
|-----------|---------------|-------------|--------|
| `AssignMoleculeAdmin` | New molecule created | Area Admin | Assign Molecule Admin role |
| `AssignAlhutDirector` | New workforce molecule | Molecule Admin | Assign Alhut Director |
| `AssignTextDirector` | New workforce molecule | Molecule Admin | Assign Text Director |
| `AssignBRDirector` | New company created | Molecule Admin | Assign BR Director |
| `AssignAlhutLead` | New company created | Alhut Director | Assign Alhut Lead |
| `AssignTextLead` | New company created | Text Director | Assign Text Lead |
| `AssignAssigners` | New molecule created | Molecule Admin | Assign 2 Assigners |
| `SetupShiftGroupings` | New workforce molecule | Molecule Admin | Configure shift groupings |
| `SetupDutyPrograms` | New molecule created | Molecule Admin | Configure duty rotation |
| `SetupShiftBlueprints` | New molecule created | Directors | Create shift templates |

### 17.3 Task Schema

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
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }
}

public enum SetupTaskType
{
    AssignMoleculeAdmin,
    AssignAlhutDirector,
    AssignTextDirector,
    AssignBRDirector,
    AssignAlhutLead,
    AssignTextLead,
    AssignAssigners,
    SetupShiftGroupings,
    SetupDutyPrograms,
    SetupShiftBlueprints
}

public enum SetupTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped
}
```

### 17.4 Task Generation

```csharp
// When new molecule is created, generate setup tasks
public async Task GenerateSetupTasksForMoleculeAsync(Molecule molecule)
{
    var tasks = new List<SetupTask>
    {
        new SetupTask
        {
            Type = SetupTaskType.AssignMoleculeAdmin,
            Title = $"Assign Molecule Admin for {molecule.DisplayName}",
            MoleculeId = molecule.Id,
            AssignedToUserId = areaAdminId,
            Status = SetupTaskStatus.Pending
        }
    };

    if (molecule.Type == MoleculeType.Workforce)
    {
        tasks.Add(new SetupTask
        {
            Type = SetupTaskType.AssignAlhutDirector,
            Title = $"Assign Alhut Director for {molecule.DisplayName}",
            MoleculeId = molecule.Id,
            Status = SetupTaskStatus.Pending
        });
        // ... more tasks
    }

    _db.SetupTasks.AddRange(tasks);
    await _db.SaveChangesAsync();
}
```

---

## 18. Admin UI Organization

### 18.1 Sidebar Navigation Structure

```
📅 My Shifty
  ├─ 🏠 Home
  ├─ 📅 My Schedule
  ├─ 📝 My Requests
  └─ 👥 My Friends (Circle)

📊 Calendars
  ├─ 📅 Shift Calendar
  ├─ 🧹 Chore Calendar
  ├─ 🎖️ Duty Calendar
  └─ 🏖️ Vacation Calendar

📊 Analytics (if ViewAnalytics grant)
  ├─ 📊 Dashboard
  ├─ ⚖️ Fairness Stats
  └─ 📈 Reports

👥 People & Structure (if ViewUsers grant)
  ├─ 🏗️ Organization Structure
  ├─ 👥 Users
  ├─ 💼 JobTypes
  └─ 🎖️ Grants

📅 Scheduling (if ManageBlueprints grant)
  ├─ 📘 Blueprints
  ├─ 📅 Programs
  ├─ 🎯 Master Programs
  └─ 📊 Shift Groupings

⚙️ Settings (if EditSettings grant)
  ├─ 🏢 Company Settings
  ├─ 🔬 Molecule Settings
  └─ 🌍 Area Settings

🔧 Administration (if Molecule Admin+)
  ├─ ✅ Setup Tasks
  ├─ 👥 Role Assignments
  └─ 🔍 Audit Log

🌐 System (if SystemAdmin)
  ├─ 📧 Email Configuration
  ├─ 🔐 ADFS Configuration
  ├─ 🎮 Game Configuration
  ├─ 🚩 Feature Flags
  ├─ 💾 Database Console
  └─ 🏥 System Health
```

### 18.2 Owner Context Selector

Owners (SystemAdmin) see a context selector in the top navigation:

```html
<nav class="top-navbar">
    <div class="navbar-brand">📊 ShiftManager</div>

    @if (HasSystemAdmin)
    {
        <div class="owner-context">
            <span class="context-label">Viewing:</span>
            <select class="context-selector" onchange="selectContext(this.value)">
                <option value="global">🌐 Global (All)</option>
                <optgroup label="Area: 190">
                    <optgroup label="Molecule: Oren">
                        <option value="company:1">Tzafona</option>
                        <option value="company:2">Hir</option>
                        <option value="company:3">Camps</option>
                    </optgroup>
                    <optgroup label="Molecule: Shikma">
                        <option value="dept:1">Pie</option>
                        <option value="dept:2">Tao</option>
                    </optgroup>
                </optgroup>
            </select>

            @if (CurrentContext == "global")
            {
                <span class="context-badge global">Global Mode</span>
            }
            else
            {
                <span class="context-badge">@CurrentContextName</span>
            }
        </div>
    }

    <div class="navbar-user"><!-- user menu --></div>
</nav>
```

### 18.3 Context Behavior

| Context | What User Sees | Can Access Global Settings |
|---------|---------------|---------------------------|
| **Global** | All data, all molecules | ✅ Yes |
| **Molecule** | Data for that molecule | ✅ Yes (Owner only) |
| **Company** | Data for that company | ✅ Yes (Owner only) |

Non-owners see only their company's data (no context selector).

---

## 19. Global Configurations

### 19.1 Email Configuration

```csharp
public class EmailConfig
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string? EncryptedApiKey { get; set; }
    public string? ApiUrl { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }

    public DateTime UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
}

// Email templates are per-molecule (can have different templates per molecule)
public class EmailTemplate
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;  // "ShiftAssigned", "VacationApproved"
    public string SubjectEn { get; set; } = string.Empty;
    public string SubjectHe { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;
    public string BodyHe { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
```

### 19.2 ADFS (Griffin) Configuration

```csharp
public class GriffinConfig
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? TokenConsumerUrl { get; set; }
    public string? ClientId { get; set; }
    public string? EncryptedClientSecret { get; set; }
    public bool AutoProvisionUsers { get; set; }
    public int? DefaultMoleculeId { get; set; }  // Where to put auto-provisioned users
    public int? DefaultJobTypeId { get; set; }

    public DateTime UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
}
```

### 19.3 Game Configuration

```csharp
public class GameConfig
{
    public int Id { get; set; }
    public bool Enabled { get; set; }

    // Grid settings
    public int GridSize { get; set; } = 6;
    public int MinMatchLength { get; set; } = 3;

    // Points
    public int PointsPer3Match { get; set; } = 40;
    public int PointsPer4Match { get; set; } = 60;
    public int PointsPer5Match { get; set; } = 100;

    // Timing
    public int GameDurationSeconds { get; set; } = 60;
    public int CooldownMinutes { get; set; } = 30;

    public DateTime UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
}
```

### 19.4 Feature Flags

```csharp
public class FeatureFlag
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int? MoleculeId { get; set; }  // null = global, set = molecule-specific

    public DateTime UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
}

// Example flags
// "CircleEnabled" - Enable/disable friend system
// "GameEnabled" - Enable/disable shift game
// "DutyProgramsEnabled" - Enable/disable automatic duty scheduling
// "SwapRequestsEnabled" - Enable/disable shift swaps
```

---

## 20. User Authentication & Claims

### 20.1 Claims Structure

```csharp
// Claims stored in JWT/cookie at login
public class UserClaims
{
    public int UserId { get; set; }
    public int? CompanyId { get; set; }      // null for tech users
    public int? DepartmentId { get; set; }   // null for workforce users
    public int MoleculeId { get; set; }      // Derived from Company/Department
    public int AreaId { get; set; }          // Derived
    public int ProjectId { get; set; }       // Derived
    public int? JobTypeId { get; set; }      // null for tech users
    public string Username { get; set; } = string.Empty;
    public MilitaryRank Rank { get; set; }
}

// Set at login
public async Task<ClaimsPrincipal> CreateClaimsPrincipalAsync(User user)
{
    var molecule = user.CompanyId.HasValue
        ? user.Company.Molecule
        : user.Department.Molecule;

    var claims = new List<Claim>
    {
        new Claim("UserId", user.Id.ToString()),
        new Claim("CompanyId", user.CompanyId?.ToString() ?? ""),
        new Claim("DepartmentId", user.DepartmentId?.ToString() ?? ""),
        new Claim("MoleculeId", molecule.Id.ToString()),
        new Claim("AreaId", molecule.AreaId.ToString()),
        new Claim("ProjectId", molecule.Area.ProjectId.ToString()),
        new Claim("JobTypeId", user.JobTypeId?.ToString() ?? ""),
        new Claim("Username", user.Username),
        new Claim("Rank", ((int)user.Rank).ToString())
    };

    var identity = new ClaimsIdentity(claims, "ShiftManager");
    return new ClaimsPrincipal(identity);
}
```

### 20.2 Current User Service

```csharp
public class CurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public int UserId => int.Parse(GetClaim("UserId"));
    public int? CompanyId => GetNullableInt("CompanyId");
    public int? DepartmentId => GetNullableInt("DepartmentId");
    public int MoleculeId => int.Parse(GetClaim("MoleculeId"));
    public int AreaId => int.Parse(GetClaim("AreaId"));
    public int ProjectId => int.Parse(GetClaim("ProjectId"));
    public int? JobTypeId => GetNullableInt("JobTypeId");
    public MilitaryRank Rank => (MilitaryRank)int.Parse(GetClaim("Rank"));

    public bool IsOfficer => (int)Rank >= 9;
    public bool IsWorkforce => CompanyId.HasValue;
    public bool IsTech => DepartmentId.HasValue;
}
```

---

## 21. Auto-Grant Rules

### 21.1 Implicit Grants

When a grant is given, related grants are automatically created:

```csharp
public static readonly Dictionary<string, string[]> AutoGrantRules = new()
{
    // Assign grants auto-give View grants
    { "AssignAlhutShifts", new[] { "ViewAlhutShiftCalendar" } },
    { "AssignTextShifts", new[] { "ViewTextShiftCalendar" } },
    { "AssignBRShifts", new[] { "ViewBRShiftCalendar" } },
    { "AssignHakamShifts", new[] { "ViewHakamShiftCalendar" } },
    { "AssignChores", new[] { "ViewChoreCalendar" } },
    { "AssignKatzinOnCall", new[] { "ViewKatzinCalendar" } },

    // Tech shifts
    { "AssignHanavaShifts", new[] { "ViewHanavaCalendar" } },
    { "AssignDeltaShifts", new[] { "ViewDeltaCalendar" } },
    { "AssignYekevShifts", new[] { "ViewYekevCalendar" } },
    { "AssignMoviltechShifts", new[] { "ViewMoviltechCalendar" } },

    // Vacation
    { "ApproveVacations", new[] { "ViewCompanyVacations" } },

    // User management
    { "EditUsers", new[] { "ViewCompanyUsers" } },
    { "CreateUsers", new[] { "ViewCompanyUsers" } },
    { "ManageGrants", new[] { "ViewGrants", "ViewCompanyUsers" } },

    // Settings
    { "EditCompanySettings", new[] { "ViewSettings" } },
    { "EditMoleculeSettings", new[] { "ViewSettings" } },
    { "EditAreaSettings", new[] { "ViewSettings" } },
};
```

### 21.2 Auto-Grant Service

```csharp
public class AutoGrantService
{
    public async Task CreateGrantWithAutoGrantsAsync(Grant primaryGrant, int grantedByUserId)
    {
        // Add primary grant
        primaryGrant.GrantedByUserId = grantedByUserId;
        primaryGrant.GrantedAt = DateTime.UtcNow;
        _db.Grants.Add(primaryGrant);

        // Check for auto-grants
        var grantKey = await _db.GrantTypes
            .Where(gt => gt.Id == primaryGrant.GrantTypeId)
            .Select(gt => gt.Key)
            .FirstAsync();

        if (AutoGrantRules.TryGetValue(grantKey, out var autoGrantKeys))
        {
            foreach (var autoKey in autoGrantKeys)
            {
                var autoGrantType = await _db.GrantTypes.FirstAsync(gt => gt.Key == autoKey);

                // Check if user already has this grant
                var exists = await _db.Grants.AnyAsync(g =>
                    g.UserId == primaryGrant.UserId &&
                    g.GrantTypeId == autoGrantType.Id);

                if (!exists)
                {
                    var autoGrant = new Grant
                    {
                        UserId = primaryGrant.UserId,
                        GrantTypeId = autoGrantType.Id,
                        ProjectId = primaryGrant.ProjectId,
                        AreaId = primaryGrant.AreaId,
                        MoleculeId = primaryGrant.MoleculeId,
                        CompanyId = primaryGrant.CompanyId,
                        DepartmentId = primaryGrant.DepartmentId,
                        JobTypeId = primaryGrant.JobTypeId,
                        CanOwn = true,
                        CanGive = false,  // Auto-grants cannot delegate
                        IsAutoGrant = true,
                        GrantedByUserId = grantedByUserId,
                        GrantedAt = DateTime.UtcNow,
                        Notes = $"Auto-granted from {grantKey}"
                    };
                    _db.Grants.Add(autoGrant);
                }
            }
        }

        await _db.SaveChangesAsync();
    }
}
```

---

## 22. Cascade Deletion Rules

### 22.1 Deletion Behaviors

| Parent Entity | Child Entity | On Delete |
|---------------|--------------|-----------|
| **ShiftBlueprint** | ShiftInstance | CASCADE (delete instances) |
| **ShiftBlueprint** | ShiftProgramItem | CASCADE (remove from programs) |
| **ShiftProgram** | ShiftProgramItem | CASCADE |
| **Company** | User | RESTRICT (must reassign users first) |
| **Department** | User | RESTRICT |
| **Molecule** | Company | RESTRICT |
| **Molecule** | Department | RESTRICT |
| **Area** | Molecule | RESTRICT |
| **User** | Grant | CASCADE (delete grants) |
| **User** | UserRole | CASCADE (delete roles) |
| **User** | ShiftInstance (assigned) | SET NULL |
| **User** | VacationRequest | CASCADE |
| **User** | UserFriendship | CASCADE |

### 22.2 EF Core Configuration

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // CASCADE: Deleting Blueprint deletes all ShiftInstances
    modelBuilder.Entity<ShiftInstance>()
        .HasOne(si => si.ShiftBlueprint)
        .WithMany(sb => sb.ShiftInstances)
        .HasForeignKey(si => si.ShiftBlueprintId)
        .OnDelete(DeleteBehavior.Cascade);

    // CASCADE: Deleting Blueprint removes from Programs
    modelBuilder.Entity<ShiftProgramItem>()
        .HasOne(spi => spi.ShiftBlueprint)
        .WithMany()
        .HasForeignKey(spi => spi.ShiftBlueprintId)
        .OnDelete(DeleteBehavior.Cascade);

    // RESTRICT: Cannot delete Company with Users
    modelBuilder.Entity<User>()
        .HasOne(u => u.Company)
        .WithMany(c => c.Users)
        .HasForeignKey(u => u.CompanyId)
        .OnDelete(DeleteBehavior.Restrict);

    // SET NULL: Deleting User unassigns shifts
    modelBuilder.Entity<ShiftInstance>()
        .HasOne(si => si.AssignedToUser)
        .WithMany()
        .HasForeignKey(si => si.AssignedToUserId)
        .OnDelete(DeleteBehavior.SetNull);
}
```

### 22.3 Deletion Confirmation UI

```csharp
public async Task<IActionResult> OnPostDeleteBlueprintAsync(int id, bool confirmed)
{
    if (!confirmed)
    {
        var instanceCount = await _db.ShiftInstances.CountAsync(si => si.ShiftBlueprintId == id);
        var programCount = await _db.ShiftProgramItems.CountAsync(spi => spi.ShiftBlueprintId == id);

        return new JsonResult(new
        {
            requiresConfirmation = true,
            message = $"This will DELETE {instanceCount} shift assignments and remove from {programCount} programs.",
            instanceCount,
            programCount
        });
    }

    var blueprint = await _db.ShiftBlueprints.FindAsync(id);
    _db.ShiftBlueprints.Remove(blueprint);
    await _db.SaveChangesAsync();

    return RedirectToPage();
}
```

---

## 23. Military Rank System

### 23.1 Rank Enum

```csharp
public enum MilitaryRank
{
    // Enlisted (0-8)
    Turai = 0,              // טוראי
    TuraiRishon = 1,        // טוראי ראשון
    Rav_Turai = 2,          // רב טוראי
    Samal = 3,              // סמל
    Samal_Rishon = 4,       // סמל ראשון
    Rav_Samal = 5,          // רב סמל
    Rav_Samal_Mitkadam = 6, // רב סמל מתקדם
    Rav_Samal_Bakhir = 7,   // רב סמל בכיר
    Rav_Nagad = 8,          // רב נגד

    // Officers (9+)
    Segen_Mishne = 9,       // סגן משנה
    Segen = 10,             // סגן
    Seren = 11,             // סרן
    Rav_Seren = 12,         // רב סרן
    Sgan_Aluf = 13,         // סגן אלוף
    Aluf_Mishne = 14,       // אלוף משנה
    Tat_Aluf = 15,          // תת אלוף
    Aluf = 16,              // אלוף
    RavAluf = 17            // רב אלוף
}
```

### 23.2 Rank Helper Methods

```csharp
public static class RankExtensions
{
    public static bool IsOfficer(this MilitaryRank rank) => (int)rank >= 9;

    public static bool IsNCO(this MilitaryRank rank) => (int)rank >= 3 && (int)rank <= 8;

    public static bool IsEnlisted(this MilitaryRank rank) => (int)rank < 3;

    public static string GetDisplayName(this MilitaryRank rank, string language = "he")
    {
        // Return Hebrew or English display name
        return language == "he" ? GetHebrewName(rank) : rank.ToString();
    }

    public static string GetAbbreviation(this MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "טר'",
            MilitaryRank.Samal => "סמל",
            MilitaryRank.Segen => "סג'",
            MilitaryRank.Seren => "סר'",
            // ... etc
        };
    }
}
```

### 23.3 Rank-Based Eligibility

```csharp
// Katzin on-call requires officer rank
public bool CanBeKatzinOnCall(User user)
{
    return user.Rank.IsOfficer();
}

// Some duties require specific rank
public bool IsEligibleForDuty(User user, DutyType dutyType)
{
    if (dutyType.RequiresOfficerRank && !user.Rank.IsOfficer())
        return false;

    if (dutyType.MinimumRank.HasValue && (int)user.Rank < dutyType.MinimumRank)
        return false;

    return true;
}
```

---

## End of Document

**Status:** ✅ FINALIZED - Ready for Implementation
**Version:** 3.0
**Date:** 2026-01-25

**Key Changes from v2.0:**
- ✅ Hierarchy corrected: JobType is USER attribute, not level
- ✅ Three molecule types: Workforce, Tech, Helper
- ✅ Shift groupings for Alhut/Text company combinations
- ✅ BR shifts molecule-wide, Hakam shifts area-wide
- ✅ 107 grants (was 26)
- ✅ 11 role templates with configurable auto-grants
- ✅ Circle/Friends for cross-molecule visibility
- ✅ Dynamic grant creation by Owner
- ✅ Tech molecule specific shifts (Hanava, Delta, Yekev, Moviltech)
- ✅ Katzin on-call duty (area-wide, director-level)
- ✅ Programs & MasterPrograms for automatic scheduling
- ✅ Calendar scoping rules
- ✅ Smart Task System for onboarding
- ✅ Admin UI organization
- ✅ Global configurations (Email, ADFS, Game)
- ✅ User claims structure
- ✅ Auto-grant rules
- ✅ Cascade deletion rules
- ✅ Military rank system (18 ranks)
