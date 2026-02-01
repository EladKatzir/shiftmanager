# ShiftManager v3.0 UI/UX + Access Control Parity Audit

**Date:** 2026-01-26
**Auditor:** ShiftManagerGPT (Product+Engineering Auditor)
**Sources of Truth:**
- `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Architecture + Behaviors)
- `docs/plans/2026-01-25-v3-ui-integration-plan.md` (Screens/Flows + Integration)

---

## Executive Summary

1. **Hierarchy infrastructure complete**: All 5 hierarchy levels (Project → Area → Molecule → Company/Department) have UI management pages with IsAdmin policy enforcement.

2. **Grant system partially complete**: 58 of 107 specified grants seeded (~54%); missing JobType-specific calendars, eligibility grants, Tech shift grants, and Helper molecule grants.

3. **Role templates fully seeded**: All 11 role templates (Employee through Owner) present with auto-grant mappings.

4. **Authorization dual-mechanism operational**: Both role-based policies (IsAdmin, IsManagerOrAdmin) and grant-based authorization (GrantAuthorizationHandler, RequireGrantTagHelper) are functional.

5. **Friends/Circle system UI exists**: `/Friends/Index` page present with send/accept/reject flow; calendar integration for cross-molecule visibility NOT verified.

6. **Settings hierarchy incomplete**: Entities exist (AreaSettings, MoleculeSettings, CompanySettings), but dedicated UI pages (`/Admin/Settings/Area`, `/Admin/Settings/Molecule`, `/Admin/Settings/Company`) NOT found.

7. **Tech/Shikma shifts not specialized**: No dedicated Hanava/Delta/Yekev/Moviltech shift types or calendar filtering found.

8. **Smart Task UI exists**: `/Admin/SetupTasks/Index` page present; generation and completion flow verification needed.

9. **Calendar JobType filtering present**: `/Calendar/Month` shows JobType and ShiftGrouping filter integration.

10. **Access control uses legacy UserRole enum**: Many pages use `[Authorize(Policy = "IsAdmin")]` tied to `UserRole.Owner`, not grant-based `[Authorize(Policy = "Grant:AdminAccess")]`.

---

## Deliverable 1: Feature → UI/UX → Access Coverage Matrix

### 1.1 Organizational Hierarchy Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Project management | Sidebar → Admin → Organization | `/Admin/Organization/Projects/Index` | HierarchyService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Area management | Admin → Organization | `/Admin/Organization/Areas/Index` | HierarchyService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Molecule management | Admin → Organization | `/Admin/Organization/Molecules/Index` | HierarchyService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| MoleculeType selection | Molecule create/edit | Dropdown in Molecules page | Molecule entity | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Company management (Workforce) | Admin → Organization | `/Admin/Organization/Index` (inline) | HierarchyService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Department management (Tech) | Admin → Organization | `/Admin/Organization/Departments/Index` | HierarchyService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Hierarchy tree view | Admin → Organization | `/Admin/Organization/Index` | HierarchyService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |

### 1.2 JobType Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| JobType CRUD | Admin → Organization → JobTypes | `/Admin/Organization/JobTypes/Index` | JobTypeService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| JobType user assignment | Admin → Users → Edit | User edit page | JobTypeService | `[Authorize(Policy = "IsManagerOrAdmin")]` | ⚠️ Partial |
| JobType color/sort | JobTypes page | Create/edit form | JobType entity | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Area-scoped JobTypes | JobTypes page | AreaId FK on JobType | JobType entity | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |

### 1.3 ShiftGrouping Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| ShiftGrouping CRUD | Admin → Organization → ShiftGroupings | `/Admin/Organization/ShiftGroupings/Index` | ShiftGroupingService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Company linking | ShiftGroupings page | Multi-select companies | ShiftGroupingCompany | `[Authorize(Policy = "IsAdmin")]` | ⚠️ Needs verification |
| JobType linking | ShiftGroupings page | Multi-select JobTypes | ShiftGroupingJobType | `[Authorize(Policy = "IsAdmin")]` | ⚠️ Needs verification |

### 1.4 Role System Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Role template list | Admin → Organization → Roles | `/Admin/Organization/Roles/Index` | RoleService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Role assignment view | Roles page (mode=assignments) | Same page, different mode | RoleService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Assign role to user | Roles page | Assignment form | RoleService | `[Authorize(Policy = "IsAdmin")]` | ⚠️ Needs verification |
| Revoke role | Roles page | Revoke button | RoleService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Auto-grant on role assign | Backend | N/A (automatic) | GrantService.ApplyAutoGrantsAsync | N/A | ✅ Complete |
| 11 role templates | Seed data | N/A | RoleTemplateSeed | N/A | ✅ Complete |

### 1.5 Grant System Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Grant list (by user) | Admin → Organization → Grants | `/Admin/Organization/Grants/Index` (mode=users) | GrantService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Grant list (by grant) | Grants page | Same page (mode=grants) | GrantService | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| Manual grant assignment | Grants page | Form/modal | GrantService.GrantAsync | `[Authorize(Policy = "IsAdmin")]` | ⚠️ Needs verification |
| Revoke grant | Grants page | Revoke button | GrantService.RevokeAsync | `[Authorize(Policy = "IsAdmin")]` | ✅ Complete |
| 107 built-in grants | Seed data | N/A | GrantTypeSeed | N/A | ❌ **58 seeded** |
| Grant scope hierarchy | Backend | N/A | GrantService.HasGrantWithScopeAsync | N/A | ✅ Complete |
| CanOwn / CanGive flags | Grant entity | Display in UI | Grant entity | N/A | ✅ Complete |

### 1.6 Shift System Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| JobType filtering in calendar | Calendar → Month | `/Calendar/Month` | CalendarService | `[Authorize]` | ✅ Complete |
| ShiftGrouping filtering | Calendar → Month | `/Calendar/Month` | CalendarService | `[Authorize]` | ✅ Complete |
| Alhut/Text shift assignment | Calendar/Assignments | Various | ShiftAssignmentService | Grant-based | ⚠️ Partial |
| BR molecule-wide shifts | Calendar | Various | ShiftAssignmentService | Grant-based | ⚠️ Needs verification |
| Hakam area-wide shifts | Duty calendar | `/Calendar/Duties` (expected) | DutyService | Grant-based | ❌ **Not found** |
| Tech shifts (Hanava, Delta) | Calendar | Expected separate UI | ShiftAssignmentService | Grant-based | ❌ **Not implemented** |

### 1.7 Circle/Friends System Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Friend list | Sidebar → Friends | `/Friends/Index` | FriendshipService | `[Authorize]` | ✅ Complete |
| Send friend request | Friends page | Search + send button | FriendshipService.SendRequestAsync | `[Authorize]` | ✅ Complete |
| Accept/reject request | Friends page | Pending requests section | FriendshipService.Accept/RejectAsync | `[Authorize]` | ✅ Complete |
| Remove friend | Friends page | Remove button | FriendshipService.RemoveFriendAsync | `[Authorize]` | ✅ Complete |
| Cross-molecule calendar visibility | Calendar | Integrate friend data | Calendar integration | N/A | ⚠️ **Needs verification** |

### 1.8 Settings Hierarchy Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Area settings | Admin → Settings → Area | `/Admin/Settings/Area` (expected) | HierarchySettingsService | Grant: EditAreaSettings | ❌ **Not found** |
| Molecule settings | Admin → Settings → Molecule | `/Admin/Settings/Molecule` (expected) | HierarchySettingsService | Grant: EditMoleculeSettings | ❌ **Not found** |
| Company settings | Admin → Settings → Company | `/Admin/Settings/Company` (expected) | HierarchySettingsService | Grant: EditCompanySettings | ❌ **Not found** |
| Settings cascade logic | Backend | N/A | HierarchySettingsService.GetEffectiveSettingsAsync | N/A | ✅ Complete (service exists) |

### 1.9 Smart Task System Features

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Setup task list | Admin → SetupTasks | `/Admin/SetupTasks/Index` | SetupTaskService | `[Authorize(Policy = "IsManagerOrAdmin")]` | ✅ Complete |
| Task completion | SetupTasks page | Complete button | SetupTaskService.CompleteTaskAsync | `[Authorize]` | ⚠️ Needs verification |
| Auto-generate tasks | Backend | N/A | SetupTaskService.GenerateTasksForMoleculeAsync | N/A | ⚠️ Needs verification |

### 1.10 Authorization Infrastructure

| Spec Feature | UI Entrypoint | UI Screens | Backend Service | Access Control | Status |
|--------------|---------------|------------|-----------------|----------------|--------|
| Grant policy provider | N/A | N/A | GrantPolicyProvider | `Grant:*` dynamic policies | ✅ Complete |
| Grant authorization handler | N/A | N/A | GrantAuthorizationHandler | Scope-aware checking | ✅ Complete |
| RequireGrant tag helper | UI templates | `<require-grant key="...">` | RequireGrantTagHelper | Client-side hide | ✅ Complete |
| CurrentUserService | N/A | N/A | ICurrentUserService | Hierarchy claims | ✅ Complete |
| Hierarchy claims at login | N/A | N/A | Auth provider | MoleculeId, AreaId, etc. | ✅ Complete |

---

## Deliverable 2: Gap Report

### 2.1 Missing Features (Critical)

| ID | Feature | Spec Reference | Impact | Priority |
|----|---------|----------------|--------|----------|
| M1 | **49 grants not seeded** | Section 5.3 (107 grants) | Grant-based auth incomplete for: JobType calendars (4), Tech shifts (16), Helper molecules (10), Eligibility (8), Analytics granular (5+) | P0 |
| M2 | **Tech shift types UI** | Section 2.3, Task 7.1 | Shikma molecule cannot use Hanava/Delta/Yekev/Moviltech shifts | P1 |
| M3 | **Duty calendar page** | Task 8.3 | No dedicated `/Calendar/Duties` for Hakam/Katzin area-wide duties | P1 |
| M4 | **Settings hierarchy UI** | Phase 10, Task 10.2 | Admin cannot manage Area/Molecule/Company settings via dedicated pages | P1 |

### 2.2 Partial Implementations

| ID | Feature | Current State | Gap | Priority |
|----|---------|---------------|-----|----------|
| P1 | **JobType user assignment** | Present in user edit | No clear UI indicator of JobType change consequences (grant removal) | P2 |
| P2 | **ShiftGrouping company/JobType linking** | Page exists | Multi-select UX not verified | P2 |
| P3 | **Role assignment form** | Roles page exists | Scope selection UI (Company, Molecule, Area, JobType) needs verification | P2 |
| P4 | **Manual grant assignment** | Grants page exists | Assignment form/modal functionality not verified | P2 |
| P5 | **Friends calendar integration** | Friends page works | Calendar service integration for cross-molecule visibility unverified | P2 |
| P6 | **Smart task auto-generation** | SetupTasks page exists | Auto-generation triggers unverified | P3 |

### 2.3 UX Quality Issues

| ID | Issue | Location | Impact | Priority |
|----|-------|----------|--------|----------|
| U1 | **No hierarchy breadcrumb** | Organization pages | Users lose context when deep in hierarchy | P3 |
| U2 | **Inconsistent navigation** | Admin/Organization vs Owner panel | Hierarchy management split across Owner and Admin sections | P3 |
| U3 | **Missing empty states** | Grants, Roles pages | No guidance when no grants/roles assigned | P3 |
| U4 | **No bulk operations** | Grants page | Cannot bulk-grant/revoke permissions | P3 |
| U5 | **Missing confirmation dialogs** | Role/grant revoke | No confirmation before destructive actions | P2 |

### 2.4 Access Control Risks

| ID | Risk | Location | Current State | Required State | Priority |
|----|------|----------|---------------|----------------|----------|
| A1 | **IsAdmin = Owner only** | Organization pages | Uses `UserRole.Owner` enum | Should use `Grant:AdminAccess` or similar | P2 |
| A2 | **No Molecule Admin hierarchy access** | Organization pages | Only Owner can manage | Molecule Admin should manage within scope | P1 |
| A3 | **Grant CanGive not enforced** | Grant UI | CanGive flag stored but delegation logic unverified | P2 |
| A4 | **Assigner role chores-only** | Spec vs implementation | Assigner has shift assignment grants in seed | Should only have chore grants per spec | P2 |

---

## Deliverable 3: "Next Plan Inputs" Backlog

### 3.1 Must-Fix (P0/P1) - Functional Gaps

| Task | Description | Files | Acceptance Criteria |
|------|-------------|-------|---------------------|
| **SEED-001** | Complete grant type seeding | `Data/SeedData/GrantTypeSeed.cs` | All 107 grants from spec Section 5.3 seeded |
| **SEED-002** | Update RoleTemplateGrant mappings | `Data/SeedData/RoleTemplateSeed.cs` | Auto-grants match spec Section 4.1 exactly |
| **UI-001** | Create Duty Calendar page | `Pages/Calendar/Duties.cshtml[.cs]` | Area-wide Hakam/Katzin calendar with proper grant checks |
| **UI-002** | Create Settings hierarchy UI | `Pages/Admin/Settings/{Area,Molecule,Company}.cshtml[.cs]` | Cascade visualization, override toggles |
| **SHIFT-001** | Add Tech shift type support | `Models/ShiftType.cs`, Calendar pages | TechShiftType enum (Hanava, Delta, Yekev, Moviltech) |
| **AUTH-001** | Migrate IsAdmin to grant-based | All Organization pages | Replace `UserRole.Owner` with `Grant:AdminAccess` or hierarchical grants |

### 3.2 Should-Fix (P2) - Completeness & Polish

| Task | Description | Files | Acceptance Criteria |
|------|-------------|-------|---------------------|
| **UI-003** | Add role scope selection UI | `Pages/Admin/Organization/Roles/Index.cshtml` | Company/Molecule/Area/JobType dropdowns |
| **UI-004** | Add grant assignment modal | `Pages/Admin/Organization/Grants/Index.cshtml` | Grant type picker, scope selector, CanOwn/CanGive |
| **UI-005** | Add confirmation dialogs | Various pages | Confirm before role/grant revoke |
| **UI-006** | JobType change warning | User edit page | Warn about grant removal on JobType change |
| **UI-007** | Verify friends calendar integration | `Services/CalendarService.cs` | Friend shifts visible, cross-molecule only via friendship |
| **SEED-003** | Fix Assigner role grants | `RoleTemplateSeed.cs` | Remove shift grants, keep only chore grants per spec |

### 3.3 Nice-to-Have (P3) - UX Improvements

| Task | Description | Files | Acceptance Criteria |
|------|-------------|-------|---------------------|
| **UX-001** | Add hierarchy breadcrumb | Shared component | Show Project > Area > Molecule > Company path |
| **UX-002** | Unify admin navigation | Layout/navigation | Single admin section with sub-navigation |
| **UX-003** | Add empty states | Grants, Roles pages | Helpful guidance when no data |
| **UX-004** | Add bulk grant operations | Grants page | Multi-select grant/revoke |
| **UX-005** | Smart task dashboard widget | Admin hub | Pending tasks summary on main admin page |

---

## Appendix A: Grant Seed Gap Analysis

### Grants in Spec but Not Seeded (49 missing)

#### JobType-Specific Calendar Grants (4)
- ViewAlhutShiftCalendar
- ViewTextShiftCalendar
- ViewBRShiftCalendar
- ViewHakamShiftCalendar

#### Blueprint/Program Grants (12)
- ManageAlhutBlueprints
- ManageTextBlueprints
- ManageBRBlueprints
- ManageHakamBlueprints
- ManageAlhutPrograms
- ManageTextPrograms
- ManageBRPrograms
- ManageHakamPrograms
- ManageHanavaBlueprints
- ManageDeltaBlueprints
- ManageYekevBlueprints
- ManageMoviltechBlueprints

#### Tech Shift Grants (8)
- ViewHanavaCalendar
- ViewDeltaCalendar
- ViewYekevCalendar
- ViewMoviltechCalendar
- AssignHanavaShifts
- AssignDeltaShifts
- AssignYekevShifts
- AssignMoviltechShifts

#### Eligibility Grants (8)
- CanBeAssignedAlhutShifts
- CanBeAssignedTextShifts
- CanBeAssignedBRShifts
- CanBeAssignedHakamShifts
- CanBeAssignedHanava
- CanBeAssignedDelta
- CanBeAssignedYekev
- CanBeAssignedMoviltech

#### Helper Molecule Grants (10)
- ViewShiklutCalendar
- AssignShiklutOnCall
- CanBeShiklutOnCall
- ManageShiklutBlueprints
- ManageShiklutPrograms
- ViewNOCCalendar
- AssignNOCOnCall
- CanBeNOCOnCall
- ManageNOCBlueprints
- ManageNOCPrograms

#### Katzin Duty Grants (3)
- ViewKatzinCalendar
- CanBeKatzinOnCall
- ManageKatzinBlueprints
- ManageKatzinPrograms

#### Granular Vacation/User Grants (4)
- ViewOwnVacations
- ViewCompanyVacations
- ViewMoleculeVacations
- ViewAreaVacations

---

## Appendix B: Page Authorization Mapping

| Page | Current Authorization | Recommended Authorization |
|------|----------------------|---------------------------|
| `/Admin/Organization/Index` | IsAdmin (Owner) | Grant:ViewHierarchy |
| `/Admin/Organization/Projects/Index` | IsAdmin (Owner) | Grant:EditProject OR Owner-only |
| `/Admin/Organization/Areas/Index` | IsAdmin (Owner) | Grant:EditArea |
| `/Admin/Organization/Molecules/Index` | IsAdmin (Owner) | Grant:EditMolecule |
| `/Admin/Organization/Departments/Index` | IsAdmin (Owner) | Grant:ManageDepartments |
| `/Admin/Organization/JobTypes/Index` | IsAdmin (Owner) | Grant:ManageJobTypes |
| `/Admin/Organization/ShiftGroupings/Index` | IsAdmin (Owner) | Grant:ManageShiftGroupings |
| `/Admin/Organization/Roles/Index` | IsAdmin (Owner) | Grant:AssignRoles |
| `/Admin/Organization/Grants/Index` | IsAdmin (Owner) | Grant:ViewGrants + Grant:AssignGrants |
| `/Admin/Users` | IsManagerOrAdmin | Grant:ViewUsers + scope |
| `/Calendar/Month` | Authorize (all) | Grant:ViewShifts + scope |

---

## Appendix C: Files Audited

### UI Pages Examined
- `Pages/Admin/Organization/Index.cshtml.cs`
- `Pages/Admin/Organization/Projects/Index.cshtml.cs`
- `Pages/Admin/Organization/Areas/Index.cshtml.cs`
- `Pages/Admin/Organization/Molecules/Index.cshtml.cs`
- `Pages/Admin/Organization/Departments/Index.cshtml.cs`
- `Pages/Admin/Organization/JobTypes/Index.cshtml.cs`
- `Pages/Admin/Organization/ShiftGroupings/Index.cshtml.cs`
- `Pages/Admin/Organization/Roles/Index.cshtml.cs`
- `Pages/Admin/Organization/Grants/Index.cshtml.cs`
- `Pages/Friends/Index.cshtml.cs`
- `Pages/Calendar/Month.cshtml.cs`
- `Pages/Admin/SetupTasks/Index.cshtml.cs`

### Services Examined
- `Services/GrantService.cs`
- `Services/RoleService.cs`
- `Services/HierarchyService.cs`
- `Services/FriendshipService.cs`
- `Services/HierarchySettingsService.cs`
- `Services/ShiftAssignmentService.cs`

### Authorization Infrastructure
- `Authorization/GrantAuthorizationHandler.cs`
- `Authorization/GrantPolicyProvider.cs`
- `Authorization/GrantRequirement.cs`
- `TagHelpers/RequireGrantTagHelper.cs`
- `Middleware/ApiAuthenticationMiddleware.cs`
- `Program.cs` (policy registration)

### Seed Data
- `Data/SeedData/GrantTypeSeed.cs`
- `Data/SeedData/RoleTemplateSeed.cs`
- `Data/SeedData/ShiftyOrganizationSeed.cs`

---

**Audit Complete.** Report generated 2026-01-26.
