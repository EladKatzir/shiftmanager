# ShiftManager: Organizational Hierarchy System
**Shareholder Presentation Document**

---

## Executive Summary

ShiftManager is an operational scheduling and workforce management system designed for military units with complex multi-level organizational structures. This document explains how the system manages three types of work assignments (Shifts, Duties, Chores) across a 6-level organizational hierarchy (Project → Area → Molecule → Department → Company → Individual).

---

## 1. What Problems Does This Solve?

### Current Pain Points:
1. **No organizational structure** - Everything is flat, making it impossible to manage cross-company coordination
2. **Confused permission system** - Simple Owner/Manager/Director/Employee roles don't match real-world authority patterns
3. **Mixed responsibility types** - "On-Duty" mixes leadership responsibilities with coverage duties
4. **Manual scheduling** - No automatic rotation for recurring assignments
5. **No job differentiation** - Can't schedule differently for BR (ב"ר) vs Producer (אלחוטן) vs Hakam (חק"מ)

### What We're Building:
A military-grade scheduling system that:
- Reflects actual organizational hierarchy (Project → Area → Molecule → Department → Company)
- Separates three distinct types of assignments (Shifts, Duties, Chores)
- Enables job-specific scheduling (BR schedules differently than Hakam)
- Provides automatic rotation for recurring assignments
- Uses explicit permission grants instead of rigid roles

---

## 2. The Organizational Hierarchy

### Real-World Structure (using your actual example):

```
📦 Shifty (שיפטי) - PROJECT
   └─ 🏢 190 - AREA
       └─ 🔷 Oren (אורן) - MOLECULE
           └─ 🏛️ Defence and Manuver (הגנה ותמרון) - DEPARTMENT
               ├─ 🏢 Radio (טקטי) - COMPANY
               │   └─ 👤 Soldier A (BR)
               │   └─ 👤 Soldier B (Producer)
               │
               ├─ 🏢 North (צפון) - COMPANY
               │   └─ 👤 Soldier C (BR)
               │   └─ 👤 Soldier D (Hakam)
               │
               ├─ 🏢 City (העיר) - COMPANY
               └─ 🏢 Hir (ח'י"ר) - COMPANY
```

### What Each Level Means:

| Level | What It Is | Who Manages It | Example |
|-------|------------|----------------|---------|
| **PROJECT** | Top-level organization | Owner (SystemAdmin) | Shifty (שיפטי) |
| **AREA** | Major operational division | Area Admin | 190 |
| **MOLECULE** | Operational coordination boundary | Molecule Admin | Oren (אורן) |
| **DEPARTMENT** | Policy & planning unit | Department Job Directors | Defence and Manuver (הגנה ותמרון) |
| **COMPANY** | Team/unit with shared schedule | Company Job Leads | Radio, North, City, Hir |
| **USER** | Individual soldier | Employee | Individual person with rank & job |

---

## 3. Three Types of Work Assignments

### 🔷 **SHIFTS** - Regular scheduled work periods

**What:** Recurring time blocks where soldiers perform their primary job function (BR, Producer, Hakam).

**Examples:**
- Morning Shift (08:00-16:00) for BR at North Company
- Afternoon Shift (16:00-00:00) for Producer at Radio Company
- Night Shift (00:00-08:00) for BR at City Company

**Scope:** Department-wide (all companies in "Defence" department share the same shift definitions)

**Who Schedules:** Department Job Director (e.g., BR Director) assigns shifts across all companies in the department

**Supports Automation:** YES - Uses Programs for recurring patterns (e.g., "3 BR soldiers every Monday morning")

**Real-World Use Case:**
> The BR Director for Defence Department creates a shift blueprint "Morning - North - BR" that runs 08:00-16:00. They then create a Program that says "Every weekday, assign 3 BR soldiers from North to this shift." The system automatically generates these assignments.

---

### 🎯 **DUTIES** - Leadership & on-call responsibilities

**What:** Special responsibilities that exist **in addition to** regular shifts. Two types:

#### Type 1: **Responsibility Duties** (All-Day)
Leadership roles that last the entire day, regardless of shift schedule.

**Examples:**
- **On-Duty Lead** (מוביל תורנות) - Officer-rank soldier responsible for entire operation for 24 hours
- Requires: Officer rank (rank 9+)
- Duration: All day (no specific time blocks)

#### Type 2: **Coverage Duties** (Time-Specific)
On-call availability for specific time windows.

**Examples:**
- **Hakam On-Call** (חק"מ בכוננות) - Hakam specialist available 08:00-20:00 for urgent calls
- Requires: Hakam JobType
- Duration: Specific time window (08:00-20:00)

**Scope:** Molecule-wide (e.g., all companies in Oren molecule share duty assignments)

**Who Schedules:** Molecule Admin assigns duties across entire molecule (crosses company boundaries)

**Supports Automation:** YES - Uses DutyPrograms for rotation (e.g., "Weekly rotation: Soldier A, Soldier B, Soldier C")

**Real-World Use Case:**
> The Molecule Admin creates a DutyProgram for "Hakam On-Call" with weekly rotation. Week 1: Soldier A is on-call, Week 2: Soldier B, Week 3: Soldier C, then cycles back. The system automatically generates these assignments.

**Key Difference from Shifts:**
- Shifts = primary work (everyone does them)
- Duties = additional responsibilities (only certain people, rotates)

---

### 🧹 **CHORES** - One-off maintenance tasks

**What:** Non-recurring tasks that need to be done once (cleaning, equipment checks, maintenance).

**Examples:**
- Equipment room cleaning on December 25th
- Vehicle maintenance on January 15th
- Inventory check on February 1st

**Scope:** Molecule-wide (all companies in the molecule share chore assignments)

**Who Schedules:** Company Job Lead or Molecule Admin assigns manually

**Supports Automation:** NO - Chores are always manually assigned (no recurring patterns)

**Real-World Use Case:**
> A Company Lead notices the equipment room needs cleaning. They create a chore "Clean equipment room" for next Monday and assign Soldier X to it. This is a one-time task that won't repeat automatically.

---

## 4. JobTypes: Different Roles Need Different Schedules

### What Are JobTypes?

JobTypes represent **functional roles** within the organization. Each soldier has:
- **Primary JobType** (their main role)
- **Secondary JobTypes** (additional qualifications)

### Real JobTypes in Your System:

| JobType | Hebrew | Description | Management Pattern |
|---------|--------|-------------|-------------------|
| **BR** | ב"ר | Base Role | Hierarchical (Director → Leads) |
| **Producer** | אלחוטן | Radio Operator | Hierarchical (Director → Leads) |
| **Hakam** | חק"מ | Specialist (on-call based) | Direct Department (no leads) |

### Management Patterns Explained:

#### **Hierarchical Pattern** (BR, Producer):
```
Department Job Director (BR Director)
   └─ Manages all BR shifts across department
   └─ Delegates to Company Job Leads
       └─ Company BR Lead (North)
       └─ Company BR Lead (Radio)
       └─ Company BR Lead (City)
```

#### **Direct Department Pattern** (Hakam):
```
Department Job Director (Hakam Director)
   └─ Manages all Hakam work directly
   └─ NO Company Leads (specialized role, managed centrally)
```

### Hat Switching:

Soldiers with multiple JobTypes can "switch hats" - the UI filters to show only relevant shifts for the active JobType.

**Example:**
> Soldier X is BR (primary) + Producer (secondary). When wearing the "BR hat," they only see BR shifts in the calendar. When they switch to "Producer hat," they see Producer shifts. Their **permissions stay the same** (hat switching is UI filter only).

---

## 5. Blueprints, Programs, and MasterPrograms

### 🔷 **Blueprints** (Shift Templates)

**What:** Reusable shift definitions that specify time, company, and job type.

**Example:**
```
Blueprint: "Morning - North Defence - Producer"
Hebrew:    "בוקר - הגנה צפון - אלחוטן"
Time:      08:00 - 16:00
Company:   North
JobType:   Producer
```

**Scope:** Department-wide (all blueprints belong to a department)

**Key Rule:** Every blueprint MUST specify both Company AND JobType (no generic blueprints)

---

### 📅 **Programs** (Recurring Shift Patterns)

**What:** Automatic scheduling rules that generate shift assignments based on blueprints.

**Example:**
```
Program: "North BR Weekday Coverage"
Rule:    Every Monday-Friday
         Assign 3 soldiers to "Morning - North - BR" blueprint
         Assign 2 soldiers to "Afternoon - North - BR" blueprint
```

**How It Works:**
1. Department Director creates blueprints
2. Department Director creates program using those blueprints
3. System automatically generates shift assignments based on program rules
4. Company Lead assigns specific soldiers to fill those shifts

---

### 🎯 **MasterPrograms** (Program Templates)

**What:** Templates for creating programs quickly (copy-paste for new companies).

**Example:**
```
MasterProgram: "Standard Weekday Coverage"
Contains:      3x Morning shifts
               2x Afternoon shifts
               1x Night shift

When applied:  Creates a complete program for a new company instantly
```

**Scope:** Department-wide (templates available across the department)

---

## 6. Permission System: Grants Instead of Roles

### The Old Problem:
Simple role enum (Owner/Manager/Director/Employee) couldn't handle:
- BR Lead at North needs Department-wide shift authority
- Same person needs Company-only vacation approval
- Same person needs Molecule-wide chore assignment

**Solution:** Grant-based permissions with **Own** and **Give** capabilities.

---

### 26 Permissions (+ SystemAdmin):

#### **Operational Actions:**
1. AssignShifts
2. AssignChores
3. AssignDuties
4. ApproveVacations
5. ApproveSwaps

#### **User Management:**
6. ViewUsers
7. CreateUsers
8. EditUsers
9. DeleteUsers
10. AssignJobTypes

#### **Configuration:**
11. ManageBlueprints
12. ManagePrograms
13. ManageMasterPrograms
14. EditSettings

#### **Viewing:**
15. ViewShiftCalendar
16. ViewChoreCalendar
17. ViewDutyCalendar
18. ViewVacationCalendar
19. ViewAnalytics

#### **Admin:**
20. ManageGrants
21. EditHierarchy

#### **Communication:**
22. ViewEmailTemplates
23. EditEmailTemplates
24. SendEmails

#### **System:**
25. SystemAdmin (covers all 26 permissions)

---

### Own vs Give:

| Capability | Meaning | Example |
|------------|---------|---------|
| **Own** | Can perform the action | Can assign shifts |
| **Give** | Can grant permission to others | Can promote someone to Company Lead |

**Real-World Example:**
```
Grant for BR Lead at North:
- Permission: AssignShifts
- Scope: DepartmentId=Defence, JobTypeId=BR
- CanOwn: ✅ (can assign shifts)
- CanGive: ✅ (can delegate to subordinates)
```

This single grant means:
- BR Lead can assign shifts for all BR soldiers across entire Defence department
- BR Lead can create new grants for others (e.g., make someone an assistant lead)

---

## 7. Settings System: Department Defaults + Company Overrides

### The Problem:
Different companies need flexibility while maintaining department-wide consistency.

### The Solution:

```
Defence Department Settings (defaults for all companies):
- Rest Hours: 11 hours
- Weekly Cap: 60 hours

↓ inherited by ↓

North Company (uses defaults):
- Rest Hours: 11 (uses department default)
- Weekly Cap: 60 (uses department default)

Radio Company (overrides):
- Rest Hours: 12 (override - more rest needed)
- Weekly Cap: 55 (override - reduced workload)
```

**Who Can Edit:**
- **Department Director:** Edits department defaults (affects all companies unless overridden)
- **Company Job Lead:** Edits company overrides (only affects their company)

---

## 8. Calendar Views

### 4 Calendar Types:

| Calendar | Shows | Scope | Supports Programs |
|----------|-------|-------|-------------------|
| **Shift Calendar** | Regular work shifts | Department + JobType | ✅ YES |
| **Chore Calendar** | One-off tasks | Molecule | ❌ NO (manual) |
| **Duty Calendar** | Leadership/on-call | Molecule | ✅ YES |
| **Vacation Calendar** | Approved time off | Company + JobType | N/A |

### Example: BR Lead at North Views Calendars:

**Shift Calendar (filtered: Defence Dept + BR JobType):**
```
Shows all BR shifts across North, Radio, City, Hir companies
Can assign soldiers to shifts
```

**Chore Calendar (filtered: Oren Molecule):**
```
Shows all chores for entire Oren molecule
Can assign any soldier to chores
```

**Duty Calendar (filtered: Oren Molecule):**
```
Shows all duty assignments (On-Duty Lead, Hakam On-Call)
Read-only (only Molecule Admin can assign)
```

**Vacation Calendar (filtered: North Company + BR):**
```
Shows vacation requests for North BR soldiers
Can approve/reject vacations
```

---

## 9. Real-World Workflow Examples

### Example 1: Creating a New Shift Schedule

**Actors:**
- Department Job Director (BR Director)
- Company Job Lead (North BR Lead)
- Employees (BR soldiers at North)

**Steps:**

1. **BR Director creates Blueprints** (Department-scoped):
   ```
   "Morning - North - BR" (08:00-16:00)
   "Afternoon - North - BR" (16:00-00:00)
   "Night - North - BR" (00:00-08:00)
   ```

2. **BR Director creates Program** (automatic scheduling):
   ```
   Program: "North BR Weekday Coverage"
   Monday-Friday:
     - 3x Morning shifts
     - 2x Afternoon shifts
     - 1x Night shift
   ```

3. **System automatically generates shift instances**:
   ```
   Monday 1/27:
     - Morning shift (empty) ×3
     - Afternoon shift (empty) ×2
     - Night shift (empty) ×1
   ```

4. **North BR Lead assigns soldiers**:
   ```
   Monday 1/27:
     - Morning shift: Soldier A, Soldier B, Soldier C
     - Afternoon shift: Soldier D, Soldier E
     - Night shift: Soldier F
   ```

5. **Employees view their schedules**:
   ```
   Soldier A sees: "Monday 08:00-16:00 Morning Shift"
   ```

---

### Example 2: Duty Rotation (On-Duty Lead)

**Actors:**
- Molecule Admin (Oren)
- Officers (rank 9+)

**Steps:**

1. **Molecule Admin creates DutyRole** (Molecule-scoped):
   ```
   Name: "On-Duty Lead"
   Type: Responsibility (all-day)
   Eligibility: Officer rank required
   ```

2. **Molecule Admin creates DutyProgram** (automatic rotation):
   ```
   Program: "Weekly On-Duty Rotation"
   Frequency: Weekly
   Rotation:
     - Week 1: Officer A
     - Week 2: Officer B
     - Week 3: Officer C
     (cycles back to Officer A)
   ```

3. **System automatically generates duty assignments**:
   ```
   Monday 1/27 - Sunday 2/2: Officer A (On-Duty Lead)
   Monday 2/3 - Sunday 2/9: Officer B (On-Duty Lead)
   Monday 2/10 - Sunday 2/16: Officer C (On-Duty Lead)
   ```

4. **Officers view their duty schedule**:
   ```
   Officer A sees: "Week of 1/27: On-Duty Lead (all day)"
   ```

---

### Example 3: Manual Chore Assignment

**Actors:**
- Company Job Lead (North BR Lead)
- Employee (Soldier X)

**Steps:**

1. **Company Lead notices task needed**:
   ```
   "Equipment room needs cleaning"
   ```

2. **Company Lead creates chore** (manual, one-time):
   ```
   Chore: "Clean equipment room"
   Date: Monday 1/27
   Assign to: Soldier X
   ```

3. **Soldier X views their chores**:
   ```
   Monday 1/27: Clean equipment room
   ```

4. **Soldier X completes and marks done** (no recurrence)

---

### Example 4: Company Lead Promotes Assistant

**Actors:**
- Company Job Lead (North BR Lead)
- Employee (Soldier Y - being promoted)

**Steps:**

1. **North BR Lead decides to delegate vacation approvals**:
   ```
   Current: BR Lead approves all vacations (time-consuming)
   Goal: Give Soldier Y ability to approve vacations too
   ```

2. **BR Lead creates grant for Soldier Y** (CanGive capability):
   ```
   Grant:
     - User: Soldier Y
     - Permission: ApproveVacations
     - Scope: CompanyId=North, JobTypeId=BR
     - CanOwn: ✅ (can approve vacations)
     - CanGive: ❌ (cannot delegate further)
   ```

3. **System auto-creates dependent grant**:
   ```
   Auto-Grant:
     - User: Soldier Y
     - Permission: ViewVacationCalendar
     - (required to approve vacations)
   ```

4. **Soldier Y can now approve vacation requests** for North BR soldiers

---

## 10. Migration & Rollout Strategy

### Current State:
- Single-level "Company" organization
- Owner/Manager/Director/Employee roles (enum)
- Free-text JobTitle and Department fields
- Mixed OnDuty system

### Migration Plan:

#### **Phase 1: Data Wipe (Required)**
⚠️ **All existing data will be deleted except admin@local user**

Reason: Current structure is incompatible with new hierarchy

#### **Phase 2: Seed New Structure**
```
Create hierarchy:
  Shifty → 190 → Oren → Defence → (North, Radio, City, Hir)

Create JobTypes:
  BR, Producer, Hakam

Create DutyRoles:
  On-Duty Lead, Hakam On-Call

Create Settings:
  Department defaults (11 hours rest, 60 hours weekly cap)

Bootstrap admin:
  admin@local gets SystemAdmin grant automatically
```

#### **Phase 3: UI Migration**
- Replace role checks (`User.IsInRole()`) with grant checks
- Add hat switching UI (top navigation)
- Add grant management pages
- Add organizational structure editor

#### **Phase 4: User Training**
- Owner creates hierarchy
- Owner appoints Area Admins
- Area Admins appoint Molecule Admins
- Molecule Admins appoint Department Directors
- Department Directors appoint Company Leads

---

## 11. Benefits Summary

### For Employees:
✅ Clear visibility into their shifts, duties, and chores
✅ Understand who has authority over what (explicit grants)
✅ Fair workload distribution (duty rotation programs)

### For Company Leads:
✅ Can assign shifts department-wide (not just their company)
✅ Can approve vacations for their company only
✅ Can delegate permissions to assistants
✅ Department-level blueprints reduce duplicate work

### For Department Directors:
✅ Set department-wide policies (defaults)
✅ Create blueprints once, used by all companies
✅ Automatic scheduling via programs
✅ Visibility across entire department

### For Molecule Admins:
✅ Coordinate duties across multiple companies
✅ Automatic duty rotation (no manual scheduling)
✅ Cross-company chore assignments

### For Area Admins:
✅ High-level visibility across molecules
✅ Appoint/remove Molecule Admins
✅ Edit organizational structure

### For Owner:
✅ Single SystemAdmin grant covers everything
✅ Switch between global and company modes
✅ Complete control over hierarchy and permissions

---

## 12. Key Differentiators

| Feature | Old System | New System |
|---------|------------|------------|
| **Organizational Levels** | 1 (Company) | 6 (Project → Area → Molecule → Department → Company → User) |
| **Permission Model** | 4 roles (enum) | 26 permissions with Own/Give capabilities |
| **Work Assignment Types** | Mixed "shifts" | 3 distinct types (Shifts, Duties, Chores) |
| **Job Differentiation** | Free-text field | JobType entity with management patterns |
| **Automatic Scheduling** | Manual only | Programs for shifts, DutyPrograms for duties |
| **Settings** | Global only | Department defaults + Company overrides |
| **Cross-Company Coordination** | Not possible | Molecule-scoped duties and chores |
| **Permission Delegation** | Not possible | Give capability allows delegation |

---

## 13. Technical Implementation Highlights

### Database Schema:
- 6 hierarchy tables (Project, Area, Molecule, Department, Company, User)
- Grant table with Own/Give booleans
- JobType table with ManagementPattern enum
- DutyRole + DutyProgram tables (Molecule-scoped)
- ShiftType (Blueprints) with REQUIRED CompanyId + JobTypeId
- Settings split: DepartmentSettings + CompanySettings

### Cascade Deletion:
- Deleting Blueprint → Deletes all ShiftInstances + ProgramItems
- Confirmation modal shows impact before deletion

### Auto-Grants:
- AssignShifts automatically grants ViewShiftCalendar
- Stored as explicit grants (separate rows)

### Hat Switching:
- ActiveJobTypeId stored in Session (not Claims)
- Affects UI filtering only (not permission checks)

---

## 14. Timeline & Milestones

### Estimated Implementation: 12-16 weeks

**Weeks 1-2:** Database migration + seed scripts
**Weeks 3-4:** Grant system implementation
**Weeks 5-6:** UI migration (replace role checks)
**Weeks 7-8:** Calendar views (JobType filtering)
**Weeks 9-10:** Programs & automatic scheduling
**Weeks 11-12:** DutyPrograms & rotation
**Weeks 13-14:** Grant management UI
**Weeks 15-16:** Testing & deployment

---

## 15. Risk Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Data wipe required** | Loss of historical data | Export critical data before migration, clear communication |
| **Complex permissions** | Confusion during rollout | Comprehensive training, smart task system guides setup |
| **User resistance** | Low adoption | Phased rollout, demonstrate benefits, gather feedback |
| **Migration bugs** | System downtime | Thorough testing on staging environment, rollback plan |

---

## 16. Success Metrics

### Operational Efficiency:
- ⏱️ Time to create weekly schedule: **80% reduction** (via Programs)
- 🔄 Duty rotation errors: **95% reduction** (automatic rotation)
- 📊 Cross-company coordination: **Enabled** (Molecule-scoped duties)

### User Satisfaction:
- 👍 Manager workload: **50% reduction** (delegation via Give capability)
- 🎯 Permission clarity: **100% visibility** (explicit grants)
- ⚖️ Fair workload distribution: **Measurable** (duty rotation audit trail)

### System Quality:
- 🔒 Security: **Granular permissions** (26 permissions vs 4 roles)
- 📈 Scalability: **Supports 1000+ users** across multiple molecules
- 🌐 Flexibility: **Department defaults + Company overrides**

---

## Conclusion

This redesign transforms ShiftManager from a flat single-company system into an enterprise-grade organizational management platform that:

✅ **Reflects real military structure** (6-level hierarchy)
✅ **Separates work types** (Shifts vs Duties vs Chores)
✅ **Enables job-specific scheduling** (JobTypes with management patterns)
✅ **Automates recurring assignments** (Programs & DutyPrograms)
✅ **Provides granular permissions** (26 permissions with Own/Give)
✅ **Balances flexibility and consistency** (Department defaults + Company overrides)

The system is **ready for implementation** with all design decisions finalized and documented.

---

## Appendix: Role Permission Tables

### 1. Employee (BR at North Company)

| Grant Permission | Scope | Own | Give | What This Allows |
|-----------------|-------|-----|------|------------------|
| ViewShiftCalendar | Company=North, JobType=BR | ✅ | ❌ | View shift assignments for North BR team |
| ViewChoreCalendar | Molecule=Oren | ✅ | ❌ | View chores across entire Oren molecule |
| ViewDutyCalendar | Molecule=Oren | ✅ | ❌ | View duty assignments across Oren molecule |
| ViewVacationCalendar | Company=North, JobType=BR | ✅ | ❌ | View vacation requests for North BR team |
| ViewUsers | Company=North | ✅ | ❌ | View colleagues in North company |

**Total Grants:** ~5
**Summary:** Read-only access to own company's data

---

### 2. Company Job Lead (BR Lead at North)

| Category | Permissions | Scope | Own | Give |
|----------|------------|-------|-----|------|
| **Shifts** | AssignShifts, ViewShiftCalendar | Department=Defence, JobType=BR | ✅ | ✅ |
| **Chores** | AssignChores, ViewChoreCalendar | Molecule=Oren | ✅ | ✅ |
| **Duties** | AssignDuties, ViewDutyCalendar | Molecule=Oren | ✅ | ✅ |
| **Vacations** | ApproveVacations, ViewVacationCalendar | Company=North, JobType=BR | ✅ | ✅ |
| **Users** | CreateUsers, EditUsers, AssignJobTypes, ViewUsers | Company=North, JobType=BR | ✅ | ✅ |
| **Config** | ManageBlueprints, ManagePrograms, EditSettings | Department=Defence / Company=North | ✅ | ✅ |
| **Grants** | ManageGrants | Company=North, JobType=BR | ✅ | ✅ |

**Total Grants:** ~22
**Summary:** Mixed scopes - Department shifts, Company users/vacations, Molecule chores/duties

---

### 3. Department Job Director (BR Director)

| Category | Permissions | Scope | Own | Give | Notes |
|----------|------------|-------|-----|------|-------|
| **Shifts** | AssignShifts, ViewShiftCalendar | Department=Defence, JobType=BR | ✅ | ✅ | Department-wide |
| **Chores** | AssignChores, ViewChoreCalendar | Molecule=Oren | ✅ | ✅ | Molecule-wide |
| **Duties** | ViewDutyCalendar | Molecule=Oren | ✅ | ❌ | **Read-only** (cannot assign) |
| **Vacations** | ApproveVacations, ViewVacationCalendar | Department=Defence, JobType=BR | ✅ | ✅ | All companies |
| **Users** | CreateUsers, EditUsers, AssignJobTypes, ViewUsers | Department=Defence, JobType=BR | ✅ | ✅ | All BR users |
| **Config** | ManageBlueprints, ManagePrograms, ManageMasterPrograms, EditSettings | Department=Defence | ✅ | ✅ | Full control |

**Total Grants:** ~22
**Summary:** Department-wide authority BUT cannot assign duties (Molecule Admin's job)

---

### 4. Molecule Admin (Oren)

| Category | Permissions | Scope | Own | Give | Notes |
|----------|------------|-------|-----|------|-------|
| **View All** | ViewShiftCalendar, ViewChoreCalendar, ViewDutyCalendar, ViewVacationCalendar | Molecule=Oren | ✅ | ❌ | Read-only calendars |
| **Chores** | AssignChores | Molecule=Oren | ✅ | ✅ | Full control |
| **Duties** | AssignDuties | Molecule=Oren | ✅ | ✅ | **Primary responsibility** |
| **Users** | CreateUsers, EditUsers, DeleteUsers, AssignJobTypes, ViewUsers | Molecule=Oren | ✅ | ✅ | Cross-company |
| **Hierarchy** | EditHierarchy | Molecule=Oren | ✅ | ✅ | Edit companies/departments |
| **Grants** | ManageGrants | Molecule=Oren | ✅ | ✅ | Appoint leads |

**Total Grants:** ~17
**Summary:** Duty assignment authority + cross-JobType user management

---

### 5. Area Admin (190)

| Category | Permissions | Scope | Own | Give |
|----------|------------|-------|-----|------|
| **View All** | ViewShiftCalendar, ViewChoreCalendar, ViewDutyCalendar, ViewVacationCalendar, ViewUsers, ViewAnalytics | Area=190 | ✅ | ❌ |
| **Hierarchy** | EditHierarchy | Area=190 | ✅ | ✅ |
| **Grants** | ManageGrants | Area=190 | ✅ | ✅ |

**Total Grants:** ~8
**Summary:** High-level oversight + appoint Molecule Admins

---

### 6. Owner (SystemAdmin)

| Grant | Scope | Own | Give | What This Covers |
|-------|-------|-----|------|------------------|
| **SystemAdmin** | Project=Shifty | ✅ | ✅ | **ALL 26 permissions + global settings access** |

**Total Grants:** 1
**Summary:** God mode - single grant covers everything

---

**Document Status:** ✅ Complete
**Design Version:** 2.0 (Finalized)
**Date:** 2026-01-24
**Location:** `docs/plans/2026-01-24-shareholder-presentation.md`
**Next Step:** Implementation planning (separate session)
