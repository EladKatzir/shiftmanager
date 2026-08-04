# Cluster 2 — Access & Permissions — **PARTIAL** Audit Report

**Date:** 2026-08-03  |  **Branch:** `dev`  |  **Status: INCOMPLETE — see scope bounds below**

## SCOPE BOUND — READ FIRST

This cluster's workflow was destroyed twice by network outages (`ENOTFOUND`). Of 21 agents,
**only 4 completed**. Concretely:

- **2 of 8 planned audit dimensions produced results**: the Permission Simulator dimension and the
  Roles/Grants admin-pages dimension.
- **6 dimensions never ran at all**: `grant-engine` (the core authorization primitive),
  `dead-controls` (the systematic dead-grant sweep), `role-templates`, `policy-parity`,
  `idor-app-wide`, and `multi-company-scope`. There are ZERO findings for those surfaces —
  that is absence of evidence, NOT evidence of absence.
- Only **2 findings** were adversarially refuted (1 CONFIRMED, 1 REFUTED). Everything else here is
  **unverified single-agent output** except where marked MAIN-LOOP VERIFIED.
- The synthesis agent died; this report was assembled mechanically from `journal.jsonl`.

**The single most important open question of this cluster was never answered:** does a
molecule-scoped grant carrying a JobTypeId satisfy a request for a DIFFERENT molecule
(`GrantService.HasGrantWithScopeAsync`)? The `grant-engine` dimension that was tasked with
settling it never ran.

## MAIN-LOOP VERIFIED — critical privilege escalation

### C2-F01 (CRITICAL) — any `AssignRoles` holder can self-assign the globally-scoped Owner role

**Verified by hand (not by an agent), reading the code and querying the seeded DB.**

Chain of evidence:

1. `Pages/Admin/Organization/Roles/Assign.cshtml.cs:17` — the ONLY gate is
   `[Authorize(Policy = "Grant:AssignRoles")]`, which merely proves the caller may assign *some*
   role *somewhere*.
2. `Assign.cshtml.cs:207-212` — `AvailableUsers` is **every active user in the deployment**
   (`_db.Users.IgnoreQueryFilters().Where(u => u.IsActive)`), with no scope filter.
3. `AvailableRoles` is every active template. **`Owner` (id 11) has `IsActive=1` and
   `CanBeAssignedByDefault=1`**, so it is offered in the dropdown.
4. `Assign.cshtml.cs:179-203` `ValidateScope` switches on `RoleScopeLevel`; Owner's `ScopeLevel=7`
   matches NO case and falls through to `_ => null` — **an all-NULL scope is accepted**.
5. `Assign.cshtml.cs:152` calls `_roleService.AssignRoleAsync(SelectedUserId, SelectedRoleId, scope,
   currentUserId)` with **no check** that the caller may assign that role, to that user, at that scope.
6. `Services/RoleService.AssignRoleAsync` performs no authorization either, and then calls
   `ApplyAutoGrantsAsync(userId, roleTemplateId, scope)` — writing globally-scoped Owner grants.
7. `Services/Navigation/NavRegistry.cs:95` puts `/Admin/Organization/Roles` in the sidebar for
   anyone with `Grant:AssignRoles`, so the page is discoverable, not URL-crafted.

**Seeded-data blast radius:** `AssignRoles` (GrantTypeId 38) is held by **95 distinct users** across
99 grant rows — **90 of them company-scoped** (ordinary desk leads).

**THE ROOT CAUSE IS CIRCULAR DELEGATION OF AUTHORIZATION.** Both files carry a `SECURITY-AUDITED`
comment, and each defers the boundary to the other:

- The page (`:15-16`): *"All IgnoreQueryFilters() in this class are SAFE — requires Grant:AssignRoles policy"*
- The service: *"SAFE — ... The access boundary is enforced UPSTREAM by the caller:
  Pages/Admin/Organization/Roles/Assign.cshtml.cs gates with the AssignRoles grant."*

Each statement is individually true and jointly vacuous. Neither validates the TARGET.
This is the audit's meta-pattern in its purest form: a `SECURITY-AUDITED` comment that asserts a
guard which does not exist, and thereby terminates review.

**Proposed fix (not yet applied):** before assigning, verify (a) the target user is within the
caller's grant scope, (b) the requested role's privilege level does not exceed the caller's own,
(c) the requested scope is a subset of the caller's scope, and (d) `CanGive` is honoured. The same
check belongs in `RoleService` itself, not only in the page, since the service is the reusable
boundary. Also fix `ValidateScope` so a role whose ScopeLevel is unmatched FAILS CLOSED rather than
returning null.


## MAIN-LOOP VERIFIED — C2-F02 (CRITICAL): the core scope primitive discards Molecule/Area scope

**This answers the cluster's headline open question. Verified by hand: full method read + DB query.**

`Services/GrantService.cs:276-280`, the LAST branch of `HasGrantWithScopeAsync`:

```csharp
// JobType scope (optionally combined with company)
if (grant.JobTypeId.HasValue && jobTypeId.HasValue && grant.JobTypeId == jobTypeId)
{
    if (!grant.CompanyId.HasValue || (companyId.HasValue && grant.CompanyId == companyId))
        return true;
}
```

It returns `true` on a JobType match whenever the grant has **no CompanyId** — **without ever
consulting the grant's `MoleculeId` or `AreaId`**. The molecule branch at `:251-261` correctly
rejects a cross-molecule request; this branch then grants it anyway, three lines later.

### Trace (grant `{MoleculeId: 1, JobTypeId: 1}`, request `moleculeId: 9, jobTypeId: 1`)
- `:226` `jobTypeMismatch` = false (job types match)
- `:251` molecule branch: `:253` 1 != 9 -> no; `:257` requires `!moleculeId.HasValue` -> no. **Correctly rejected.**
- `:269` company branch: grant has no CompanyId -> skipped
- `:276` job types match -> `:278` `!grant.CompanyId.HasValue` is TRUE -> **`return true`**

The comment at `:273-275` claims targeted grants "require both company AND jobtype to match". The
code does the opposite when CompanyId is absent: it treats "no company restriction" as "no scope
restriction at all", ignoring a Molecule/Area restriction that IS present.

### Real blast radius (queried from the seeded DB)
Grants with `JobTypeId` set, `CompanyId` NULL, and a Molecule/Area scope — the exploitable shape:
**268 rows, 21 grant types, 44 distinct users.** Includes `AssignGrants`, `RevokeGrants`,
`DeactivateUsers`, `ApproveVacations`, `ApproveExtendedLeave`, `ApproveSwaps`,
`OverrideVacationLimits`, `ViewAuditLog`, `ViewAnalytics`, `ViewReports`, `EditShiftTypes`,
`CreateShiftTypes`, `ManageShiftCapacity`, and the program/blueprint management grants.
Example — Alhut lead (user 118): `EditShiftTypes {MoleculeId:1, CompanyId:null, JobTypeId:1}`.

### Reachability (calibrated)
The bypass fires ONLY when the caller passes a `jobTypeId` that matches the grant's (`:276` requires
`jobTypeId.HasValue`). Call sites that omit `jobTypeId` are unaffected. `Table.cshtml.cs`
`OnPostAssignEmployeeAsync` passes `shift.JobTypeId`, so it is reachable there. Each other call site
that passes a jobTypeId needs its own trace before asserting end-to-end exploitability.

### Proposed fix (not applied)
The final branch must not treat "no CompanyId" as "no scope". It should require that the grant's
Molecule/Area/Project scope (if any) also covers the request — i.e. only fall through to a pure
JobType match when the grant carries NO hierarchy scope at all. Note this interacts with the
deliberate legacy behaviour documented at `:217-225` (requests that omit `jobTypeId` keep matching
on molecule/area alone), so the fix must preserve that path while closing this one.


## MAIN-LOOP VERIFIED — C2-F03 (CRITICAL): 57 of 138 grants are silently unenforced

**Method (mechanical, reproducible — script at `scratchpad/dead_grants.py`):** extract every grant key
from `Data/SeedData/GrantTypeSeed.cs`, then walk all live `.cs`/`.cshtml` (excluding
Backups/docs/FinalProductPublish/ProjectPublish/obj/bin) counting references as either the direct key
literal `"X"` or the policy literal `"Grant:X"` (the latter is what `[Authorize(Policy=...)]` and
`GrantPolicyProvider` use — **a word-chars-only regex misses them and makes every policy-gated grant
look dead**; this bit me once and is corrected here). Classify seed-only / test-only / production.
Cross-check provisioning against the seeded DB.

### Result

| Class | Count |
|---|---|
| Enforced by production code | **74** |
| Unenforced but correctly `IsDeprecated` | **7** |
| **Unenforced and NOT deprecated (silently dead)** | **57** |

The 7 deprecated ones (`AssignTextShifts`, `AssignBRShifts`, `AssignTechShifts`, `AssignHanavaShifts`,
`AssignDeltaShifts`, `AssignYekevShifts`, `AssignMoviltechShifts`) are the per-job-type assign grants
collapsed into `AssignShifts` (id137). **Those are working as intended** and are the proof that the
deprecation mechanism exists and someone knows how to use it — which is exactly why the other 57 are a
defect rather than mere neglect.

### Why this is severe

The Grants admin UI renders all 57 as real, scoped, assignable permissions. An administrator can grant
or revoke one, watch it persist, and get no signal that it changes nothing. The most dangerous are the
ones that sound most protective:

- **`CanBeAssignedAlhutShifts` / `CanBeAssignedTextShifts` / `CanBeAssignedBRShifts` /
  `CanBeAssignedHakamShifts` — 609 users each.** These read as ELIGIBILITY controls. Revoking one does
  NOT prevent that soldier from being assigned those shifts.
- `ViewVacations`, `RequestVacation`, `ViewCompanyUsers`, `ViewCompanyCalendar`,
  `ViewAlhutShiftCalendar` / `Text` / `BR` / `Hakam` — 609 users each (visibility controls).
- `ViewAuditLog` (95 users), `ManageShiftCapacity` (95), `OverrideVacationLimits` (54), `InitiateSwap` (54).

**Consequence: a permission audit of this system currently cannot be trusted**, because nothing in the
grant list distinguishes an enforced permission from an ornamental one.

### Calibration
Many of these are likely SUPERSEDED rather than forgotten (e.g. `DoesShifts` + `ShiftCategory`
replaced the `CanBeAssigned*` eligibility model; `ManagerHomeAccess` gates the Audit Log page instead
of `ViewAuditLog`). The defect is not the supersession — it is that supersession never reached
`IsDeprecated`, so the UI keeps presenting them as live controls.

### Proposed fix (not applied)
For each of the 57: either (a) wire it to a real check, or (b) mark `IsDeprecated=true` and hide/annotate
it in the Grants UI. Then add a guard test asserting that every non-deprecated seeded grant key has at
least one production reference — so this class cannot silently reappear. (That test is the durable fix;
the 57 are the backlog.)

### Full list (silently dead, held by real users)

| Grant | Users holding | Templates granting it |
|---|---|---|
| `ViewVacations` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewTextShiftCalendar` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewHakamShiftCalendar` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewCompanyUsers` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewCompanyCalendar` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewBRShiftCalendar` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewAlhutShiftCalendar` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `RequestVacation` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `CanBeAssignedTextShifts` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `CanBeAssignedHakamShifts` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `CanBeAssignedBRShifts` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `CanBeAssignedAlhutShifts` | 609 | AreaAdmin, Assigner, BRDirector, DepartmentLead, Director, Employee, Lead, MoleculeAdmin, Owner, Trainee |
| `ViewAuditLog` | 95 | AreaAdmin, BRDirector, DepartmentLead, Director, Lead, MoleculeAdmin, Owner |
| `ManageShiftCapacity` | 95 | AreaAdmin, BRDirector, Director, Lead, MoleculeAdmin, Owner |
| `OverrideVacationLimits` | 54 | AreaAdmin, BRDirector, Director, MoleculeAdmin, Owner |
| `InitiateSwap` | 54 | AreaAdmin, BRDirector, Director, MoleculeAdmin, Owner |
| `ManageHakamPrograms` | 51 | AreaAdmin, BRDirector, MoleculeAdmin, Owner |
| `ManageHakamBlueprints` | 51 | AreaAdmin, BRDirector, MoleculeAdmin, Owner |
| `ManageBRPrograms` | 51 | AreaAdmin, BRDirector, MoleculeAdmin, Owner |
| `ManageBRBlueprints` | 51 | AreaAdmin, BRDirector, MoleculeAdmin, Owner |
| `EditDutyPrograms` | 51 | AreaAdmin, BRDirector, MoleculeAdmin, Owner |
| `ManageTextPrograms` | 50 | AreaAdmin, Director, Lead, MoleculeAdmin, Owner |
| `ManageTextBlueprints` | 50 | AreaAdmin, Director, Lead, MoleculeAdmin, Owner |
| `ManageAlhutPrograms` | 50 | AreaAdmin, Director, Lead, MoleculeAdmin, Owner |
| `ManageAlhutBlueprints` | 50 | AreaAdmin, Director, Lead, MoleculeAdmin, Owner |
| `ViewYekevCalendar` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ViewMoviltechCalendar` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ViewHanavaCalendar` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ViewDeltaCalendar` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageYekevPrograms` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageYekevBlueprints` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageMoviltechPrograms` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageMoviltechBlueprints` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageKatzinPrograms` | 6 | AreaAdmin, MoleculeAdmin, Owner |
| `ManageKatzinBlueprints` | 6 | AreaAdmin, MoleculeAdmin, Owner |
| `ManageHanavaPrograms` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageHanavaBlueprints` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageDeltaPrograms` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `ManageDeltaBlueprints` | 6 | AreaAdmin, DepartmentLead, MoleculeAdmin, Owner |
| `CreateChoreTypes` | 6 | AreaAdmin, MoleculeAdmin, Owner |
| `ViewShiklutCalendar` | 3 | AreaAdmin, Owner |
| `ViewNOCCalendar` | 3 | AreaAdmin, Owner |
| `SendNotifications` | 3 | Owner |
| `ManageShiklutPrograms` | 3 | AreaAdmin, Owner |
| `ManageShiklutBlueprints` | 3 | AreaAdmin, Owner |
| `ManageNOCPrograms` | 3 | AreaAdmin, Owner |
| `ManageNOCBlueprints` | 3 | AreaAdmin, Owner |
| `ExportData` | 3 | Owner |
| `CreateCompany` | 3 | AreaAdmin, Owner |
| `CanBeAssignedYekev` | 3 | Owner |
| `CanBeAssignedShiklut` | 3 | AreaAdmin, Owner |
| `CanBeAssignedNOC` | 3 | AreaAdmin, Owner |
| `CanBeAssignedMoviltech` | 3 | Owner |
| `CanBeAssignedHanava` | 3 | Owner |
| `CanBeAssignedDelta` | 3 | Owner |
| `AssignShiklutChores` | 3 | AreaAdmin, Owner |
| `AssignNOCChores` | 3 | AreaAdmin, Owner |
| `AssignYekevShifts` | 1 | - |
| `AssignTextShifts` | 1 | - |
| `AssignTechShifts` | 1 | - |
| `AssignMoviltechShifts` | 1 | - |
| `AssignHanavaShifts` | 1 | - |
| `AssignDeltaShifts` | 1 | - |
| `AssignBRShifts` | 1 | - |

## Findings from the 2 dimensions that completed

`[V]` = adversarially refuted. `[ ]` = **single-agent output, NOT independently verified.**

### CRITICAL (4)

- `[ ]` **/Admin/Directors write handlers do no scope check — 99 company-scoped users can assign/revoke/move directors across every company in the deployment**  
  `Pages/Admin/Directors.cshtml.cs:100` — kind=*security* — persona: Lead (מפ"צ) / BRDirector (קב"ר) — company-scoped AssignRoles holder  
  **Failure:** lead.alhut.tzafona@test (Lead, grant scope CompanyId=1) opens /Admin/Directors. The policy handler passes because `GrantAuthorizationHandler` evaluates AssignRoles against the caller's OWN company. The Director dropdown lists every Director in all 22 companies with their email addresses, and the Company dropdown lists all 22 companies. He submits Assign with DirectorUserId = a director from a different molecule and CompanyId = a company he has no relationship to, or clicks Move to relocate an existing Owner-created assignment. Both succeed, are audited as his action, and are invisible to the affected companies' admins.  
  **Why wrong for the user:** A desk-level Lead is shown, and can rewrite, the org-wide director oversight map.  
  **Proposed fix:** Scope both the GET pickers and all three POST handlers: build `var reach = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "AssignRoles")` and (a) filter `AvailableCompanies` / `AvailableDirectors` / `Assignments` to companies in `reach`, (b) reject Assign/Reassign when `!reach.Contains(CompanyId)` (and for Reassign, also when `!reach.Contains(assignment.CompanyId)`), (c) r

- `[ ]` **Removing a grant from a Role Template never revokes it from existing users — the cleanup filter matches a Notes tag that 99.5% of auto-grants do not carry**  
  `Services/GrantService.cs:744` — kind=*security* — persona: Owner tightening a role after an over-granting incident  
  **Failure:** An Owner discovers Leads should not hold `OverrideVacationLimits`, opens /Owner/Hub/RoleTemplates/Edit?id=3 and clicks Remove on that grant (`OnPostRemoveGrantAsync`, Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs:261). The template row is deleted and an audit entry says the grant was removed. Every one of the ~40 existing Leads keeps the `OverrideVacationLimits` Grant row forever: it survives their next login (Pages/Auth/Login.cshtml.cs:335 calls ApplyAutoGrantsAsync, whose Phase-2 filter cannot see the Onboarding-tagged row), it survives the Grants-page back-fill, and no UI action anywhere removes it. The same applies to NARROWING a scope: switching a grant from ExpandToProject to SameAsRole leaves the project-wide row in place and merely adds a company row next to it.  
  **Why wrong for the user:** The Owner believes a permission was revoked org-wide; it was revoked from nobody.  
  **Proposed fix:** Stop identifying template-derived grants by a free-text Notes substring. Add a `SourceRoleTemplateId` column to `Grant` (or at minimum unify both writers on the identical Notes literal and back-fill the 17k existing rows), then have Phase 2 match on that column. Separately, give `IGrantBackfillService` a `ExecuteSurplusRemovalAsync` and wire a button for it on /Owner/Hub/Grants, so the surplus rep

- `[ ]` **Grant assignment handler performs ZERO authorization on target user, grant type, or scope — any AssignGrants holder can grant project-wide power to anyone including themselves**  
  `Pages/Admin/Organization/Grants/Assign.cshtml.cs:166` — kind=*security* — persona: Lead (מפ"צ) / BRDirector (קב"ר) — company-scoped middle seat; 99 users hold AssignGrants in the seeded DB, most at CompanyId scope  
  **Failure:** mgr.alhut.tz@test is a Lead whose AssignGrants row is scoped to Company 1 / JobType 1 with CanGive=0. They open /Admin/Organization/Grants/Assign (policy passes on their own company), and the form itself already offers every user, every project, every area, every molecule and every company in the deployment (Assign.cshtml:65-69 and 120-186, fed by LoadDropdownOptionsAsync's `IgnoreQueryFilters()` queries at lines 207-258). They select themselves, grant type `AdminAccess`, Project = 1, CanGive = checked, submit. A Grant row is created with ProjectId=1 for the caller. `GetAccessibleCompanyIdsForGrantAsync` now returns EVERY company in the project for them, and the unscoped `HasGrantAsync(userId, key)` (GrantService.cs:40-48, used at 113 call sites including `Pages/Admin/Config.cshtml.cs:117   
  **Why wrong for the user:** The grant model exists precisely so a squad leader's authority stops at their desk. This page hands them the whole project.  
  **Proposed fix:** Route the page through `IGrantService.GrantAsync(userId, grantTypeId, scope, grantedByUserId: currentUserId)` so `CanUserGrantAsync` (CanGive + ScopeCovers) runs, instead of `_db.Grants.Add`. Additionally, before that: (a) verify the target user's company is in `GetAccessibleCompanyIdsForGrantAsync(actorId, "AssignGrants")`; (b) verify the requested scope tuple is covered by one of the actor's own

- `[ ]` **Role assignment handler has no target/scope check — any AssignRoles holder can assign the Owner role to themselves; RoleService's documented 'enforced UPSTREAM by the caller' invariant is false**  
  `Pages/Admin/Organization/Roles/Assign.cshtml.cs:152` — kind=*security* — persona: Lead (מפ"צ), BRDirector (קב"ר), DepartmentLead, Director, MoleculeAdmin, AreaAdmin — every template that carries grant 38  
  **Failure:** A Lead at Company 1 opens /Admin/Organization/Roles/Assign, picks themselves in the User dropdown (populated with every active user in the deployment via `_db.Users.IgnoreQueryFilters()` at line 207-212), picks role 'Owner', leaves every scope select empty (the JS marks nothing required for Project scope — Assign.cshtml:178 `'Project': []`), and submits. `ValidateScope` returns null, `AssignRoleAsync` creates the UserRoleAssignment and `ApplyAutoGrantsAsync` writes the whole Owner grant set (RoleTemplateSeed.cs:772-826 — ~100 grants including AdminAccess, SystemConfiguration, AssignGrants/RevokeGrants/AssignRoles, all with `canGive: true`) onto the Lead's account. Because the caller's own AssignRoles grant is company-scoped, nothing in the request is compared against it. The Lead is now Ow  
  **Why wrong for the user:** A comment asserts a security boundary that no code implements — the most dangerous shape of the 'provisioned but never wired' pattern in this codebase.  
  **Proposed fix:** In OnPostAsync, before calling the service: (1) resolve the target user's company and require it to be in `GetAccessibleCompanyIdsForGrantAsync(actorId, "AssignRoles")`; (2) require every non-null scope id in the request to be covered by the actor's own AssignRoles scope (reject Area/Molecule/Project levels the actor does not hold); (3) refuse to assign a template whose grant set is not a subset o


### HIGH (6)

- `[ ]` **Permission Simulator reports GRANTED where production denies, for molecule/area-scoped grants that also carry a JobType — the exact case the tool exists to diagnose**  
  `Services/PermissionSimulatorService.cs:337` — kind=*implementation-bug* — persona: Owner debugging "why can't this Lead assign this Text shift?"  
  **Failure:** Owner selects lead.alhut.tzafona@test, grant key `EditShiftTypes`, target Molecule = 1, target Job Type = 3 (Text). The user's grant is {MoleculeId:1, JobTypeId:1}. The simulator's CASE D matches on molecule alone and renders a green "ACCESS GRANTED" with trace `MoleculeId=1, JobTypeId=1`. Production denies: jobTypeMismatch is true so the molecule branch is skipped, and CASE-G's `grant.JobTypeId == jobTypeId` fails. The Owner concludes the permission model is fine and goes hunting for a bug in the shift-type page that does not exist — or worse, uses the simulator to "prove" a Lead is correctly fenced out of another job type when the simulator would have said GRANTED either way.  
  **Why wrong for the user:** The one tool built to give authoritative answers about the grant model gives the opposite answer from the model.  
  **Proposed fix:** Port the `jobTypeMismatch` precondition into the simulator: compute it once at the top of `EvaluateGrantAsync` and gate CASE C and CASE D on `!jobTypeMismatch`, emitting `SimFailureKind.JobTypeMismatch` when it fires. Better: delete the duplicated evaluator and have `HasGrantWithScopeAsync` accept an optional trace sink, so there is exactly one implementation of the rules — the same duplication al

- `[ ]` **`Grant:AdminAccess` page policies accept an AdminAccess grant at ANY scope, so a company-scoped AdminAccess grant confers full cross-tenant Owner-Hub power**  
  `Authorization/GrantAuthorizationHandler.cs:48` — kind=*design-flaw* — persona: Lead/Manager given "admin over my desk only"  
  **Failure:** An Owner wants a trusted Lead to administer one desk, so on /Owner/Hub/Grants he assigns `AdminAccess` with CompanyId = that desk (the CanUserGrant check is bypassed by his own AdminAccess). The Lead now passes `Grant:AdminAccess` on every Owner-Hub page. He opens /Owner/Hub/RoleTemplates/Edit, adds `AdminAccess` at ExpandToProject to the Employee template, opens /Owner/Hub/Grants and back-fills — every Employee in the deployment becomes a global admin. He can also unlock any account (Pages/Owner/LockedUsers.cshtml.cs:44) and enumerate every user and company via the Simulator.  
  **Why wrong for the user:** "Admin of one desk" is silently the same thing as "admin of everything".  
  **Proposed fix:** Introduce a scope-minimum concept: either (a) have `GrantRequirement` carry a required `GrantScopeLevel` and reject grants narrower than the GrantType's `DefaultScope` for System-category grants, or (b) for the specific set of cross-tenant Owner surfaces, replace the policy check with an explicit `HasGrantWithScopeAsync(userId, "AdminAccess", projectId: <root project>)` so only a project-scoped gr

- `[ ]` **/Admin/Directors is a dead control: DirectorCompany rows are never consulted for director reach, so Revoke removes no access**  
  `Services/DirectorService.cs:71` — kind=*dead-control* — persona: Owner/Director-admin offboarding a director from a company  
  **Failure:** A director moves off the Tzafona desk. The admin opens /Admin/Directors, finds the Tzafona row, clicks Revoke, confirms the danger modal, and gets "Director access revoked". The `DirectorCompany` row is soft-deleted. The director's `DirectorHubAccess` grant is untouched, so he still sees Tzafona in the desk switcher, still receives its notification hub, and still approves its vacation and swap requests via /Requests. Symmetrically, an Assign on this page grants a director nothing — only the tile count on their home dashboard moves.  
  **Why wrong for the user:** An offboarding action reports success and leaves the director with full access to the company they were removed from.  
  **Proposed fix:** Pick one authority. Either make `DirectorCompany` real — have `GetDirectorCompanyIdsAsync` intersect the grant-derived set with active `DirectorCompanies` rows when any exist for that user — or retire /Admin/Directors and redirect it to the grant editor for `DirectorHubAccess`. Leaving a Revoke button that revokes nothing is the worst of the three.

- `[ ]` **Role revoke handler has no revoke-specific grant and no scope check — a Lead can strip the Owner role (and all its grants) from every Owner in the deployment**  
  `Pages/Admin/Organization/Roles/Index.cshtml.cs:141` — kind=*security* — persona: Lead (מפ"צ) or DepartmentLead — any holder of AssignRoles  
  **Failure:** A Lead at one desk opens /Admin/Organization/Roles?ViewMode=assignments, sees every role assignment in the deployment including `test.owner@shifty.test`'s Owner assignment, and clicks Revoke on it. `RemoveRoleAsync` soft-deletes the assignment and `RemoveAutoGrantsAsync` deletes the Owner's ~100 auto-grants. Repeated across the Owner rows, the deployment is left with nobody holding AdminAccess/SystemConfiguration and no way to restore it from the UI — an air-gapped, IIS-hosted system with no console recovery path. The same handler also lets a Lead quietly demote peers in other molecules.  
  **Why wrong for the user:** Visibility==access is the stated principle; here visibility is deployment-wide while the authority check is 'you hold AssignRoles somewhere'.  
  **Proposed fix:** Mirror the grants handler exactly: introduce/require a revoke-capable check (`AssignRoles` at minimum, ideally a dedicated grant), then `HasGrantForCompanyAsync(actorId, key, assignment.User.CompanyId)` plus a scope-coverage check against the assignment's own Area/Molecule/Company, and refuse to revoke an assignment whose template outranks the actor's. Also scope the assignments list to the actor'

- `[ ]` **Role scope form does not require the ancestor levels its template's ExpandTo* auto-grants consume, silently producing all-NULL-scope grants that are global to every unscoped permission check**  
  `Pages/Admin/Organization/Roles/Assign.cshtml.cs:179` — kind=*design-flaw* — persona: Any admin assigning Assigner/Lead/BRDirector/Owner; the resulting holder is the victim/beneficiary  
  **Failure:** An admin assigns the Assigner role to a soldier and fills in only Molecule (the single field the UI marks required). `ApplyAutoGrantsAsync` expands `G(8, 17, ETA)` with `roleScope.AreaId == null`, writing an AssignChores grant with Project/Area/Molecule/Company/Department/JobType all NULL. From then on: every unscoped gate — `HasCalendarEditPermissionAsync` (GrantService.cs:52-56 `|| await HasGrantAsync(userId, "AssignChores")`) and the 113 `HasGrantAsync(userId, key)` call sites such as `Pages/Admin/Config.cshtml.cs:117` — returns true for that user regardless of company, while `GetAccessibleCompanyIdsForGrantAsync` falls into its self-scope branch (GrantService.cs:427-431) and reports only their own company. The soldier is simultaneously over-granted on one code path and under-granted on  
  **Why wrong for the user:** An admin filling every field the UI marks required still ends up creating a permission that means 'everywhere' to half the codebase.  
  **Proposed fix:** Compute the required scope fields from the template's auto-grant ScopeModes (union of the levels any ExpandTo* mode will read), not from ScopeLevel alone, and reject the POST when a needed ancestor is missing. Add ScopeProjectId to the page model and view for Project-level templates. Defensively, make `DetermineEffectiveScope` throw/fail when the requested mode resolves to an all-null scope, and m

- `[ ]` **Revoking one role assignment deletes auto-grants belonging to the user's OTHER role assignments**  
  `Services/GrantService.cs:812` — kind=*data-integrity* — persona: Any user holding two roles (e.g. Lead at Company A and BRDirector at Company B — common for multi-company members)  
  **Failure:** A soldier holds Lead at Company 1 and BRDirector at Company 4. Both templates auto-grant ViewShifts(1), ViewChores(16), ViewDuties(12), ApproveVacations(22), AssignGrants(36), RevokeGrants(37), AssignRoles(38) and ~20 more overlapping types. An admin revokes only the Lead assignment. `RemoveAutoGrantsAsync` deletes every auto-grant whose GrantTypeId appears in the Lead template — including the Company-4 rows created by the BRDirector assignment. The BRDirector assignment stays `IsActive = true` in the UI and on the Roles page, but the user silently loses the ability to approve vacations or view shifts at Company 4, with no error and no audit entry naming those grants. Recovery requires revoking and re-assigning the surviving role.  
  **Why wrong for the user:** Removing one hat should not silently strip the powers that came with a different hat the user still wears.  
  **Proposed fix:** Filter the delete by the same discriminator ApplyAutoGrantsAsync writes (`g.Notes.Contains($"role: {roleTemplate.Key}")`) AND by the revoked assignment's scope tuple; better, add a nullable `SourceUserRoleAssignmentId` FK to Grant so auto-grants are deleted by provenance rather than by string matching. After deletion, re-run ApplyAutoGrants for the user's remaining active assignments so overlappin


### MEDIUM (8)

- `[ ]` **Owner Hub unlock has no scope check while the identical action in Admin › Users does**  
  `Pages/Owner/LockedUsers.cshtml.cs:42` — kind=*security* — persona: Company-scoped AdminAccess holder (see the AdminAccess-scope finding)  
  **Failure:** An account with a company-scoped `AdminAccess` grant (reachable per the AdminAccess-scope finding) opens /Owner/LockedUsers, sees the Owner account of a different desk locked out after a brute-force attempt, reads the attacker's source IP, and clicks Unlock — resetting `FailedLoginAttempts` to 0 and clearing `LockoutEnd`, which hands the in-progress brute force a fresh budget of 10 attempts. Nothing in the handler consults the caller's reach over `user.CompanyId`.  
  **Why wrong for the user:** Unlocking someone else's tenant's account is a one-click, unchecked action on a page whose sibling implementation gets it right.  
  **Proposed fix:** Mirror the Users-page check: resolve the target, then require `HasGrantAsync(currentUserId, "AdminAccess")` at project scope OR `HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", user.CompanyId)`; scope `LoadLockedUsersAsync` to `GetAccessibleCompanyIdsForGrantAsync(currentUserId, "EditCompanyUsers")` for non-project-scoped callers so the IP/email disclosure is bounded the same way.

- `[ ]` **Deactivating a system Role Template from the Edit page silently zeroes grant provisioning for every user created afterwards**  
  `Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs:85` — kind=*data-integrity* — persona: Owner tidying up the template list  
  **Failure:** An Owner unchecks Active on the `Employee` system template to hide it from a picker. From that moment every user created via Admin › Users, the user API (Services/Api/UserApiService.cs:183) and company transfer (Services/UserCompanyTransferService.cs:199) is created with ZERO grants — the create flow reports success. Those users can log in and see an empty app; nobody connects it to a checkbox on a different page, and the fix (re-activate + run back-fill) is undiscoverable.  
  **Why wrong for the user:** A cosmetic toggle silently breaks account provisioning with no feedback anywhere.  
  **Proposed fix:** Wrap the IsActive control and the assignment in `Edit.cshtml.cs` in the same `if (!template.IsSystem)` guard already used for Key/ScopeLevel, and hide the checkbox in the view for system templates. Independently, make `AssignRoleTemplateGrantsAsync` returning 0 for a non-null template key a hard error the caller surfaces, rather than an indistinguishable silent 0.

- `[ ]` **Two divergent Role-Template grant editors: the Owner-Hub Grants page creates job-type-UNRESTRICTED grants where the Edit page creates job-type-scoped ones**  
  `Pages/Owner/Hub/Grants.cshtml.cs:599` — kind=*cross-feature* — persona: Owner adding a grant to the Lead template  
  **Failure:** An Owner adds `AssignShifts` at ExpandToMolecule to the Lead template. If he does it from /Owner/Hub/Grants, every Lead is provisioned a molecule-wide grant with `JobTypeId = null` — meaning an Alhut Lead can assign Text and Techno shifts across the whole molecule. If he does the identical thing from /Owner/Hub/RoleTemplates/Edit with "Use own job type" checked (the seed's convention for all 22+ ETM Lead grants, per the comment at Services/GrantService.cs:216-225), each Lead gets a grant fenced to their own job type. Same intent, two pages, opposite blast radius, no warning on either.  
  **Why wrong for the user:** Which of two Owner pages you happen to use decides whether a role is job-type-fenced.  
  **Proposed fix:** Delete the role-template grant handlers from `Grants.cshtml.cs` (`OnPostAddRoleTemplateGrantAsync`/`OnPostUpdateRoleTemplateGrantAsync`/`OnPostRemoveRoleTemplateGrantAsync`) and link that tab to `/Owner/Hub/RoleTemplates/Edit`, which is the complete editor. If both must stay, add `UseOwnJobType`/`TargetJobTypeId` to the Grants DTO and align the duplicate check to include `TargetJobTypeId`.

- `[ ]` **Locked Users' "recently locked" section is keyed to a threshold of 5 while the login lockout fires at 10, so it can never populate and in-progress brute force stays invisible**  
  `Pages/Owner/LockedUsers.cshtml.cs:113` — kind=*implementation-bug* — persona: Owner investigating a suspected brute-force attempt  
  **Failure:** An attacker grinds passwords against 30 accounts, stopping at 9 attempts each to stay under the lock. The Owner opens /Owner/Locked Users to investigate, sees an empty page ("no locked users"), and concludes nothing is happening — even though the `FailedLoginAttempts` counters the page claims to report are sitting at 9. The `>= 5` literal makes the code read as if halfway-there accounts are covered.  
  **Why wrong for the user:** The security-monitoring page shows all-clear during an active credential-stuffing run.  
  **Proposed fix:** Split the two concepts. Keep the expired-lockout list keyed on `LockoutEnd`, and add a genuine "elevated failed attempts" list `.Where(u => u.FailedLoginAttempts >= 5 && u.LockoutEnd == null && u.LastLoginAttempt > hourAgo)`. Derive both thresholds from a single shared constant with the value used in Login.cshtml.cs so they cannot drift again.

- `[ ]` **Grants overview lists every user and every grant in the deployment to any ViewGrants holder, though ViewGrants is company-scoped for Lead/BRDirector/DepartmentLead**  
  `Pages/Admin/Organization/Grants/Index.cshtml.cs:14` — kind=*security* — persona: DepartmentLead / Lead (מפ"צ) / BRDirector (קב"ר) — company-scoped ViewGrants (99 holders in e2e.db)  
  **Failure:** A DepartmentLead at one desk opens /Admin/Organization/Grants?ViewMode=grants and reads the complete permission map of all 543 users across all 22 companies — including which accounts hold AdminAccess/SystemConfiguration, their exact scopes, who granted them and when. That is the reconnaissance step for the escalation in the Assign handler above (it also hands them the grant ids used by the Revoke form and the user ids used by the Assign form). The 'must not see the permission map' protection applied to Assigner is defeated for every seat one rank up.  
  **Why wrong for the user:** A company-scoped grant is being read as a deployment-wide one because the page never asks the grant what its scope is.  
  **Proposed fix:** Scope both queries to `GetAccessibleCompanyIdsForGrantAsync(actorId, "ViewGrants")` (users filtered by `accessibleCompanyIds.Contains(u.CompanyId)`, grants by target user's company), keeping IgnoreQueryFilters only to cross the tenant filter, and update the SECURITY-AUDITED comment to state the restoring WHERE. Project-scoped holders (Owner) then still see everything, which is the actual intent.

- `[ ]` **Grant and role changes are audited under the ACTOR's company, so the target company's admins can never see privilege changes made inside their own company**  
  `Services/AuditLogService.cs:95` — kind=*security* — persona: Company admin / Director reviewing their own desk's audit trail  
  **Failure:** A Lead whose session tenant is Company 1 grants AdminAccess to a user in Company 7. The audit row is written with CompanyId=1. The Company 7 Director opens /Admin/AuditLog, sees nothing, and has no way to discover that someone outside their desk created an administrator inside it. Post-incident review of Company 7 comes back clean.  
  **Why wrong for the user:** The one action whose audit trail matters most is the one that lands in the wrong company's log.  
  **Proposed fix:** Use the explicit-company overload in all four handlers, writing one row under the TARGET user's company (and optionally a second under the actor's), so the affected desk sees the change. Alternatively give the audit viewer a scoped cross-company mode driven by `GetAccessibleCompanyIdsForGrantAsync(actorId, "ViewAuditLog")`.

- `[ ]` **Grant assign/revoke audit entries omit the scope and CanGive — the log cannot distinguish a desk-scoped grant from a project-wide one**  
  `Pages/Admin/Organization/Grants/Assign.cshtml.cs:190` — kind=*security* — persona: Owner or auditor reconstructing how a user obtained project-wide power  
  **Failure:** After the escalation above, an Owner reviews the audit log and finds `Assigned grant 'AdminAccess' to user 'Ploni'` — indistinguishable from a routine desk-level grant. Because the Grant row itself was subsequently revoked (leaving only `Revoked grant 'AdminAccess' from user 'Ploni'`, equally scope-less), there is no surviving record anywhere that the grant was project-wide or carried delegation rights. The blast radius of the incident cannot be established.  
  **Why wrong for the user:** An audit entry that omits the scope of a permission change records that something happened but not what.  
  **Proposed fix:** Pass the same `details` string GrantService.cs:647-649 builds (full scope tuple plus CanOwn/CanGive) from both page handlers; do the same for RoleAssigned/RoleRevoked in the Roles pages, which today log only the template key and user name.

- `[ ]` **Deprecated grant types are still offered in the grant-assignment catalog and can be granted, producing permissions that do nothing**  
  `Pages/Admin/Organization/Grants/Assign.cshtml.cs:214` — kind=*dead-control* — persona: BRDirector (קב"ר) trying to give a subordinate shift-assignment rights  
  **Failure:** A BRDirector needs a soldier to be able to assign BR shifts. The picker lists 'Assign BR Shifts' (a name that matches the task exactly) above the unified 'Assign Shifts'. They grant it, the page reports success, the grant appears in the Grants list with an Own badge — and the soldier still cannot assign anything, because `GetGrantTypeByKeyAsync` resolves by key with `gt.IsActive` and no production code looks up the deprecated keys any more. The admin has no signal that the permission is inert and will most likely conclude the shift board is broken.  
  **Why wrong for the user:** The catalog advertises eight permissions that were deliberately retired; picking one fails silently.  
  **Proposed fix:** Add `&& !gt.IsDeprecated` to both catalog queries (Assign.cshtml.cs:216 and Index.cshtml.cs:71), or set `IsActive = false` alongside `IsDeprecated = true` in AssignShiftsCollapse.HealAsync. Keep deprecated rows visible in the Index listing (for existing grants) but never in the assignable picker.


### LOW (4)

- `[ ]` **Permission Simulator returns raw exception messages to the browser**  
  `Pages/Owner/Hub/PermissionSimulator.cshtml.cs:248` — kind=*security* — persona: Any AdminAccess-policy holder (incl. a company-scoped one)  
  **Failure:** A malformed simulate request (e.g. `ScopeMode` outside the enum reaching `DetermineEffectiveScope`, which throws `ArgumentOutOfRangeException` at Services/PermissionSimulatorService.cs:427, or an EF/SQLite failure) returns the raw .NET message — including entity, column or connection details — to the client, while leaving no server-side log line for the Owner to investigate.  
  **Why wrong for the user:** Internal detail leaks outward and the diagnostic detail the Owner actually needs is thrown away.  
  **Proposed fix:** Inject `ILogger<PermissionSimulatorModel>`, log `ex` with the request parameters, and return a generic `"An error occurred"` payload, matching the convention already used by the other Owner-Hub handlers.

- `[ ]` **Grants index renders Assign and Revoke controls to ViewGrants-only holders, both of which dead-end in a 403**  
  `Pages/Admin/Organization/Grants/Index.cshtml:173` — kind=*dead-control* — persona: DepartmentLead (מפקד מחלקה טכנית) — holds ViewGrants but neither AssignGrants nor RevokeGrants  
  **Failure:** A DepartmentLead opens the Grants page, clicks 'Assign Grant' next to a soldier and lands on a bare 403; or confirms the danger-styled Revoke modal and gets an empty Forbid() response with no message, leaving them unsure whether the grant was revoked. They cannot tell the difference between 'you may not do this' and 'the app is broken'.  
  **Why wrong for the user:** Offering a control the caller is structurally forbidden to use reads as a bug, not a boundary.  
  **Proposed fix:** Wrap the Assign links in `<require-grant key="AssignGrants">` and the Revoke form in `<require-grant key="RevokeGrants">`, matching the app's stated visibility==access principle, and return a localized error page rather than a bare Forbid() when the handler does reject.

- `[ ]` **Grant revocation and all role changes notify nobody, while grant assignment does — users discover lost permissions only by hitting 403s**  
  `Pages/Admin/Organization/Grants/Index.cshtml.cs:173` — kind=*ux-discoverability* — persona: Lead (מפ"צ) whose approval or assignment rights were revoked or re-scoped  
  **Failure:** A Lead's ApproveVacations grant is revoked, or their Lead role is re-scoped from Molecule to Company. They receive nothing. The next time a soldier's vacation request needs approval they open the request, click Approve and get a permission error — after the soldier has already been told the request is with their commander. The same silence applies to being GIVEN a role: a newly promoted BRDirector is never told their new admin surfaces exist.  
  **Why wrong for the user:** Permission changes are account changes; the app already decided those warrant a notification and then applied that decision to only one of the four paths.  
  **Proposed fix:** Emit `NotificationType.GrantChanged` (Account category, personallyActionable) from all four handlers — grant revoke, role assign, role revoke — mirroring Assign.cshtml.cs:193-197, with a message naming the permission/role and whether it was added or removed.

- `[ ]` **Grant assign accepts inactive grant types that the picker never offers, creating an inert grant that can silently become live later**  
  `Pages/Admin/Organization/Grants/Assign.cshtml.cs:134` — kind=*implementation-bug* — persona: Any AssignGrants holder crafting a POST, or any admin replaying a stale form  
  **Failure:** An admin submits a stale form (or a crafted POST) naming a deactivated grant type. The row is created, shows in the Grants list with an Own badge and a scope, and reads as a live permission — but `GetGrantTypeByKeyAsync` filters on `gt.Key == key && gt.IsActive` (GrantService.cs:319-320), so it grants nothing. If that grant type is ever re-activated, the user instantly gains the permission with no new audit event and no notification, at whatever scope was recorded months earlier.  
  **Why wrong for the user:** A permission row that means nothing today and everything tomorrow is worse than an error message.  
  **Proposed fix:** Add `|| !grantType.IsActive` (and `|| grantType.IsDeprecated`) to the rejection at line 135, matching GrantService.cs:593.


## Not covered by this pass

- `GrantService` core scope comparison (`HasGrantWithScopeAsync`) — **never audited**.
- Systematic dead-grant / unread-scope-parameter sweep — **never ran** (this is how `ViewAllShifts`
  was found in C1; more instances are likely).
- RoleTemplate coherence + auto-grant lifecycle — **never ran**.
- Nav/policy parity and the no-`[Authorize]`-attribute sweep — **never ran**.
- App-wide IDOR sweep outside Pages/Calendar — **never ran**.
- Multi-company switcher as an authorization surface — **never ran**.
- Nothing in this cluster was click-verified at runtime; no test was run against any finding.
