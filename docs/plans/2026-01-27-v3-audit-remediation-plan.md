# V3 Audit Remediation Plan - Complete Implementation Design

**Date:** 2026-01-27
**Status:** Ready for Implementation
**Priority:** Production Release
**Timeline:** 4-5 days

---

## Executive Summary

This document outlines the complete implementation plan to remediate all findings from the V3 UI/UX + Access Control Parity Audit (2026-01-26). The goal is full production readiness for Shifty, with no shortcuts or deferred items.

**Scope:** 17 implementation items + 1 verification item across 3 phases.

---

## Table of Contents

1. [Complete Scope](#1-complete-scope)
2. [Dependency Graph](#2-dependency-graph)
3. [Phase 1: Foundation](#3-phase-1-foundation)
4. [Phase 2: Core Features](#4-phase-2-core-features)
5. [Phase 3: Polish & Verification](#5-phase-3-polish--verification)
6. [Testing Strategy](#6-testing-strategy)
7. [Rollout Plan](#7-rollout-plan)
8. [Risk Mitigation](#8-risk-mitigation)
9. [Success Criteria](#9-success-criteria)

---

## 1. Complete Scope

### P0/P1 — Must-Fix (6 items)

| ID | Task | Description |
|----|------|-------------|
| **SEED-001** | Complete grant seeding | Add 49 missing grants to reach 107 total |
| **SEED-002** | Fix RoleTemplateGrant mappings | Ensure auto-grants match spec Section 4.1 exactly |
| **UI-001** | Create Duty Calendar | `/Calendar/Duties` for Hakam/Katzin area-wide |
| **UI-002** | Create Settings Hierarchy UI | Area/Molecule/Company pages with cascade visualization |
| **SHIFT-001** | Tech shift types | Hanava/Delta/Yekev/Moviltech enum + calendar integration |
| **AUTH-001** | Migrate to grant-based auth | Replace `IsAdmin` (Owner-only) with `Grant:*` policies |

### P2 — Should-Fix (6 items)

| ID | Task | Description |
|----|------|-------------|
| **UI-003** | Role scope selection UI | Verify/fix Company/Molecule/Area/JobType dropdowns work |
| **UI-004** | Grant assignment modal | Verify/fix grant type picker, scope selector, CanOwn/CanGive |
| **UI-005** | Confirmation dialogs | Add confirm before role/grant revoke |
| **UI-006** | JobType change warning | Warn about grant removal when changing user's JobType |
| **UI-007** | Friends calendar integration | Verify friend shifts visible cross-molecule |
| **SEED-003** | Fix Assigner role | Remove shift grants, keep only chore grants |

### P3 — UX Polish (5 items)

| ID | Task | Description |
|----|------|-------------|
| **UX-001** | Hierarchy breadcrumb | Show Project > Area > Molecule > Company path |
| **UX-002** | Unify admin navigation | Single admin section with sub-navigation |
| **UX-003** | Empty states | Helpful guidance when no grants/roles |
| **UX-004** | Bulk grant operations | Multi-select grant/revoke |
| **UX-005** | Smart task dashboard widget | Pending tasks summary on admin hub |

### Access Control Verification (1 item)

| ID | Risk | Fix |
|----|------|-----|
| **A3** | CanGive not enforced | Verify/implement delegation logic in GrantService |

---

## 2. Dependency Graph

```
SEED-001 (Grant Seeding)
    │
    ├──────────────────┬─────────────────┬──────────────────┐
    ▼                  ▼                 ▼                  ▼
AUTH-001           UI-001            SHIFT-001          SEED-003
(Grant Migration)  (Duty Calendar)   (Tech Shifts)      (Fix Assigner)
    │                  │                 │
    ▼                  │                 │
UI-002             ◄───┴─────────────────┘
(Settings UI)
    │
    ▼
┌───┴───┬───────┬───────┬───────┐
▼       ▼       ▼       ▼       ▼
UI-003  UI-004  UI-005  UI-006  UI-007
(Scope) (Grant) (Confirm)(Warn) (Friends)
    │
    ▼
┌───┴───┬───────┬───────┬───────┐
▼       ▼       ▼       ▼       ▼
UX-001  UX-002  UX-003  UX-004  UX-005
(Bread) (Nav)   (Empty) (Bulk)  (Widget)
```

### Three Implementation Phases

| Phase | Items | Purpose |
|-------|-------|---------|
| **Phase 1: Foundation** | SEED-001, SEED-002, SEED-003, AUTH-001 | All grants seeded, auth migrated |
| **Phase 2: Core Features** | UI-001, UI-002, SHIFT-001 | Missing pages built, Tech shifts working |
| **Phase 3: Polish** | UI-003→UI-007, UX-001→UX-005, A3 | Verification, UX improvements |

---

## 3. Phase 1: Foundation

### SEED-001: Complete Grant Type Seeding

**Current state:** 58 grants seeded
**Target state:** 107 grants seeded (49 to add)

**File:** `Data/SeedData/GrantTypeSeed.cs`

**Missing grants by category:**

#### JobType-Specific Calendar Grants (4)
- ViewAlhutShiftCalendar
- ViewTextShiftCalendar
- ViewBRShiftCalendar
- ViewHakamShiftCalendar

#### Blueprint/Program Grants (12)
- ManageAlhutBlueprints, ManageTextBlueprints, ManageBRBlueprints, ManageHakamBlueprints
- ManageAlhutPrograms, ManageTextPrograms, ManageBRPrograms, ManageHakamPrograms
- ManageHanavaBlueprints, ManageDeltaBlueprints, ManageYekevBlueprints, ManageMoviltechBlueprints

#### Tech Shift Grants (8)
- ViewHanavaCalendar, ViewDeltaCalendar, ViewYekevCalendar, ViewMoviltechCalendar
- AssignHanavaShifts, AssignDeltaShifts, AssignYekevShifts, AssignMoviltechShifts

#### Eligibility Grants (8)
- CanBeAssignedAlhutShifts, CanBeAssignedTextShifts, CanBeAssignedBRShifts, CanBeAssignedHakamShifts
- CanBeAssignedHanava, CanBeAssignedDelta, CanBeAssignedYekev, CanBeAssignedMoviltech

#### Helper Molecule Grants (10)
- ViewShiklutCalendar, AssignShiklutOnCall, CanBeShiklutOnCall, ManageShiklutBlueprints, ManageShiklutPrograms
- ViewNOCCalendar, AssignNOCOnCall, CanBeNOCOnCall, ManageNOCBlueprints, ManageNOCPrograms

#### Katzin Duty Grants (4)
- ViewKatzinCalendar, CanBeKatzinOnCall, ManageKatzinBlueprints, ManageKatzinPrograms

#### Granular Vacation Grants (4)
- ViewOwnVacations, ViewCompanyVacations, ViewMoleculeVacations, ViewAreaVacations

**Validation checklist:**
- [ ] Count after seed = exactly 107
- [ ] Each grant has correct Category, DefaultScope, IsSystem=true
- [ ] Keys match spec Section 5.3 verbatim
- [ ] No duplicate keys
- [ ] Migration idempotent (runs on fresh and existing DB)

---

### SEED-002: Fix RoleTemplateGrant Mappings

**File:** `Data/SeedData/RoleTemplateSeed.cs`

**Target:** Each of 11 roles has exactly the grants specified in spec Section 4.1

**Implementation:** Compare seeded mappings against spec table row-by-row.

---

### SEED-003: Fix Assigner Role

**Current state:** Assigner has shift assignment grants
**Target state:** Assigner has ONLY chore grants

**Remove from Assigner:**
- ❌ AssignAlhutShifts
- ❌ AssignTextShifts
- ❌ AssignBRShifts
- ❌ Any other shift-related grants

**Keep only:**
- ✅ AssignChores
- ✅ ViewChoreCalendar

**Security note:** This is a permission reduction. Existing Assigner users will lose shift assignment capability.

---

### AUTH-001: Migrate IsAdmin to Grant-Based Authorization

**Files to modify:**

| Page | Current | Target |
|------|---------|--------|
| `/Admin/Organization/Index` | IsAdmin | `Grant:ViewHierarchy` |
| `/Admin/Organization/Projects/Index` | IsAdmin | `Grant:ManageAreas` |
| `/Admin/Organization/Areas/Index` | IsAdmin | `Grant:ManageAreas` |
| `/Admin/Organization/Molecules/Index` | IsAdmin | `Grant:ManageMolecules` |
| `/Admin/Organization/Departments/Index` | IsAdmin | `Grant:ManageDepartments` |
| `/Admin/Organization/JobTypes/Index` | IsAdmin | `Grant:ManageJobTypes` |
| `/Admin/Organization/ShiftGroupings/Index` | IsAdmin | `Grant:ManageShiftGroupings` |
| `/Admin/Organization/Roles/Index` | IsAdmin | `Grant:AssignRoles` |
| `/Admin/Organization/Grants/Index` | IsAdmin | `Grant:ViewGrants` + `Grant:ManageGrants` |

**Implementation pattern:**

```csharp
// Before:
[Authorize(Policy = "IsAdmin")]
public class IndexModel : PageModel

// After:
[Authorize(Policy = "Grant:ViewHierarchy")]
public class IndexModel : PageModel
```

**Critical:** Add scope filtering in page logic:

```csharp
public async Task OnGetAsync()
{
    var userScope = await _grantService.GetUserScopeForGrantAsync(
        User.GetUserId(), "ViewHierarchy");

    Molecules = await _hierarchyService.GetMoleculesInScopeAsync(userScope);
}
```

**Pre-requisite:** Owner role template must include all hierarchy grants BEFORE migration.

---

## 4. Phase 2: Core Features

### UI-001: Create Duty Calendar Page

**Target:** `/Calendar/Duties.cshtml` for area-wide Hakam and Katzin scheduling

**Page structure:**
```
┌─────────────────────────────────────────────────┐
│  Duty Calendar                    [Area: 190 ▼] │
├─────────────────────────────────────────────────┤
│  [Hakam On-Call]  [Katzin On-Call]              │
├─────────────────────────────────────────────────┤
│  ◄ January 2026 ►                               │
│  ┌───┬───┬───┬───┬───┬───┬───┐                 │
│  │Mon│Tue│Wed│Thu│Fri│Sat│Sun│                 │
│  ├───┼───┼───┼───┼───┼───┼───┤                 │
│  │   │   │ H │ H │ H │ K │ K │                 │
│  │   │   │Bob│Bob│Ana│Dan│Dan│                 │
│  └───┴───┴───┴───┴───┴───┴───┘                 │
└─────────────────────────────────────────────────┘
```

**Authorization:**
```csharp
[Authorize(Policy = "Grant:ViewHakamShiftCalendar,Grant:ViewKatzinCalendar")]
```

**Backend:** `DutyService.GetDutiesForAreaAsync(areaId, dutyType, dateRange)`

---

### UI-002: Create Settings Hierarchy UI

**Target:** `/Admin/Settings/Index` with scope selector

**Page structure:**
```
┌─────────────────────────────────────────────────┐
│  Settings                                       │
├─────────────────────────────────────────────────┤
│  Scope: [Area ▼] [190 ▼]                        │
├─────────────────────────────────────────────────┤
│  Setting          │ Value    │ Source          │
│  ─────────────────┼──────────┼─────────────────│
│  Default Rest Hrs │ 11       │ Area (default)  │
│  Weekly Cap       │ 60       │ Area (default)  │
│  ─────────────────┼──────────┼─────────────────│
│  [Molecule: Oren]                               │
│  Rest Hours       │ [  ] Override  │ 11        │
│  Weekly Cap       │ [✓] Override  │ 55        │
└─────────────────────────────────────────────────┘
```

**Authorization:** Scope-dependent grants (EditAreaSettings, EditMoleculeSettings, EditCompanySettings)

**Backend:** Uses `HierarchySettingsService.GetEffectiveSettingsAsync()`

---

### SHIFT-001: Tech Shift Type Support

**Target:** Shikma molecule can use Hanava/Delta/Yekev/Moviltech shifts

**1. Add TechShiftType enum:**
```csharp
public enum TechShiftType
{
    Hanava,    // Pie + Tao departments
    Delta,     // Pie + Tao + Samapkam departments
    Yekev,     // Yekev department
    Moviltech  // Department heads only
}
```

**2. Extend ShiftBlueprint:**
```csharp
public TechShiftType? TechShiftType { get; set; }
```

**3. Calendar integration:**
- Add TechShiftType filter to `/Calendar/Month`
- Filter by department eligibility

**4. Department eligibility matrix:**

| Shift Type | Eligible Departments |
|------------|---------------------|
| Hanava | Pie, Tao |
| Delta | Pie, Tao, Samapkam |
| Yekev | Yekev |
| Moviltech | Department heads only |

---

## 5. Phase 3: Polish & Verification

### UI-003: Role Scope Selection UI

**Target:** Dynamic scope dropdowns based on role's ScopeLevel

- Company → Company dropdown
- CompanyJobType → Company + JobType dropdowns
- Molecule → Molecule dropdown
- MoleculeJobType → Molecule + JobType dropdowns
- Area → Area dropdown
- Department → Department dropdown (Tech only)

---

### UI-004: Grant Assignment Modal

**Target:** Full-featured modal with grant type picker, scope selector, CanOwn/CanGive toggles

---

### UI-005: Confirmation Dialogs

**Locations:**
- Roles page: Revoke role
- Grants page: Revoke grant
- Organization: Delete entity

**Pattern:**
```javascript
async function confirmRevoke(roleId, userId) {
    const impact = await fetch(`/api/roles/${roleId}/revoke-impact?userId=${userId}`);
    const { message } = await impact.json();
    if (confirm(message)) { /* proceed */ }
}
```

---

### UI-006: JobType Change Warning

**Trigger:** User edit page, JobType dropdown change

**Warning content:**
- Grants to be REMOVED
- Grants to be ADDED
- Existing shift assignments to be UNASSIGNED

---

### UI-007: Friends Calendar Integration

**Verification:**
- [ ] User A adds User B (different molecule) as friend
- [ ] User B accepts
- [ ] User A's calendar shows User B's shifts marked as "Friend"
- [ ] User A cannot see non-friend shifts from other molecule
- [ ] Friend shifts are read-only

**Fix location if broken:** `Services/CalendarService.cs`

---

### A3: CanGive Delegation Verification

**Test:**
1. User with CanGive=true can delegate grant
2. User with CanGive=false cannot delegate (clear error)

**Fix location if broken:** `Services/GrantService.cs`

---

### UX-001: Hierarchy Breadcrumb

**Component:** `Pages/Shared/_HierarchyBreadcrumb.cshtml`

**Visual:** `🏗️ Shifty › 190 › Oren › Tzafona`

---

### UX-002: Unify Admin Navigation

**New structure:**
```
📊 Administration
├── 🏗️ Organization (Hierarchy, Areas, Molecules, etc.)
├── 👥 People (Users, Roles, Grants)
├── ⚙️ Settings (Area, Molecule, Company)
├── 📋 Setup Tasks
└── 🔍 Audit Log
```

---

### UX-003: Empty States

**Locations:** Grants, Roles, Users, Setup Tasks, Friends

**Pattern:** Icon + message + action button

---

### UX-004: Bulk Grant Operations

**Target:** Select multiple users, grant/revoke in batch

---

### UX-005: Smart Task Dashboard Widget

**Location:** `/Admin/Index`

**Widget content:**
```
📋 Setup Tasks                    [View All →]
⚠️ 3 tasks need attention
──────────────────────────────────────────────
🔴 Setup Molecule Admin for Gefen
   Assigned to: You  •  Created 2 days ago

🟡 Setup Shift Groupings for Ella
   Assigned to: You  •  Created 1 day ago
```

**Priority indicators:**
- 🔴 Red: Overdue (> 3 days)
- 🟡 Yellow: Pending
- 🟢 Green: Recently completed

---

## 6. Testing Strategy

### Unit Tests (Phase 1)

```csharp
[Fact]
public async Task GrantTypeSeed_Creates_Exactly_107_Grants()

[Fact]
public async Task AssignerRole_Has_Only_Chore_Grants()
```

### Integration Tests (Phase 2)

```csharp
[Fact]
public async Task MoleculeAdmin_Can_Access_Molecules_Page()

[Fact]
public async Task MoleculeAdmin_Sees_Only_Own_Molecule()

[Fact]
public async Task Employee_Cannot_Access_Organization_Pages()

[Fact]
public async Task CanGive_False_Prevents_Delegation()
```

### E2E Tests (Critical Paths)

| Test | Steps | Expected |
|------|-------|----------|
| Role Assignment Flow | Admin → Roles → Assign | Role + auto-grants created |
| Grant Delegation Flow | User with CanGive → Assign | Grant created |
| Duty Calendar View | User → Calendar → Duties | Correct duties visible |
| Settings Cascade | Admin → Settings → Override | Override persisted |
| Tech Shift Assignment | Shikma admin → Assign Hanava | Only eligible users shown |
| Friend Visibility | Add friend → Calendar | Friend shifts visible |

### Manual Verification Checklist

**Phase 1:**
- [ ] Query `SELECT COUNT(*) FROM GrantTypes` = 107
- [ ] Query Assigner grants = only chore grants
- [ ] Login as Owner: all pages accessible
- [ ] Login as Molecule Admin: scoped pages only
- [ ] Login as Employee: 403 on Organization pages

**Phase 2:**
- [ ] Duty Calendar displays Hakam/Katzin tabs
- [ ] Settings cascade works
- [ ] Tech calendar shows shift type filter

**Phase 3:**
- [ ] Breadcrumb on all Organization pages
- [ ] Empty states appear
- [ ] Confirmation dialogs work
- [ ] Bulk operations work
- [ ] Dashboard widget shows tasks

---

## 7. Rollout Plan

### Day 1: Foundation

**Morning:**
- SEED-001: Add 49 missing grants
- SEED-002: Verify/fix RoleTemplateGrant mappings
- SEED-003: Fix Assigner role
- Run unit tests

**Afternoon:**
- AUTH-001: Migrate Organization pages
- Update Owner role template
- Run integration tests
- Manual verification

### Day 2: Core Features

**Morning:**
- UI-001: Build Duty Calendar page
- UI-002: Build Settings Hierarchy UI
- Run E2E tests

**Afternoon:**
- SHIFT-001: Add Tech shift type support
- Integrate with Calendar/Month
- Test Shikma molecule

### Day 3: Verification & Polish

**Morning:**
- UI-003 through UI-007: Verify and fix
- A3: Verify CanGive delegation

**Afternoon:**
- UX-001 through UX-005: Build polish items

### Day 4: Final Testing

**Morning:**
- Full E2E test suite
- Manual walkthrough all personas
- Fix discovered issues

**Afternoon:**
- Load test
- Security review
- Final verification checklist
- Prepare release notes

### Day 5: Buffer / Meeting

- Buffer for overflow
- Demo rehearsal
- Shareholder meeting

---

## 8. Risk Mitigation

### Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Auth migration breaks Owner access | Medium | Critical | Add grants to Owner BEFORE policy change |
| Seed migration fails on prod data | Low | High | Test on production copy first |
| Existing users lose permissions | Medium | High | Run grant backfill script |
| CalendarService missing area-scope | Medium | Medium | Check early, budget extension time |
| CanGive logic missing | Medium | Medium | Check GrantService early |
| Performance regression | Low | Medium | Add indexes on scope columns |

### Rollback Plan

**Seed rollback:**
```sql
DELETE FROM Grants WHERE GrantTypeId IN (SELECT Id FROM GrantTypes WHERE Id > 58);
DELETE FROM GrantTypes WHERE Id > 58;
```

**Auth rollback:** Revert to `[Authorize(Policy = "IsAdmin")]`

**Full rollback:** Git revert + database restore

---

## 9. Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| All 107 grants seeded | `COUNT(*) FROM GrantTypes = 107` |
| Owner has full access | Manual verification |
| Molecule Admin scoped correctly | Sees only own molecule |
| Employee blocked from admin | 403 on Organization pages |
| Duty Calendar works | Hakam/Katzin visible |
| Settings cascade works | Override reflects correctly |
| Tech shifts work | Shikma can assign Hanava |
| All P2 items verified | 6/6 checked |
| All P3 items complete | 5/5 checked |
| No test regressions | CI green |

---

## Pre-Implementation Checklist

- [ ] Database backup created
- [ ] Current branch committed and pushed
- [ ] CI pipeline green
- [ ] Spec Section 5.3 available for reference
- [ ] Spec Section 4.1 available for reference
- [ ] Test user accounts ready (Owner, Molecule Admin, Employee, Tech user)

---

**Document Status:** Ready for Implementation
**Estimated Duration:** 4-5 days
**Author:** Claude (Staff Systems Engineer)
**Date:** 2026-01-27
