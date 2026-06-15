# QA_MATRIX_STATUS — Pre-Deployment E2E Sweep

**Mission:** Exhaustive, aggressive E2E browser testing of ShiftManager before tomorrow's deployment.
**Started:** 2026-06-14
**App under test:** http://localhost:5000 (Development env, live seeded DB — safe/disposable per directive)
**Framework:** Playwright (JS, `@playwright/test`) — existing `qa-automation/` harness.
**Legend:** Status = `Not Tested` / `Testing` / `Done` · Result = `PASS` / `FAIL` / `WARN` / `N/A`

---

## Verified Environment Facts (recon)
- Running process is **Development** — all 45 `QaTestUserSeed` users exist (password `Test1234!`). ✅ verified by live login probe.
- `locked@test` & `deactivated@test` correctly **reject** login (lockout + deactivation gates enforced). ✅
- Owners (`admin@local`, `owner2@test`) land on `/Home`; all other roles land on `/`. ⚠️ note divergent post-login landing.
- Org: Area **190** → molecules {Oren, Ella, Harava, Gefen, Shikma, System, QA} → companies {Tzafona, Hir, Hitazmut, Element, QA-Alpha, QA-Beta, Hamasa, Kabah, Matot, Pie, Tao, Yeadim, SystemAdmins, HQ×n}.
- Job types: Alhut, BR, Text, Hakam (area-wide), ProjectManager (Shikma).
- Role templates: Owner, Director, BRDirector, Lead, MoleculeAdmin, AreaAdmin, Assigner, Employee, Trainee, (none=nogrants).

## Roster used for RBAC matrix
| Tier | User | Role | Template | Company/Molecule |
|---|---|---|---|---|
| Owner | admin@local (`admin123`) | Owner | — | system |
| Owner | owner2@test | Owner | SystemAdmins | SystemAdmins |
| Director | dir.alhut@test | Director | Director | Tzafona/Alhut |
| Manager/Lead | mgr.alhut.tz@test | Manager | Lead | Tzafona/Alhut |
| MoleculeAdmin | moladmin.oren@test | Manager | MoleculeAdmin | Tzafona |
| AreaAdmin | areaadmin@test | Manager | AreaAdmin | Tzafona |
| Assigner | assigner.oren@test | Assigner | Assigner | Tzafona |
| Employee | emp.tz.alhut@test | Employee | Employee | Tzafona/Alhut |
| Trainee | trainee.alhut@test | Trainee | Trainee | Tzafona/Alhut |
| NoGrants | nogrants@test | Employee | (none) | Tzafona/Alhut |
| Cross-company Employee | emp.hir.alhut@test | Employee | Employee | Hir/Alhut |

---

## PHASE STATUS
| Phase | Description | Status |
|---|---|---|
| 1 | Feature mapping & matrix | DONE — 168 page files mapped (agent inventory in report); route×role grid below; grant→page map captured |
| 2 | Tooling setup (Playwright) | DONE — existing harness verified |
| 3.1 | Core workflows (assign/notes/vacation) | PARTIAL — vacation req→approval GREEN (spec); shorten BROKEN (F5); assign shift/chore/onduty UI + notes + day-at covered by static audit (F7) but not E2E happy-path tested (see Deferred) |
| 3.2 | Keyboard/a11y grid nav | DEFERRED — Excel-calendar grid behind feature flag (default OFF); not exercised |
| 3.3 | Permissions & hierarchy (RBAC) | DONE — full role×route sweep; F1(P0)/F2(P1)/F3 confirmed live; F6–F10 via code audit; regression specs added |
| 3.4 | Fuzz / chaos / edge cases | PARTIAL — vacation input path (illogical/oversized/cross-user) probed → surfaced F5/F6; broad fuzz deferred |
| 4 | Keep good tests in suite | DONE — 3 specs added (below) |
| 5 | Final report | DONE — see `QA_FINDINGS_REPORT.md` |

**New permanent specs added to `qa-automation/tests/`:**
- `security-grant-handler-authz.spec.js` — F1/F2 red→green regression (3 fail now = bugs; 1 control passes)
- `vacation-shorten-dates.spec.js` — F5/F6 red→green regression (2 fail now = bugs)
- `vacation-approval-workflow.spec.js` — core happy-path (passing)

**PoC/tooling at `qa-automation/` root (not in test runner):** `rbac-sweep.js`+`reports/rbac-sweep.json`, `auth-probe.js`, `verify-org-access.js`, `exploit-revoke.js`, `exploit-updatedates.js`

---

## FEATURE × ROLE MATRIX
_(Filled continuously. One row per feature/route; columns = expected access per tier.)_

### A. Authentication & Session
| Feature | Route | Owner | Mgr | Emp | NoGrants | Status | Result | Notes |
|---|---|---|---|---|---|---|---|---|
| Local login | /Auth/Login | ✓ | ✓ | ✓ | ✓ | Done | PASS | probe verified |
| Lockout gate | locked@test | block | block | block | block | Done | PASS | login rejected |
| Deactivation gate | deactivated@test | block | block | block | block | Done | PASS | login rejected |

_(Full per-route × per-role data: `qa-automation/reports/rbac-sweep.json` via `rbac-sweep.js`. Security-relevant grid embedded at the bottom of this file. Complete feature/handler/grant inventory: `QA_FINDINGS_REPORT.md` + the 168-route map.)_

---

## ISSUES LOG
_(Appended as found. Severity: P0 blocker / P1 major / P2 minor / P3 cosmetic.)_

| # | Severity | Area | Description | Evidence | Status |
|---|---|---|---|---|---|
| F1 | **P0** | RBAC / Grants | **Cross-tenant privilege escalation.** Any authenticated user (proven as **Trainee**) can revoke ANY user's grant in ANY company via `POST /Admin/Organization/Grants?handler=Revoke`. Handler `OnPostRevokeAsync` is gated only by class-level `Grant:ViewGrants` (held by **149/150 users**) and uses `IgnoreQueryFilters()`. | Trainee (Co.1) revoked grant #1380 (ViewChores) of emp.hir.alhut@test (Co.2): POST→302 success branch; WAL-verified row DELETED. `exploit-revoke.js` | CONFIRMED |
| F2 | **P1** | RBAC / Disclosure | **Permission-map & hierarchy disclosure.** Every authenticated user (incl. Trainee/Employee) can open `/Admin/Organization`, `/Admin/Organization/Hierarchy`, `/Admin/Organization/Grants` and view the full org tree + 148-row grant-assignment table. Root cause: `ViewGrants`(35)+`ViewHierarchy`(39) seeded into BASE templates Employee(1)/Trainee(12)/Assigner(8) in `RoleTemplateSeed`. | `verify-org-access.js`: Trainee sees 148 rows, bodyLen≈Owner's. DB: 149/150 users hold both grants. | CONFIRMED |
| F3 | P2 | Test fixtures | **`nogrants@test` is NOT grant-less** in live DB (RoleTemplateId=1, 22 grants). `QaTestUserSeed.HandleSpecialCases` nulls the template but a later backfill re-applies template 1. Any test using it as a zero-grant baseline is invalid. | DB query: userId 63, RoleTemplateId=1, 22 grants incl. ViewGrants. | CONFIRMED |
| F4 | P3 | UX | Divergent post-login landing: Owners → `/Home`, all other roles → `/`. Possibly intentional; flagged for consistency review. | auth-probe.js | Observed |
| F5 | **P1** (functional) | Vacation (in-flight) | **Shorten-approved-vacation is 100% broken for everyone.** `UpdateRequestDatesAsync` opens a tx (`VacationApprovalService.cs:872`) then calls `SyncMaterialisedHomeRowsAsync` which opens a 2nd tx on the same connection (`HomeMaterialiserService.cs:42`) → `InvalidOperationException: connection is already in a transaction`. Returns 400 `VacationApproval_Error`. **Uncommitted code shipping tomorrow.** | Owner shorten → 400; log stacktrace; `exploit-updatedates.js`, `tests/vacation-shorten-dates.spec.js` | CONFIRMED |
| F6 | **P0** (latent) | Vacation / RBAC (in-flight) | **`/Api/TimeOffRequest?handler=UpdateDates` has NO authorization** (ownership or `ApproveVacations`) — `[Authorize]`-only; loads request via `IgnoreQueryFilters()` (cross-tenant). Sibling Cancel handler checks `request.UserId==userId`; UpdateDates omits it (`actorUserId` used only for logging). Non-owner reaches the mutation (proven); only F5's crash prevents a persisted cross-tenant write. **Fixing F5 without adding the check = live cross-tenant data-tamper P0.** | Cross-company base user reached mutation past authz (got post-guard catch error); code `VacationApprovalService.cs:847-908` | CONFIRMED (code+runtime); write masked by F5 |
| F7 | P1 (×15) | RBAC / Shifts | **`Pages/Calendar/Table.cshtml.cs` — 15 of 16 shift-mutation handlers** (AssignUserToSlot, ChangeUser, UnassignEmployee, ClearAssignment, Update/Delete/Create/EnsureShiftInstance, FillRange[≤730d], +trainee/program-state) gate ONLY on class-level `ManagerHomeAccess` (~59% users), not the dedicated `Assign*Shifts` grants. Only `OnPostAssignEmployeeAsync` checks them ("FINDING-002 FIX"). Most use `IgnoreQueryFilters()` + raw AssignmentId/ShiftInstanceId → cross-tenant. | Audit workflow (static); same class proven by F1 | HIGH-CONF (static; not individually reproduced) |
| F8 | P1 (×8) | RBAC / Programs | `Owner/Programs` + `Owner/MasterPrograms` create/update/delete/generate handlers gate on `ManagerHomeAccess`, not dedicated `CreateShiftPrograms`/`EditShiftPrograms`/`DeleteShiftPrograms`. Template-9 holders (ManagerHomeAccess, no program grants) escalate. Sibling Blueprints does it correctly. Tenant-scoped. | Audit workflow (static) | HIGH-CONF (static) |
| F9 | P1 | RBAC | `Admin/EditProfile` `OnPostDeleteAvatarAsync` gates on `ManagerHomeAccess` with NO per-handler `EditCompanyUsers` check (every sibling handler has it via `CanEditTargetAsync`) → cross-tenant avatar deletion by any base manager. | Audit workflow (static) | HIGH-CONF (static) |
| F10 | P2 | RBAC | (a) `Admin/Settings` edit handler gated by VIEW grant `ViewSettings` (edit grants `EditCompanySettings/EditMoleculeSettings` exist, unchecked; no `SelectedId` scope check). (b) `Admin/Config` Add/Delete OnDutyType mutate GLOBAL config under `ManagerHomeAccess`, bypassing `ManageOnDutyTypes`. (c) `AreaPalette` Save/Reset accept arbitrary `AreaId` with no per-area scope check. | Audit workflow (static) | HIGH-CONF (static) |
| ENV | note | Infra | App on :5000 restarted ≥2× mid-sweep (peer session rebuild). Not an app bug, but tests must tolerate transient `ECONNREFUSED`; sweep hardened with retries+incremental save. SQLite is WAL-mode → snapshot the `-wal`/`-shm` set or reads are stale. | rbac-sweep ERR rows | Mitigated |

### Bug-class summary
**The dominant defect is a single pattern:** a Razor Page declares a class-level `[Authorize(Policy="Grant:<view-or-broad>")]`, but its mutating POST handler performs no stronger per-handler grant/ownership/scope check — so anyone who can VIEW the page can EXECUTE its destructive actions, often cross-tenant via `IgnoreQueryFilters()`. Confirmed live in F1; same pattern in F6–F10. The codebase already has the correct pattern in many places (Users.cshtml.cs, Blueprints, Hierarchy CRUD, Grants/Assign, Companies) — these handlers regressed or were never brought up to it.

### Route × Role access grid (security-relevant routes; from rbac-sweep.json)
`ok`=served · `deny`=403/AccessDenied · `🔴SERVED`=under-privileged user served (escalation) · `—`=env restart (retest)

| Route | Owner | Director | Manager | MoleculeAdmin | AreaAdmin | Assigner | Employee | Trainee | NoGrants |
|---|---|---|---|---|---|---|---|---|---|
| /Director/CompanyFilter | ok | ok | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Director/NotificationHub | ok | ok | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Director/ViewAsMode | ok | ok | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin | ok | ok | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Users | ok | ok | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Companies | ok | ok | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Analytics | ok | ok | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | deny | deny | deny |
| /Admin/Announcements | ok | ok | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/AuditLog | ok | ok | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Config | ok | ok | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Directors | ok | ok | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/EditProfile | 400 | 400 | 400 | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/DutyRotation | ok | ok | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/HomeTypes | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/SetupTasks | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Admin/Organization | ok | ok | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED |
| /Admin/Organization/Areas | ok | deny | deny | deny | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/Molecules | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/Projects | ok | deny | deny | deny | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/Departments | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/JobTypes | ok | deny | deny | deny | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/ChoreTypes | ok | deny | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/DutyTypes | ok | deny | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/Stores | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/ShiftGroupings | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/Hierarchy | ok | ok | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED |
| /Admin/Organization/Grants | ok | ok | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED |
| /Admin/Organization/Roles | ok | ok | ok | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Organization/AreaPalette | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Settings | ok | deny | deny | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Admin/Settings/ApprovalRules | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Admin/Molecules/ApprovalSettings | deny | ok | deny | 🔴SERVED | deny | deny | deny | deny | deny |
| /Owner | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Hub | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Hub/Grants | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Hub/PermissionSimulator | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Hub/RoleTemplates | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Hub/SeedData | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Hub/AuditSearch | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/AreaConfig | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Backup | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Blueprints | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Owner/DataLifecycle | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/DatabaseConsole | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/EmailConfig | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/EmailTemplates | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/FeatureFlags | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/GameConfig | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/GriffinConfig | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/LanguageManagement | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/LockedUsers | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/MasterPrograms | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Owner/Permissions | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Programs | ok | 🔴SERVED | 🔴SERVED | 🔴SERVED | 🔴SERVED | deny | deny | deny | deny |
| /Owner/SelectCompany | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/SystemHealth | ok | deny | deny | deny | deny | deny | deny | deny | deny |
| /Owner/Telemetry | ok | deny | deny | deny | deny | deny | deny | deny | deny |

_Non-admin routes (41): no 5xx and no escalation observed; per-route detail in reports/rbac-sweep.json._
