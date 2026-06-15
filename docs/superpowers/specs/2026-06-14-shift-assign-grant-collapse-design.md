# Shift-Assignment Grant Collapse (8 → 1 `AssignShifts`) — Design Spec

**Date:** 2026-06-14 · **Branch:** `dev` · **Status:** Design approved (with architect + UI-designer review folded in); pending implementation plan.

**Paired sub-project (BOTH required — "we need both done"):** [Category-based shift eligibility](2026-06-14-category-based-shift-eligibility-design.md) (3b). This spec (3a) is the **authorization** fix; 3b is the **candidate-filtering** fix. They are decoupled for delivery but neither is "done" until both ship.

---

## 1. Problem

A pre-deploy QA sweep (finding #3 / I1) found shift-assignment authorization hard-codes **4 of 8** `Assign*Shifts` grant keys:

- Checked: `AssignAlhutShifts`(3), `AssignTextShifts`(4), `AssignBRShifts`(5), `AssignTechShifts`(6)
- **Never checked:** `AssignHanavaShifts`(81), `AssignDeltaShifts`(82), `AssignYekevShifts`(83), `AssignMoviltechShifts`(84)

The unchecked four are assigned to role templates 7/9/10/11 and map to **live Tech shift types** (Shikma molecule). A manager whose only assign authority is one of them is **Forbidden on all 16 shift-mutation handlers** plus the pre-existing `AssignEmployee` handler — a functional lockout.

**Root cause:** the 8 keys conflate two axes — job-type authority (Alhut/Text/BR, which already scope by `JobTypeId`) and tech shift-family authority (tech shifts have `JobTypeId == null`, so per-family *keys* were invented). In the OR-chain the keys add **no enforcement value**; the real gate is the grant's org scope + (for workforce) the `JobTypeId` scope on the grant.

**Owner decision:** assignment *permission* needs **no** per-job-type / per-tech-family granularity. (Job-type relevance belongs to candidate *filtering* = sub-project 3b.) Therefore permission collapses to **one job-type-agnostic capability gated by org scope.**

## 2. Decision

Introduce a single **`AssignShifts`** grant (job-type-agnostic, org-scoped). Replace every consumer of the 8 keys with it. Migrate existing user/template data. Deprecate the 8 (retained in DB for ID stability + audit history; hidden from the admin catalog).

Verified by the architecture review: **nothing reads the 8 grants' `JobTypeId` for non-permission purposes** (accessible-company resolution, scope filtering, distribution, reporting all ignore it), so dropping the job-type pin is side-effect-free beyond the intended permission widening.

## 3. Consumers to update — **9 sites across 4 files** (architect sweep; verify line numbers at implementation)

> Missing any of these re-creates the bug class. Consumers #5–#7 are the dangerous omissions: they feed **note-writing reach**, so collapsing only the calendar sites and then deleting old grant rows would **silently shrink every manager's note scope**.

1. `Pages/Calendar/Table.cshtml.cs:~804` — `OnPostAssignEmployeeAsync` OR-chain (+ the `probeShiftJobTypeId` lookup at ~:798 that exists only to feed it).
2. `Pages/Calendar/Table.cshtml.cs:~2842` — `CanAssignForShiftScopeAsync` OR-chain (the workhorse; ~15 handler callers — collapsing this fixes all 15).
3. `Pages/Calendar/Shifts.cshtml.cs:~239` — `CanEdit` OR-chain.
4. `Pages/Calendar/Shifts.cshtml.cs:~252` — `RequiredGrantNameKeys` (read-only banner) + its `GetGrantNameKeysAsync(...)` call → `"AssignShifts"`.
5. `Services/GrantService.cs:~53` — `HasCalendarEditPermissionAsync` (feeds `HasCalendarNotePermissionAsync`).
6. `Services/GrantService.cs:~113` — `HasAnyAssignGrantAsync` (manager-tier gate inside `CanReachUserForNoteAsync`).
7. `Services/GrantService.cs:~94` — the `assignGrantKeys` `string[]` in `CanReachUserForNoteAsync` (consumed by `Pages/Api/Calendar/QuickAddTextEntry.cshtml.cs` + `DeleteTextEntry.cshtml.cs`).
8. `Pages/Calendar/Month.cshtml:~217,336,397` — `<require-grant key="AssignAlhutShifts,AssignTextShifts,AssignBRShifts,AssignTechShifts" mode="any">` UI gates → `key="AssignShifts"`.
9. Seeds (top-level source only): `Data/SeedData/GrantTypeSeed.cs` + `Data/SeedData/RoleTemplateSeed.cs`.

## 4. The new grant

- `AssignShifts` appended at the **next sequential id** (verify max at implementation; ≈ **137**). `Category = Shift`, `DefaultScope = Molecule`, `IsSystem = true`.
- **Never inserted mid-list** (seed uses sequential `id++`; `RoleTemplateSeed` references grants by numeric id).
- `DefaultScope = Molecule` matches the dominant real scope of the 8 (mix of Company/Molecule/Department, assigned ETM-or-broader in templates) and the `HasGrantWithScopeAsync` cascade (molecule grant → any company in molecule).

## 5. Check shape (all code consumers)

```
AdminAccess short-circuit
  || await _grantService.HasGrantWithScopeAsync(userId, "AssignShifts", <TARGET scope>)
```

- **No `jobTypeId` param** (permission is job-type-agnostic). Delete the per-handler `ShiftType → JobTypeId` lookups done *solely* for the auth check — this also resolves efficiency finding #8 (the 4-query fan-out) and removes ~4 redundant DB round-trips.
- `<TARGET scope>` = the **target** shift/instance's real company/molecule (NOT the caller's), per Fix #5.

## 6. Fix #5 folded in (do not defer)

For molecule/area-scoped shift types (`ShiftType.CompanyId == null`), `GetEffectiveCompanyId(callerCompany)` returns the **caller's** company, which can wrongly authorize a cross-molecule target. Fix: pass the target's **real `moleculeId`** (from `ShiftType.MoleculeId` / the instance) to `HasGrantWithScopeAsync`, falling back to `companyId` only for company-scoped shift types. Apply in `EnsureShiftInstance`/`CreateShiftInstance`, `OnPostAssignEmployeeAsync`, and the `CanAssignForShiftScopeAsync` signature.

## 7. Migration — idempotent startup self-heal (mirror the existing F2 block; place **before** the `RepairUserGrants` passes, ~`Program.cs:890`)

1. **Template-mapping removal (mandatory):** explicitly delete the 8 old `RoleTemplateGrants` rows for templates 2/3/5/7/9/10/11. The seed reconciliation only *inserts* and *updates* mappings — it **never deletes** de-seeded ones, so removing the lines from `RoleTemplateSeed` alone does nothing on existing DBs.
2. **User-grant heal (self-contained):** for every user holding any of the 8 old grants, create `AssignShifts` at each distinct org-scope tuple (Company/Molecule/Area/Project) they held, with `JobTypeId` forced **null**, deduped to one row per scope tuple. Then delete the 8 old user-grant rows. Do **not** rely on the existing `templateIdsToRepair = {2,3,7}` pass — it omits 5/9/10/11.
3. **Idempotency:** guard with `if (staleRows.Count > 0)`; steady-state no-ops. Dedup the new rows against existing `AssignShifts` rows (scope-tuple key).

## 8. Deprecation + admin-catalog hiding (owner approved)

- The 8 `GrantType` rows (3,4,5,6,81–84) are **retained** (ID stability + audit history), removed only from templates / checks / user-rows.
- Add an **`IsDeprecated`** flag to `GrantType` (model + migration); set the 8 deprecated via an **explicit update block** (the GrantType seed only inserts missing keys; it never updates existing rows).
- Grant-admin catalog (`Owner/Hub/Grants` et al.): render deprecated grants under a **default-off "Show deprecated"** toggle, badge them **"Deprecated"**, and **block new assignment**.
- "Deprecate" here keeps rows `IsActive = true` + `IsDeprecated = true`. Do **not** flip `IsActive = false` (the seed won't apply it, and `GetGrantTypeByKeyAsync` filters `IsActive`, which would hard-false any lingering reference).

## 9. Seed / generated artifacts

- Edit **top-level** `GrantTypeSeed.cs` + `RoleTemplateSeed.cs` only. **Do NOT hand-edit `FinalProductPublish/**`** — it is generated; regenerate via `scripts/Update-FinalProductPublish.ps1` after. *(Correction to the architect review, which suggested editing the FinalProductPublish mirror directly.)*
- `RoleTemplateSeed`: replace each affected template's 8-key assignments with a single `AssignShifts` at that template's scope; fix per-template count comments.

## 10. resx

Add `Grant_AssignShifts` + `Grant_AssignShifts_Desc` to **both** `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`. Old keys' resx retained (deprecated types still render in the catalog).

## 11. Tests

- `RoleTemplateAutoGrantTests` InlineData: recompute each affected template's total (its N `Assign*` rows → 1).
- `GrantScopeResolutionTests` / `AuthorizationDenialTests`: update old-key tests (decide: keep as "deprecated key returns false" or replace with `AssignShifts`).
- **New regressions:** (a) a former tech-only-assign holder (Assigner/AreaAdmin) can now assign; (b) migration idempotency (run self-heal twice → stable row counts); (c) **note-writing reach preserved** for managers after migration (guards consumers #5/#6/#7); (d) Fix #5 — cross-molecule target rejected.
- `GrantServiceTests` grant-count `HaveCount(N)` → 137.

## 12. Docs / MEMORY / checklist

Follow `grant_change_checklist.md` fully: seed, OR chains, resx, stale count comments (`Owner/Hub/Grants`, `SystemHealth`, `RoleTemplateSeed` header, `Program.cs` godmode comment), `GrantServiceTests` counts, docs, and the `MEMORY.md` grant-count note (→ 137 grant types; one active assign grant).

## 13. Non-goals (explicitly OUT — handled by 3b)

- **No change to candidate/eligibility filtering** (who appears in assign dropdowns/autocomplete). This spec does not touch `GetUsersForCalendarAsync` / `GetEligibleUsersForShiftTypeAsync` / quick-entry. That is sub-project 3b and is a **required follow-up**.

## 14. Risks

- **Note-writing regression** if any of consumers #5/#6/#7 are missed → covered by test 11(c).
- **Migration ordering** relative to repair passes → must precede them (§7).
- **Grant-count comment drift** → grant-change checklist (§12).
