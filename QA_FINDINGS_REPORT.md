# ShiftManager — Pre-Deployment E2E QA Report

**Date:** 2026-06-14 · **Target:** http://localhost:5000 (Development, live seeded DB) · **Framework:** Playwright (JS) on the existing `qa-automation/` harness
**Scope:** Active, aggressive discovery — new tests written and executed to break the app before tomorrow's deploy.

---

## ⛔ Executive verdict: DO NOT DEPLOY without addressing P0s

Two **P0** privilege-escalation defects and a cluster of **P1** authorization gaps were found, plus one **P0-functional** break in the *uncommitted* vacation code. All critical findings were **empirically reproduced** end-to-end (not just static analysis). The dominant issue is a single, repeating **bug class**, which means the fix is systemic and well-defined.

| ID | Sev | One-line | Verified |
|----|-----|----------|----------|
| **F1** | **P0** | Any authenticated user (proven: Trainee) can **revoke any user's grants, cross-tenant** | ✅ live (grant row deleted) |
| **F6** | **P0** (latent) | `UpdateDates` vacation handler has **no ownership/grant check**; cross-tenant; currently masked by F5 | ✅ code + runtime |
| **F5** | **P1** func | **Shorten-vacation is 100% broken** (nested DB transaction) — in-flight code | ✅ live (400 for owner) |
| **F2** | **P1** | Every user can view the **org hierarchy + full grant-assignment table** (permission map) | ✅ live (148 rows as Trainee) |
| **F7** | **P1**×15 | `Calendar/Table` shift mutations gated by `ManagerHomeAccess`, not dedicated `Assign*Shifts` | ⚠️ static (pattern proven by F1) |
| **F8** | **P1**×8 | `Owner/Programs`+`MasterPrograms` gated by `ManagerHomeAccess`, not program grants | ⚠️ static |
| **F9** | **P1** | `Admin/EditProfile` avatar-delete: cross-tenant, no `EditCompanyUsers` check | ⚠️ static |
| **F3** | P2 | `nogrants@test` fixture is **not** grant-less (22 grants) — invalidates negative tests | ✅ DB |
| **F10** | P2 | Settings edit via VIEW grant; global OnDutyType via `ManagerHomeAccess`; AreaPalette scope | ⚠️ static |
| **F4** | P3 | Divergent post-login landing (Owner→/Home, others→/) | ✅ observed |

> "Verified live" = reproduced through the browser/HTTP against the running app + DB confirmation. "Static" = found by code audit (5-agent workflow) using the *same* pattern that F1 proved live; high confidence, not individually reproduced due to time.

---

## The root-cause bug class (read this first)

> **A Razor Page declares a class-level `[Authorize(Policy="Grant:X")]` where X is a view-level or broadly-held grant, but a state-mutating POST handler on that page performs NO stronger per-handler grant/ownership/scope check. Anyone who can VIEW the page can EXECUTE its destructive actions — frequently cross-tenant because the target is loaded via `IgnoreQueryFilters()` by a raw integer id.**

The codebase already implements the **correct** pattern in many places — `Pages/Admin/Users.cshtml.cs` (every handler re-checks `AdminAccess`/`EditCompanyUsers` with scope), `Owner/Blueprints` (per-handler `CreateShiftTypes`/`EditShiftTypes`), `Admin/Organization/Index` (per-handler `ManageHierarchy` scope), `Grants/Assign` (`AssignGrants`), `Companies` (`EditCompany`). The findings below are handlers that **regressed from, or never adopted,** that pattern. The dedicated grants they *should* enforce already exist in `GrantTypeSeed.cs` (`RevokeGrants`, `AssignAlhutShifts`, `CreateShiftPrograms`, `EditCompanyUsers`, `ApproveVacations`, …).

---

## P0 — Deployment blockers

### F1 — Cross-tenant mass grant revocation by any authenticated user
- **Where:** `Pages/Admin/Organization/Grants/Index.cshtml.cs` → `OnPostRevokeAsync` (`POST /Admin/Organization/Grants?handler=Revoke`)
- **Gate:** class-level `[Authorize(Policy="Grant:ViewGrants")]`. `ViewGrants` is held by **149/150 users** (seeded into base templates — see F2). Handler does `_db.Grants.Remove(grant)` after loading via `IgnoreQueryFilters()` (any company), with **no** per-handler check. A dedicated `RevokeGrants` grant exists but is never enforced.
- **Proof (reproduced):** Logged in as `trainee.alhut@test` (Company 1, no admin grants). Harvested a real antiforgery token, POSTed `id=1380` (a `ViewChores` grant belonging to `emp.hir.alhut@test`, **Company 2**). Response: `302 → ?FilterUserId=31` (success branch). WAL-applied DB snapshot: **grant #1380 deleted.** Script: `qa-automation/exploit-revoke.js`.
- **Impact:** Any user can strip anyone's grants in any company — including revoking an Owner's `AdminAccess`, or self-escalation games. Catastrophic, trivially scriptable.
- **Fix:** Add per-handler `await _grantService.HasGrantForCompanyAsync(userId, "RevokeGrants", grant.CompanyId)` (or `AssignGrants`) → `Forbid()` if false; and verify the target grant's company is within the caller's scope. Mirror `Grants/Assign.cshtml.cs`.

### F6 — `UpdateDates` vacation handler has no authorization (latent cross-tenant tamper)
- **Where:** `Pages/Api/TimeOffRequest.cshtml.cs` → `OnPostUpdateDatesAsync`; service `VacationApprovalService.UpdateRequestDatesAsync` (`cs:847-908`).
- **Gate:** `[Authorize]` only (every authenticated user). Loads the request via `IgnoreQueryFilters()`. **No ownership check, no `ApproveVacations` check** — `actorUserId` is used only in log/audit strings. The **sibling** `CancelRequestAsync` (`cs:797`) correctly enforces `if (request.UserId != userId) return NotYourRequest` — proving the check was simply omitted here.
- **Proof:** A base employee in **Company 2** POSTed `UpdateDates` against a **Company-1** approved request and **reached the mutation transaction** (failure came from the post-guard `catch`, line 906 — i.e., authorization did not stop them). A persisted write is currently prevented only by F5's crash. Script: `qa-automation/exploit-updatedates.js`.
- **Impact:** **The moment F5 is fixed, this becomes a live cross-tenant data-tamper P0** — any user can shorten anyone's approved leave in any company and tear down their materialised HOME coverage. Must be fixed *together with* F5.
- **Fix:** At handler/service entry: `if (req.UserId != actorUserId && !await _grantService.HasGrantForCompanyAsync(actorUserId, "ApproveVacations", req.CompanyId)) return Forbid/NotYourRequest;`

---

## P1 — Major

### F5 — Shorten-approved-vacation is 100% broken (in-flight code)
- **Where:** `VacationApprovalService.UpdateRequestDatesAsync` (`cs:872` opens a transaction) → `HomeMaterialiserService.SyncMaterialisedHomeRowsAsync` (`cs:42` opens **another** transaction on the same connection).
- **Symptom:** `System.InvalidOperationException: The connection is already in a transaction and cannot participate in another transaction.` → caught at `VacationApprovalService.cs:906` → `400 {"success":false,"message":"VacationApproval_Error"}` for **every** caller (owner included).
- **Proof:** Owner shortening own approved request → 400 + the stacktrace above in `App_Data/logs`. Spec: `tests/vacation-shorten-dates.spec.js` (F5 test).
- **Fix:** Don't begin a transaction in `UpdateRequestDatesAsync` when the materialiser manages its own, or have the materialiser enlist in the ambient transaction (pass the existing `IDbContextTransaction`/use a single `BeginTransaction` at the outermost layer). This is in **uncommitted** code — fix before merge.

### F2 — Permission-map & hierarchy disclosure to all users
- **Where:** `/Admin/Organization`, `/Admin/Organization/Hierarchy`, `/Admin/Organization/Grants`. Root cause: `ViewGrants`(35) + `ViewHierarchy`(39) are seeded into **base templates** Employee(1)/Trainee(12)/Assigner(8) in `RoleTemplateSeed.cs` (lines 220-221, 244-245, 268-269) → **149/150 users hold them.**
- **Proof:** `trainee.alhut@test` / `emp.tz.alhut@test` / `nogrants@test` each load the **Manage Grants** page with the full **148-row grant-assignment table** (the whole permission map) + the org tree, content ≈ identical to the Owner's. Script: `qa-automation/verify-org-access.js`; spec: `tests/security-grant-handler-authz.spec.js`.
- **Impact:** Information disclosure of the entire org structure and every user's permissions to any account, incl. trainees. Also the enabler for F1 (ViewGrants is the class gate the revoke handler relies on).
- **Fix:** Remove `ViewGrants`/`ViewHierarchy` from base templates (keep on manager+), **and** decouple the revoke handler from `ViewGrants` (F1). If org-tree viewing is intended for all staff, gate `/Grants` separately from `/Hierarchy`.

### F7 — `Calendar/Table` shift-mutation handlers under-gated (15 handlers)
- **Where:** `Pages/Calendar/Table.cshtml.cs`. Class gate `[Authorize(Policy="Grant:ManagerHomeAccess")]` (~59% users). Only `OnPostAssignEmployeeAsync` enforces the dedicated `Assign{Alhut,Text,BR,Tech}Shifts` grants ("FINDING-002 FIX"). The other 15 mutators — `AssignUserToSlot`, `ChangeUser`, `UnassignEmployee`, `ClearAssignment`, `UpdateShiftStaffing`, `DeleteShiftInstance`, `EnsureShiftInstance`, `CreateShiftInstance`, `FillRange` (up to 730 dates), `AddTrainee`, `RemoveTrainee`, `DetachInstance`, `ResetInstanceToProgram`, `UpdateShiftMetadata`, `CreateCustomShiftType` — do not. Most use `IgnoreQueryFilters()` + raw `AssignmentId`/`ShiftInstanceId` → cross-tenant.
- **Confidence:** static (5-agent audit). Same class proven live by F1. **Recommend reproducing + fixing as a batch** (apply the `OnPostAssignEmployeeAsync` check to all siblings).

### F8 — `Owner/Programs` + `MasterPrograms` under-gated (8 handlers)
- Class gate `ManagerHomeAccess`; dedicated `CreateShiftPrograms`/`EditShiftPrograms`/`DeleteShiftPrograms` not enforced. Role-template-9 holders (ManagerHomeAccess, no program grants) can create/edit/delete programs and bulk-generate/overwrite the roster. Tenant-scoped. Sibling `Blueprints` does it right.

### F9 — `Admin/EditProfile` avatar deletion cross-tenant
- `OnPostDeleteAvatarAsync` gated only by `ManagerHomeAccess`; every sibling uses `CanEditTargetAsync → EditCompanyUsers`. Any base manager can delete any user's avatar in any company. Lower impact (cosmetic), hence P1.

---

## P2 / P3

- **F10a** `Admin/Settings` `OnPostAsync` edits Area/Molecule/Company work-hour/rest-hour limits gated by the **VIEW** grant `ViewSettings`; dedicated `EditCompanySettings`/`EditMoleculeSettings` unchecked; no `SelectedId` scope check. (Mitigated in the seeded distribution because view+edit grants co-occur.)
- **F10b** `Admin/Config` Add/Delete OnDutyType mutate the **global** `OnDutyTypeConfig` under `ManagerHomeAccess`, bypassing `ManageOnDutyTypes` (which the proper `DutyTypes` page enforces).
- **F10c** `AreaPalette` Save/Reset accept arbitrary `AreaId` with no per-area scope check (cosmetic calendar colors).
- **F3** `nogrants@test` is not grant-less (RoleTemplateId=1, 22 grants) — `HandleSpecialCases` nulls the template but a backfill re-applies it. **Any test/assumption using it as a zero-grant baseline is invalid.** Fix the seed to also strip its grant rows, or exclude it from backfill.
- **F4** Owners land on `/Home`, other roles on `/` after login — confirm intended.
- **Minor:** `RequestLoggingMiddleware` logs `UserId=anonymous` for authenticated API requests (observed on `/Api/TimeOffRequest`).

---

## What PASSED / what's healthy
- **Auth boundary solid:** local login works for all 9 tiers; `locked@test` and `deactivated@test` correctly rejected; `/Admin/Users`, `/Admin/Companies`, `/Admin/Settings`, all `/Owner/*` (AdminAccess/SystemConfiguration), `/Director/*` (DirectorHubAccess) correctly deny base users (RBAC sweep: 741 PASS cells).
- **No 5xx** observed across the full role×route sweep (≈120 routes × 9 roles) once transient restart noise (peer-session rebuilds) was filtered.
- **Vacation request → manager approval happy path WORKS** (`tests/vacation-approval-workflow.spec.js` passing).
- **Many handlers correctly gated** (audit's negative space): `Users`, `Blueprints`, `Hierarchy` CRUD, `Grants/Assign`, `Roles`, `Companies`, `ChoreTypes`/`Stores` (with per-scope IDOR checks), `DistributionLists`, `My/*` (self-scoped), `Requests/Index` (per-handler manager+scope checks), API `Hierarchy`/`Theme`/`SelectMolecule`.

---

## Coverage & deferred (honest accounting)
**Deep-tested:** RBAC route access (all tiers), grant-handler authorization (full codebase audit), vacation request/approval/shorten, grant revoke.
**NOT deep-tested this session (recommend follow-up):**
- Keyboard-only grid navigation / focus-trap (Phase 3.2) — the Excel-calendar grid is behind a feature flag (default OFF); not exercised.
- Assign shift/chore/on-duty *happy-path UI* + notes CRUD + "Day at X" (Phase 3.1) — covered by static audit (F7) but not E2E happy-path tested except vacation.
- Broad fuzz/chaos (Phase 3.4) — only the vacation input path was probed.
- F7/F8/F9/F10 — found by static audit; **not individually reproduced live** (the shared bug class was proven via F1).

## Artifacts (in `qa-automation/`)
- **Permanent specs (committed to `tests/`):** `security-grant-handler-authz.spec.js`, `vacation-shorten-dates.spec.js`, `vacation-approval-workflow.spec.js`
- **PoC / tooling (root, NOT in test runner):** `rbac-sweep.js` (+ `reports/rbac-sweep.json`), `auth-probe.js`, `verify-org-access.js`, `exploit-revoke.js`, `exploit-updatedates.js`
- **Matrix:** `QA_MATRIX_STATUS.md`
