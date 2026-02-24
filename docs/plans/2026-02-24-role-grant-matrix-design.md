# Role-Grant Matrix Redesign

**Date**: 2026-02-24
**Status**: Approved (Rev 3 — fixes critical scope cascade bug)
**Approach**: Option C Hybrid — UserRole for tier/layout, Grants for ALL operational permissions

---

## 1. Design Principles

1. **Grant Inheritance**: Each higher role inherits ALL grants from its predecessor(s), just at wider scope
2. **Scope Widening**: Moving up the chain = same grants at wider organizational scope
3. **Two Branches**: Employee → Assigner splits into Branch A (Leads→Directors) and Branch B (BRDirector→MoleculeAdmin), merging at AreaAdmin
4. **JobType Scoping**: New `TargetJobTypeId` + `UseOwnJobType` fields on RoleTemplateGrant
5. **ID Source of Truth**: GrantTypeSeed.cs sequential `id++` count determines actual IDs (fresh reseed required)
6. **Reference by Name**: All grant references in this document use grant KEY names. IDs are listed for cross-referencing but the name is authoritative.
7. **Scope Separation**: The roleScope carries full hierarchy for extraction. DetermineEffectiveScope selects exactly ONE level. The scope cascade (`GetAccessibleCompanyIdsForGrantAsync`) evaluates from broadest to narrowest with early-return — redundant fields would cause over-granting.

## 2. Inheritance Chain

```
Trainee ──→ Employee ──→ Assigner
                           ↙        ↘
                Branch A:            Branch B:
                AlhutLead            BRDirector
                TextLead                 ↓
                    ↓              MoleculeAdmin
                AlhutDirector
                TextDirector
                    ↓                    ↓
                      AreaAdmin (merges both)
                           ↓
                         Owner
```

DepartmentLead: parallel tech track, inherits from Employee base.

### Inheritance Rules

- **Assigner**: Same as Employee (all grants at SAR) + AssignChores at ETM scope. Assigner does NOT widen Employee grants to molecule — only the extra AssignChores grant operates at molecule scope.
- **Leads** (AlhutLead, TextLead): All Employee grants at SAR + lead-specific grants at SAR with OWN jobtype. AssignAlhutShifts/AssignTextShifts at ETM.
- **Directors** (AlhutDirector, TextDirector): All Lead grants widened to ETM + director extras. Self-scoped grants stay SAR.
- **BRDirector**: All Employee grants at SAR + BR management at SAR + dual ApproveVacations (BR + Hakam jobtypes). AssignBRShifts + ViewAllShifts at ETM.
- **MoleculeAdmin**: All BRDirector grants widened to ETM + molecule admin extras + AssignAlhutShifts + AssignTextShifts. ApproveVacations widened to ALL jobtype (supersedes inherited BR+HAKAM). Self-scoped grants stay SAR.
- **AreaAdmin**: Merges ALL Director + ALL MoleculeAdmin grants at ETA + area-level extras. When merging, broader JobType wins (ALL > OWN). Self-scoped grants stay SAR.
- **Owner**: All AreaAdmin grants at ETP + system-level grants. Self-scoped grants stay SAR.
- **Trainee**: Same as Employee minus RequestSwap
- **DepartmentLead**: Employee base (with correct IDs) + department management grants

### Merge Conflict Resolution

When AreaAdmin merges Branch A (Directors) and Branch B (MoleculeAdmin), some grants exist in both branches with different JobType scoping:
- AssignAlhutShifts: Directors have OWN, MoleculeAdmin has ALL → AreaAdmin gets **ALL** (broader wins)
- AssignTextShifts: Same pattern → AreaAdmin gets **ALL**
- ApproveVacations: Directors have OWN, MoleculeAdmin has ALL → AreaAdmin gets **ALL**
- ApproveSwaps: Both have OWN/ALL → AreaAdmin gets **ALL**

## 3. Schema Changes

### 3.1 RoleTemplateGrant — New Fields
```csharp
public int? TargetJobTypeId { get; set; }  // Explicit JobType (e.g., Hakam for BRDirector)
public bool UseOwnJobType { get; set; }     // Resolve to user's own JobType at login
```

**Resolution order at login in ApplyAutoGrantsAsync:**
1. `TargetJobTypeId` set → grant.JobTypeId = TargetJobTypeId (explicit)
2. `UseOwnJobType` true → grant.JobTypeId = user's JobTypeId from hierarchy context
3. Both null → grant.JobTypeId = null (all jobtypes)

**CRITICAL**: The JobTypeId resolution MUST happen BEFORE scope determination and dedup checks. The resolved JobTypeId becomes part of the effective scope for dedup comparison.

### 3.2 GrantScopeMode — New Value
```csharp
ExpandToProject = 4  // Keeps only ProjectId — for Owner
```

**DetermineEffectiveScope update (CRITICAL — scope isolation):**

```csharp
private GrantScope DetermineEffectiveScope(GrantScopeMode scopeMode, GrantScope roleScope)
{
    return scopeMode switch
    {
        // SAR: Company level — extract ONLY CompanyId + DepartmentId.
        // MUST NOT include ProjectId/AreaId/MoleculeId because
        // GetAccessibleCompanyIdsForGrantAsync cascades broadest-first
        // (ProjectId → AreaId → MoleculeId → CompanyId) and would over-grant.
        GrantScopeMode.SameAsRole => new GrantScope(
            CompanyId: roleScope.CompanyId,
            DepartmentId: roleScope.DepartmentId
        ),
        GrantScopeMode.ExpandToMolecule => new GrantScope(MoleculeId: roleScope.MoleculeId),
        GrantScopeMode.ExpandToArea => new GrantScope(AreaId: roleScope.AreaId),
        GrantScopeMode.ExpandToProject => new GrantScope(ProjectId: roleScope.ProjectId),
        GrantScopeMode.Custom => roleScope,
        _ => throw new ArgumentOutOfRangeException(nameof(scopeMode), $"Unknown scope mode: {scopeMode}")
    };
}
```

**Why SAR must strip**: The scope cascade in `GetAccessibleCompanyIdsForGrantAsync` (lines 204-260) uses `if/continue` from broadest to narrowest. If a Grant record has BOTH `ProjectId=1` AND `CompanyId=5`, the ProjectId branch fires first and returns ALL companies in the project — massively over-granting an Employee. SAR must produce a clean single-level scope.

### 3.3 Login roleScope — Full Hierarchy

Both `Login.cshtml.cs` and `GriffinService.cs` must build a **full hierarchy** roleScope so that ETM/ETA/ETP can extract the level they need:

```csharp
var roleScope = new GrantScope(
    ProjectId: hierarchyContext?.Path.Project?.Id,
    AreaId: hierarchyContext?.Path.Area?.Id,
    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
    DepartmentId: user.DepartmentId,       // NEW — for DepartmentLead SAR
    CompanyId: user.CompanyId,
    JobTypeId: hierarchyContext?.JobType?.Id // NEW — for UseOwnJobType resolution
);
```

The roleScope is raw material. `DetermineEffectiveScope` selects exactly one level from it.

### 3.4 ApplyAutoGrantsAsync — Updated Flow

The updated flow for each RoleTemplateGrant during login:

```
For each autoGrant in template.Grants:
  1. Resolve JobTypeId:
     - if autoGrant.TargetJobTypeId != null → resolvedJobTypeId = autoGrant.TargetJobTypeId
     - else if autoGrant.UseOwnJobType → resolvedJobTypeId = roleScope.JobTypeId
     - else → resolvedJobTypeId = null
  2. Determine effective scope (extracts ONE level from roleScope):
     effectiveScope = DetermineEffectiveScope(autoGrant.ScopeMode, roleScope)
  3. Overlay resolved JobTypeId:
     effectiveScope = effectiveScope with { JobTypeId = resolvedJobTypeId }
  4. Check dedup against effectiveScope (all fields including JobTypeId)
  5. Insert if not duplicate
```

This ensures:
- SAR grants get clean company-level scope (no hierarchy leakage)
- ETM grants get molecule-level scope (extracted from full roleScope)
- BRDirector's dual ApproveVacations produce different effective scopes (different JobTypeIds)
- ALL-jobtype grants get JobTypeId=null (universal)
- OWN-jobtype grants get user's specific JobTypeId

### 3.5 Program.cs Seeding Dedup Key — Include TargetJobTypeId

Current key: `$"{m.RoleTemplateId}:{m.GrantTypeId}"`
Updated key: `$"{m.RoleTemplateId}:{m.GrantTypeId}:{m.TargetJobTypeId?.ToString() ?? "null"}"`

This prevents the BRDirector dual ApproveVacations grants from being dropped during seeding.

### 3.6 BuildGrantScopeForUserAsync — Role-Independent Full Hierarchy

**Major simplification**: Since DetermineEffectiveScope now handles scope-level selection, BuildGrantScopeForUserAsync is role-independent. It always returns the full hierarchy.

```csharp
private async Task<GrantScope> BuildGrantScopeForUserAsync(AppUser user, string roleTemplateKey)
{
    var company = await _db.Companies
        .IgnoreQueryFilters()
        .Include(c => c.Molecule)
            .ThenInclude(m => m!.Area)
                .ThenInclude(a => a!.Project)
        .FirstOrDefaultAsync(c => c.Id == user.CompanyId);

    if (company == null)
        return GrantScope.Company(user.CompanyId);

    // Full hierarchy — DetermineEffectiveScope extracts the right level per grant
    return new GrantScope(
        ProjectId: company.Molecule?.Area?.ProjectId,
        AreaId: company.Molecule?.AreaId,
        MoleculeId: company.MoleculeId,
        DepartmentId: user.DepartmentId,
        CompanyId: user.CompanyId,
        JobTypeId: user.JobTypeId
    );
}
```

**Why this works**: The role-specific behavior is entirely encoded in `ScopeMode` on each `RoleTemplateGrant`. SAR extracts (CompanyId, DepartmentId). ETM extracts MoleculeId. ETA extracts AreaId. ETP extracts ProjectId. The roleScope is just the raw hierarchy data.

**What this fixes**:
- AlhutLead's ETM grants (AssignAlhutShifts) now correctly get MoleculeId (previously missing)
- BRDirector's ETM grants (AssignBRShifts, ViewAllShifts) now correctly get MoleculeId (previously missing)
- Assigner's ETM grant (AssignChores) now correctly gets MoleculeId
- All roles' SAR grants correctly get CompanyId only (no over-granting via hierarchy cascade)

### 3.7 Stale Grant Cleanup on JobType Change

When a user's JobType changes between logins, old JobType-scoped auto-grants may remain stale. The solution:

In `ApplyAutoGrantsAsync`, before inserting new grants, clean up stale JobType-scoped auto-grants:

```
1. Load all existing auto-grants for this user from this template
2. For each existing auto-grant where JobTypeId != null:
   - If the template no longer has a matching (GrantTypeId, resolved JobTypeId) → delete it
3. Proceed with normal insert-if-not-exists logic
```

This makes `ApplyAutoGrantsAsync` fully idempotent AND self-healing.

### 3.8 Migration Rollback Strategy

The EF migration adds two nullable columns (`TargetJobTypeId` and `UseOwnJobType`) and one enum value (`ExpandToProject`). Rollback:

1. **Schema**: Standard EF `Down()` migration drops the two columns
2. **Seed data**: A fresh reseed with the old RoleTemplateSeed.cs restores previous state
3. **Runtime grants**: `ApplyAutoGrantsAsync` is idempotent — old grants remain valid; new ones are additive
4. **No destructive changes**: Existing Grant records are never modified, only new ones inserted

## 4. Correct Grant ID Reference

GrantTypeSeed.cs is source of truth. Sequential `id++` from 1:

### Shifts (1-11)
| ID | Key |
|----|-----|
| 1 | ViewShifts |
| 2 | ViewAllShifts |
| 3 | AssignAlhutShifts |
| 4 | AssignTextShifts |
| 5 | AssignBRShifts |
| 6 | AssignTechShifts |
| 7 | EditShiftPrograms |
| 8 | CreateShiftPrograms |
| 9 | DeleteShiftPrograms |
| 10 | EditShiftTypes |
| 11 | CreateShiftTypes |

### Duties (12-15)
| ID | Key |
|----|-----|
| 12 | ViewDuties |
| 13 | AssignHakamDuties |
| 14 | AssignKatzinDuties |
| 15 | EditDutyPrograms |

### Chores (16-19)
| ID | Key |
|----|-----|
| 16 | ViewChores |
| 17 | AssignChores |
| 18 | EditChoreTypes |
| 19 | CreateChoreTypes |

### Vacations (20-24)
| ID | Key |
|----|-----|
| 20 | ViewVacations |
| 21 | RequestVacation |
| 22 | ApproveVacations |
| 23 | OverrideVacationLimits |
| 24 | ApproveExtendedLeave |

### Swaps (25-27)
| ID | Key |
|----|-----|
| 25 | RequestSwap |
| 26 | ApproveSwaps |
| 27 | InitiateSwap |

### User Management (28-34)
| ID | Key |
|----|-----|
| 28 | ViewUsers |
| 29 | EditUsers |
| 30 | CreateUsers |
| 31 | DeactivateUsers |
| 32 | ResetPasswords |
| 33 | AssignJobTypes |
| 34 | ViewAllUsers |

### Grant Management (35-38)
| ID | Key |
|----|-----|
| 35 | ViewGrants |
| 36 | AssignGrants |
| 37 | RevokeGrants |
| 38 | AssignRoles |

### Hierarchy (39-47)
| ID | Key |
|----|-----|
| 39 | ViewHierarchy |
| 40 | EditCompany |
| 41 | EditMolecule |
| 42 | EditArea |
| 43 | CreateCompany |
| 44 | CreateMolecule |
| 45 | ManageShiftGroupings |
| 46 | ManageJobTypes |
| 47 | ManageDepartments |

### Settings (48-51)
| ID | Key |
|----|-----|
| 48 | ViewSettings |
| 49 | EditCompanySettings |
| 50 | EditMoleculeSettings |
| 51 | EditAreaSettings |

### Analytics (52-54)
| ID | Key |
|----|-----|
| 52 | ViewAnalytics |
| 53 | ViewReports |
| 54 | ExportData |

### Email (55-56)
| ID | Key |
|----|-----|
| 55 | SendNotifications |
| 56 | ConfigureEmailSettings |

### System (57-60)
| ID | Key |
|----|-----|
| 57 | AdminAccess |
| 58 | SystemConfiguration |
| 59 | ViewAuditLog |
| 60 | ManageApiKeys |

### Per-JobType Calendars (61-64)
| ID | Key |
|----|-----|
| 61 | ViewAlhutShiftCalendar |
| 62 | ViewTextShiftCalendar |
| 63 | ViewBRShiftCalendar |
| 64 | ViewHakamShiftCalendar |

### Eligibility (65-68)
| ID | Key |
|----|-----|
| 65 | CanBeAssignedAlhutShifts |
| 66 | CanBeAssignedTextShifts |
| 67 | CanBeAssignedBRShifts |
| 68 | CanBeAssignedHakamShifts |

### Per-JobType Blueprints/Programs (69-76)
| ID | Key |
|----|-----|
| 69 | ManageAlhutBlueprints |
| 70 | ManageAlhutPrograms |
| 71 | ManageTextBlueprints |
| 72 | ManageTextPrograms |
| 73 | ManageBRBlueprints |
| 74 | ManageBRPrograms |
| 75 | ManageHakamBlueprints |
| 76 | ManageHakamPrograms |

### Tech Calendars (77-80)
| ID | Key |
|----|-----|
| 77 | ViewHanavaCalendar |
| 78 | ViewDeltaCalendar |
| 79 | ViewYekevCalendar |
| 80 | ViewMoviltechCalendar |

### Tech Assignment (81-84)
| ID | Key |
|----|-----|
| 81 | AssignHanavaShifts |
| 82 | AssignDeltaShifts |
| 83 | AssignYekevShifts |
| 84 | AssignMoviltechShifts |

### Tech Blueprints/Programs (85-92)
| ID | Key |
|----|-----|
| 85 | ManageHanavaBlueprints |
| 86 | ManageHanavaPrograms |
| 87 | ManageDeltaBlueprints |
| 88 | ManageDeltaPrograms |
| 89 | ManageYekevBlueprints |
| 90 | ManageYekevPrograms |
| 91 | ManageMoviltechBlueprints |
| 92 | ManageMoviltechPrograms |

### Tech Eligibility (93-96)
| ID | Key |
|----|-----|
| 93 | CanBeAssignedHanava |
| 94 | CanBeAssignedDelta |
| 95 | CanBeAssignedYekev |
| 96 | CanBeAssignedMoviltech |

### Helper Molecules (97-106)
| ID | Key |
|----|-----|
| 97 | ViewShiklutCalendar |
| 98 | AssignShiklutChores |
| 99 | ManageShiklutBlueprints |
| 100 | ManageShiklutPrograms |
| 101 | CanBeAssignedShiklut |
| 102 | ViewNOCCalendar |
| 103 | AssignNOCChores |
| 104 | ManageNOCBlueprints |
| 105 | ManageNOCPrograms |
| 106 | CanBeAssignedNOC |

### Katzin (107-108)
| ID | Key |
|----|-----|
| 107 | ManageKatzinBlueprints |
| 108 | ManageKatzinPrograms |

### Calendar Misc (109-111)
| ID | Key |
|----|-----|
| 109 | ManageShiftCapacity |
| 110 | WriteOverviewNotes |
| 111 | ManageOnDutyTypes |

### Navigation (112-115)
| ID | Key |
|----|-----|
| 112 | AccessAdminNavigation |
| 113 | DirectorHubAccess |
| 114 | ManagerHomeAccess |
| 115 | ViewCompanyCalendar |

### User Management Extended (116-118)
| ID | Key |
|----|-----|
| 116 | ManageJoinRequests |
| 117 | ViewCompanyUsers |
| 118 | EditCompanyUsers |

### Dynamic (119-123)
| ID | Key |
|----|-----|
| 119 | ManageAnnouncements |
| 120 | ViewSystemAlerts |
| 121 | ViewAllAreas |
| 122 | ManageOnDuty |
| 123 | ReorderHierarchy |

## 5. Role-by-Role Grant Assignments

### ScopeMode Legend
- **SAR** = SameAsRole → produces `(CompanyId, DepartmentId)` only — company level
- **ETM** = ExpandToMolecule → produces `(MoleculeId)` only — molecule level
- **ETA** = ExpandToArea → produces `(AreaId)` only — area level
- **ETP** = ExpandToProject → produces `(ProjectId)` only — project level [NEW]

**Important**: SAR always resolves to company scope regardless of the role tier. A Director's SAR grant and an Employee's SAR grant produce the same scope level (CompanyId). The tier-specific widening is done via ETM/ETA/ETP on each grant.

### JobType Legend
- **OWN** = UseOwnJobType=true (resolved at login to user's JobTypeId)
- **ALL** = No JobType filter (TargetJobTypeId=null, UseOwnJobType=false)
- **BR/HAKAM/etc.** = TargetJobTypeId set to specific JobType ID

### Self-Scoped Grants
These grants are ALWAYS SAR regardless of role tier:
- RequestVacation, RequestSwap — personal actions
- CanBeAssignedAlhutShifts, CanBeAssignedTextShifts, CanBeAssignedBRShifts, CanBeAssignedHakamShifts — eligibility within own company

---

**Grant Count Summary** (verified against RoleTemplateSeed.cs — 531 total entries):

| Template | Role | Grants |
|----------|------|--------|
| 1 | Employee | 19 |
| 12 | Trainee | 18 |
| 8 | Assigner | 20 |
| 3 | AlhutLead | 38 |
| 4 | TextLead | 38 |
| 2 | BRDirector | 45 |
| 5 | AlhutDirector | 46 |
| 6 | TextDirector | 46 |
| 7 | MoleculeAdmin | 67 |
| 9 | DepartmentLead | 25 |
| 10 | AreaAdmin | 81 |
| 11 | Owner | 88 |

### 5.1 EMPLOYEE (Template 1) — 19 grants

Base role for all workforce users. All grants at SAR (company-scoped).

| Grant | Scope | JobType | CanGive |
|-------|-------|---------|---------|
| ViewShifts | SAR | ALL | N |
| ViewChores | SAR | ALL | N |
| ViewDuties | SAR | ALL | N |
| ViewVacations | SAR | ALL | N |
| RequestVacation | SAR | ALL | N |
| RequestSwap | SAR | ALL | N |
| ViewCompanyCalendar | SAR | ALL | N |
| ViewCompanyUsers | SAR | ALL | N |
| ViewGrants | SAR | ALL | N |
| ViewHierarchy | SAR | ALL | N |
| WriteOverviewNotes | SAR | ALL | N |
| ViewAlhutShiftCalendar | SAR | ALL | N |
| ViewTextShiftCalendar | SAR | ALL | N |
| ViewBRShiftCalendar | SAR | ALL | N |
| ViewHakamShiftCalendar | SAR | ALL | N |
| CanBeAssignedAlhutShifts | SAR | OWN | N |
| CanBeAssignedTextShifts | SAR | OWN | N |
| CanBeAssignedBRShifts | SAR | OWN | N |
| CanBeAssignedHakamShifts | SAR | OWN | N |

**Known bug being fixed**: Current seed incorrectly gives Employee ApproveVacations (ID 22) instead of RequestVacation (ID 21), and ApproveSwaps (ID 26) instead of RequestSwap (ID 25). This is a privilege escalation caused by off-by-one ID errors in the current RoleTemplateSeed.cs.

### 5.2 TRAINEE (Template 12) — 18 grants

Same as Employee **minus RequestSwap**.

(All 19 Employee grants except RequestSwap.)

### 5.3 ASSIGNER (Template 8) — 20 grants

Employee grants at **same scope** (SAR) + AssignChores at molecule scope.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all 19 Employee grants)* | **SAR** | *(same as Employee)* | N | **NOT widened** — keeps company scope |
| AssignChores | **ETM** | ALL | N | **Only this grant operates at molecule scope** |

**Important**: Assigner does NOT widen Employee grants to molecule. The Assigner is an Employee who can additionally assign chores across the molecule, but all other permissions remain company-scoped.

### 5.4 ALHUT LEAD (Template 3) — 38 grants

All Employee grants (SAR) + lead-specific grants. Lead grants mostly SAR with OWN jobtype. AssignAlhutShifts at ETM.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all 19 Employee grants)* | SAR | *(same)* | N | Base |
| AssignAlhutShifts | **ETM** | OWN | N | Molecule scope per user request |
| ViewAllShifts | SAR | ALL | N | |
| EditShiftPrograms | SAR | OWN | N | |
| CreateShiftPrograms | SAR | OWN | N | |
| DeleteShiftPrograms | SAR | OWN | N | |
| EditShiftTypes | SAR | OWN | N | |
| CreateShiftTypes | SAR | OWN | N | |
| ManageAlhutBlueprints | SAR | OWN | N | |
| ManageAlhutPrograms | SAR | OWN | N | |
| ManageShiftCapacity | SAR | OWN | N | |
| AssignChores | SAR | ALL | N | |
| ApproveVacations | SAR | OWN | N | |
| ApproveSwaps | SAR | OWN | N | |
| ViewUsers | SAR | ALL | N | |
| AssignGrants | SAR | OWN | N | User confirmed intentional |
| RevokeGrants | SAR | OWN | N | User confirmed intentional |
| AccessAdminNavigation | SAR | ALL | N | |
| ManagerHomeAccess | SAR | ALL | N | |
| ViewSystemAlerts | SAR | ALL | N | |

### 5.5 TEXT LEAD (Template 4) — 38 grants

Same structure as AlhutLead but with Text-specific grants:
- AssignTextShifts instead of AssignAlhutShifts
- ManageTextBlueprints instead of ManageAlhutBlueprints
- ManageTextPrograms instead of ManageAlhutPrograms

All other grants identical to AlhutLead.

### 5.6 BR DIRECTOR (Template 2) — 44 grants

All Employee grants (SAR) + company management. Notable: dual ApproveVacations (BR + Hakam). AssignBRShifts + ViewAllShifts at ETM.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all 19 Employee grants)* | SAR | *(same)* | N | Base |
| AssignBRShifts | **ETM** | ALL | Y | Molecule scope |
| ViewAllShifts | **ETM** | ALL | N | Molecule visibility |
| EditShiftPrograms | SAR | OWN | N | |
| CreateShiftPrograms | SAR | OWN | N | |
| DeleteShiftPrograms | SAR | OWN | N | |
| EditShiftTypes | SAR | OWN | N | |
| CreateShiftTypes | SAR | OWN | N | |
| ManageBRBlueprints | SAR | ALL | N | |
| ManageBRPrograms | SAR | ALL | N | |
| ManageHakamBlueprints | SAR | ALL | N | BR manages Hakam too |
| ManageHakamPrograms | SAR | ALL | N | |
| ManageShiftCapacity | SAR | OWN | N | |
| AssignChores | SAR | ALL | N | |
| ApproveVacations | SAR | **BR** | N | TargetJobTypeId=BR JobType |
| ApproveVacations | SAR | **HAKAM** | N | TargetJobTypeId=Hakam JobType |
| OverrideVacationLimits | SAR | ALL | N | |
| ApproveSwaps | SAR | ALL | N | |
| InitiateSwap | SAR | ALL | N | |
| ViewUsers | SAR | ALL | N | |
| EditUsers | SAR | ALL | N | |
| ManageJoinRequests | SAR | ALL | N | |
| EditCompanyUsers | SAR | ALL | N | |
| AccessAdminNavigation | SAR | ALL | N | |
| ManagerHomeAccess | SAR | ALL | N | |
| ViewSystemAlerts | SAR | ALL | N | |

**Note**: ApproveVacations appears twice with different TargetJobTypeId values. The seeding dedup key MUST include TargetJobTypeId to prevent the second entry from being dropped.

### 5.7 ALHUT DIRECTOR (Template 5) — 45 grants

All AlhutLead grants widened to **ETM** + director-specific grants. Self-scoped grants stay SAR.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all AlhutLead non-self-scoped grants)* | **ETM** | *(same)* | *(same)* | Scope widens to molecule |
| *(self-scoped grants: Request*, CanBeAssigned*)* | **SAR** | *(same)* | N | Stay company-scoped |
| AssignAlhutShifts | ETM | OWN | **Y** | Now CanGive=true (was N at Lead) |
| ViewAllUsers | ETM | ALL | N | |
| AssignRoles | ETM | ALL | N | For leads |
| DirectorHubAccess | ETM | ALL | N | |
| ManageJoinRequests | ETM | ALL | N | |
| EditCompanyUsers | ETM | ALL | N | |
| ManageAnnouncements | ETM | ALL | N | |
| ViewAllAreas | ETM | ALL | N | |

### 5.8 TEXT DIRECTOR (Template 6) — 45 grants

Same as AlhutDirector but Text-specific (AssignTextShifts, ManageTextBlueprints, ManageTextPrograms).

### 5.9 MOLECULE ADMIN (Template 7) — 66 grants

All BRDirector grants widened to **ETM** + molecule admin extras + **AssignAlhutShifts + AssignTextShifts**. ApproveVacations widened to **ALL** jobtype (supersedes inherited BR+HAKAM). Self-scoped grants stay SAR.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all BRDirector non-self-scoped grants)* | **ETM** | *(same)* | *(same)* | Scope widens to molecule |
| *(self-scoped grants)* | **SAR** | *(same)* | N | Stay company-scoped |
| **ApproveVacations** | **ETM** | **ALL** | N | **Widened from BR+HAKAM to ALL** — MolAdmin manages all jobtypes |
| **AssignAlhutShifts** | ETM | ALL | Y | **Added** — MolAdmin manages all shift types |
| **AssignTextShifts** | ETM | ALL | Y | **Added** — MolAdmin manages all shift types |
| EditChoreTypes | ETM | ALL | N | |
| CreateChoreTypes | ETM | ALL | N | |
| CreateUsers | ETM | ALL | N | |
| DeactivateUsers | ETM | ALL | N | |
| ViewAllUsers | ETM | ALL | N | |
| AssignGrants | ETM | ALL | Y | |
| RevokeGrants | ETM | ALL | N | |
| AssignRoles | ETM | ALL | Y | |
| EditMolecule | ETM | ALL | N | |
| ManageShiftGroupings | ETM | ALL | N | |
| ViewSettings | ETM | ALL | N | |
| EditCompanySettings | ETM | ALL | N | |
| EditMoleculeSettings | ETM | ALL | N | |
| ViewAnalytics | ETM | ALL | N | |
| ViewReports | ETM | ALL | N | |
| ViewAuditLog | ETM | ALL | N | |
| ManageAnnouncements | ETM | ALL | N | |
| ManageAlhutBlueprints | ETM | ALL | N | All jobtypes |
| ManageAlhutPrograms | ETM | ALL | N | |
| ManageTextBlueprints | ETM | ALL | N | |
| ManageTextPrograms | ETM | ALL | N | |

**Changes from BRDirector inheritance**:
- ApproveVacations: BR's dual (BR + HAKAM) replaced by single ALL — MolAdmin manages everyone in the molecule
- Added AssignAlhutShifts + AssignTextShifts — MolAdmin can assign all shift types
- ApproveSwaps inherits as ALL from BRDirector (already correct)

### 5.10 DEPARTMENT LEAD (Template 9) — 25 grants

Employee base with correct IDs + department management grants. All SAR (department-scoped via DepartmentId in roleScope).

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all 19 Employee grants)* | SAR | *(same)* | N | Employee base (with corrected IDs) |
| ViewUsers | SAR | ALL | N | Department users |
| EditUsers | SAR | ALL | N | |
| AssignJobTypes | SAR | ALL | N | |
| ManageDepartments | SAR | ALL | N | |
| ViewAnalytics | SAR | ALL | N | |
| ViewReports | SAR | ALL | N | |

**Note**: SAR for DepartmentLead produces (CompanyId, DepartmentId) — the DepartmentId narrows scope to their department within the company.

### 5.11 AREA ADMIN (Template 10) — 80 grants

Merges BOTH branches (all Director grants + all MoleculeAdmin grants) at **ETA** scope, plus area-level extras. Self-scoped grants stay SAR.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all MoleculeAdmin grants)* | **ETA** | **ALL** | *(same)* | Branch B widened; ALL jobtype wins over OWN |
| *(Director-only grants not in MolAdmin)* | **ETA** | **ALL** | *(same)* | Branch A merged; ALL jobtype for all |
| AssignHakamDuties | ETA | ALL | Y | |
| AssignKatzinDuties | ETA | ALL | Y | |
| EditDutyPrograms | ETA | ALL | N | |
| EditArea | ETA | ALL | N | |
| CreateCompany | ETA | ALL | N | |
| CreateMolecule | ETA | ALL | N | |
| ManageJobTypes | ETA | ALL | N | |
| EditAreaSettings | ETA | ALL | N | |
| ManageOnDuty | ETA | ALL | N | |
| ManageOnDutyTypes | ETA | ALL | N | |
| ManageKatzinBlueprints | ETA | ALL | N | |
| ManageKatzinPrograms | ETA | ALL | N | |

Self-scoped grants (Request*, CanBeAssigned*) stay SAR.

**Merge conflict resolution**: When the same grant appears in both branches with different JobType scoping, the broader one (ALL) wins. AreaAdmin manages an entire area, so ALL is correct.

### 5.12 OWNER (Template 11) — 87 grants

All AreaAdmin grants at **ETP** (ExpandToProject) scope, plus system-level. Self-scoped grants stay SAR.

| Grant | Scope | JobType | CanGive | Note |
|-------|-------|---------|---------|------|
| *(all AreaAdmin grants)* | **ETP** | *(same)* | *(same)* | Widened to project |
| AdminAccess | ETP | ALL | Y | |
| SystemConfiguration | ETP | ALL | N | |
| ManageApiKeys | ETP | ALL | N | |
| ExportData | ETP | ALL | N | |
| SendNotifications | ETP | ALL | N | |
| ConfigureEmailSettings | ETP | ALL | N | |
| ReorderHierarchy | ETP | ALL | N | |

Self-scoped grants (Request*, CanBeAssigned*) stay SAR.

## 6. Implementation Phases

### Phase 1: Schema + Seed (This task)

#### Step 1: Model & Enum Changes
1. Add `ExpandToProject = 4` to GrantScopeMode enum
2. Add `TargetJobTypeId` (int?) and `UseOwnJobType` (bool, default false) to RoleTemplateGrant model
3. Create EF migration for the two new columns

#### Step 2: GrantService Updates
4. Update `DetermineEffectiveScope` — SAR extracts (CompanyId, DepartmentId) only + ExpandToProject case + throw on unknown
5. Update `ApplyAutoGrantsAsync` — resolve JobTypeId from TargetJobTypeId/UseOwnJobType BEFORE scope determination and dedup
6. Add stale JobType-scoped grant cleanup to `ApplyAutoGrantsAsync`
7. Simplify `BuildGrantScopeForUserAsync` to role-independent full hierarchy

#### Step 3: Login Flow
8. Add full hierarchy (DepartmentId, JobTypeId) to login roleScope in `Login.cshtml.cs`
9. Add full hierarchy (DepartmentId, JobTypeId) to login roleScope in `GriffinService.cs`

#### Step 4: Seeding
10. Update Program.cs RoleTemplateGrant dedup key to include TargetJobTypeId
11. Rewrite `RoleTemplateSeed.cs` with correct grant IDs and new matrix
12. Fresh database reseed

### Phase 2: Enforcement (Future)
1. Replace role-based page access checks with grant checks
2. Wire up unenforced grants (ViewSettings, ViewAnalytics, etc.)
3. Add grant checks to API endpoints that currently rely on role only

## 7. Test Plan for Dual-JobType Grants

The BRDirector dual ApproveVacations is a novel pattern. Specific tests:

### Seeding Tests
1. **Dedup correctness**: After seeding, BRDirector has exactly 2 ApproveVacations RoleTemplateGrants (one BR, one Hakam)
2. **Reseed idempotence**: Running seed twice doesn't create duplicates

### Login/Auto-Grant Tests
3. **Dual grant provisioning**: When BRDirector logs in, exactly 2 ApproveVacations Grants are created (one with BR JobTypeId, one with Hakam JobTypeId)
4. **Dedup across logins**: Second login doesn't create duplicate grants
5. **Scope correctness**: Both grants have company-level scope (SAR → CompanyId only) with different JobTypeIds

### Enforcement Tests
6. **BR vacation approval**: BRDirector can approve vacations for BR-jobtype users
7. **Hakam vacation approval**: BRDirector can approve vacations for Hakam-jobtype users
8. **Other jobtype denial**: BRDirector CANNOT approve vacations for Alhut-jobtype users

### Scope Cascade Tests
9. **SAR isolation**: Employee's SAR grant produces (CompanyId only) — NOT (ProjectId + AreaId + MoleculeId + CompanyId)
10. **ETM extraction**: AlhutLead's ETM grant (AssignAlhutShifts) produces (MoleculeId only) — no CompanyId
11. **No over-granting**: An Employee with ViewShifts(SAR) does NOT get access to companies in other molecules via ProjectId leak

### Edge Cases
12. **JobType change**: If BRDirector's own JobType changes, UseOwnJobType grants update; TargetJobTypeId grants don't
13. **Template change**: If BRDirector template is modified to remove Hakam ApproveVacations, stale grant is cleaned up on next login

## 8. Issues Addressed from Critical Review

### Rev 2 Issues (Opus Review) — All Resolved
| Issue | Resolution |
|-------|-----------|
| C-01: ApplyAutoGrantsAsync dedup drops dual ApproveVacations | Resolve JobTypeId BEFORE scope determination and dedup (Section 3.4) |
| C-02: Program.cs seeding dedup ignores TargetJobTypeId | Updated dedup key includes TargetJobTypeId (Section 3.5) |
| C-03: ExpandToProject not in enum | Added to enum (Section 3.2) |
| C-04: Login flows missing JobTypeId | Added to both flows with full hierarchy (Section 3.3) |
| C-05: RoleTemplateGrant model missing fields | Added TargetJobTypeId + UseOwnJobType (Section 3.1) |
| I-01: Off-by-one IDs in RoleTemplateSeed | Full rewrite with correct IDs (Phase 1 Step 4) |
| I-02: Employee has ApproveVacations instead of RequestVacation | Fixed in Section 5.1 |
| I-03: DepartmentLead inherits wrong IDs | Rewritten with correct Employee base (Section 5.10) |
| I-04: ApplyAutoGrantsAsync needs scope override before dedup | Updated flow in Section 3.4 |
| I-05: AreaAdmin missing from BuildGrantScopeForUserAsync | Now role-independent (Section 3.6) |
| I-06: DetermineEffectiveScope default silently succeeds | Changed to throw (Section 3.2) |
| I-07: MoleculeAdmin missing AssignAlhutShifts/AssignTextShifts | Added in Section 5.9 |
| I-08: GetAccessibleCompanyIdsForGrantAsync ignores JobTypeId | Noted — cascades company access, not JobType. No change needed. |
| I-09: Stale grants on JobType change | Cleanup logic in Section 3.7 |
| M-01 through M-06 | All addressed (rollback strategy, test plan, reference-by-name, etc.) |

### Rev 3 Issues (Scope Cascade Review) — All Resolved
| Issue | Resolution |
|-------|-----------|
| **CRITICAL: SAR returns full roleScope → cascade over-grants** | SAR now extracts (CompanyId, DepartmentId) only (Section 3.2) |
| **CRITICAL: AlhutLead/BRDirector ETM gets MoleculeId=null** | roleScope always includes full hierarchy; ETM extracts MoleculeId (Sections 3.3, 3.6) |
| BuildGrantScopeForUserAsync was role-specific and incomplete | Simplified to role-independent (Section 3.6) |
| MoleculeAdmin ApproveVacations inherits BR+HAKAM | Widened to ALL (Section 5.9) |
| AreaAdmin merge conflict resolution undefined | Added merge rule: broader JobType wins (Section 2) |
| Login roleScope missing DepartmentId | Added (Section 3.3) |

## 9. Key Decisions Log

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Architecture | Option C Hybrid | Roles for tiers, grants for operations |
| ID source | GrantTypeSeed.cs | Sequential id++ = actual DB order on fresh seed |
| ViewDuties for Employee | Yes (read-only) | Soldiers see duty calendar |
| AlhutLead AssignAlhutShifts scope | Molecule (ETM) | User confirmed intentional |
| Employee WriteOverviewNotes | Yes | User confirmed intentional |
| Employee ViewHierarchy | All/all | User confirmed intentional |
| BRDirector ApproveVacations | Two grants (BR + Hakam) | Via TargetJobTypeId |
| AlhutLead AssignGrants/RevokeGrants | Yes (own jobtype) | User confirmed intentional |
| Per-jobtype blueprint grants | All 8 exist (69-76) | No new GrantTypes needed |
| Inheritance model | Two-branch merge | A: Leads→Directors, B: BR→MolAdmin, merge at AreaAdmin |
| Assigner scope | SAR (same as Employee) + AssignChores ETM | Assigner is Employee + molecule-wide chore assignment only |
| MoleculeAdmin shift assignment | Includes AssignAlhut + AssignText | Admin who manages blueprints must also assign shifts |
| MoleculeAdmin ApproveVacations | ALL (supersedes BR+HAKAM) | MolAdmin manages all jobtypes in molecule |
| DetermineEffectiveScope SAR | Extracts CompanyId+DepartmentId only | Prevents cascade over-granting via broadest-first evaluation |
| DetermineEffectiveScope default | Throw exception | Prevent silent scope bugs |
| BuildGrantScopeForUserAsync | Role-independent full hierarchy | ScopeMode on each grant determines level; no role-specific switch needed |
| Stale grant cleanup | On login via ApplyAutoGrantsAsync | Self-healing idempotent approach |
| Mid-session staleness | Accepted risk | Grants re-resolved on next login |
| AreaAdmin merge conflicts | Broader JobType wins (ALL > OWN) | AreaAdmin manages entire area, all jobtypes |
