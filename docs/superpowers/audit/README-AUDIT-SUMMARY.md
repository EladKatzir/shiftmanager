# ShiftManager whole-app audit — master summary

## TL;DR

**Did we check everything? Every feature area was read at least once. Almost none of it was
*verified*, and nothing was tested at runtime except one page.** Those are very different claims and
this report keeps them apart deliberately.

| | |
|---|---|
| Findings raised | **246** (23 critical · 74 high · 118 medium · 31 low) |
| **Independently hand-verified** | **15** |
| Verified **healthy** (negative results) | **10 areas** |
| Adversarially refuted (agent-vs-agent) | 40 of 132 in C1 only — **C4–C7's 67 got none** |
| Fixed | **1** (C1-F01, with tests + runtime click-through; suite 1985/1985) |
| Click-verified at runtime | `/Calendar/Team`'s desk picker, a 7-persona login matrix, language behaviour. **Nothing else.** |

**So the large majority of those 246 are unverified single-agent output** (the 15 hand-verified items
partly overlap the 246 and partly were found independently, so no exact subtraction is offered — and
the reports are not deduplicated against each other). **Expect false positives at a meaningful rate:**
three of my own mechanical sweeps were badly wrong before I corrected them — one reported **442**
fabricated criticals in the most security-sensitive file in the repo; the true answer was **0**.
Treat every unverified list as a prioritized inspection queue, not a defect list.

### The one thing to take away

**Every critical finding is the same bug wearing different clothes: a guard left pointing at the old
model.** Fourteen controls are provisioned, documented, and never wired — a grant granted to 6 role
templates and read by zero code; two functions taking scope parameters they never read; an approval
state machine with zero callers; 57 of 138 grants unenforced; a role blocklist checking the legacy
`UserRole` enum after the app migrated to grant-based authorization.

This is **drift, not incompetence** — `/Owner/DatabaseConsole` and `/Owner/Backup` are correctly
layer-defended *and* their comments match their code. Features were refactored; the claims around them
never were. That thesis is also **predictive**: it produced a 37-site hunt list (§4) where more of
this class almost certainly lives.

### If you do only three things

1. **Fix the 8 hand-verified criticals in §2** — they are real, reachable, and several need only a
   predicate. Start with the API-key theft (any Trainee, one integer) and `/Api/Hierarchy/*`
   (permanent company deletion, cross-scope).
2. **Add the two guard tests in §5.** They retire entire bug classes permanently and already exist as
   working scripts.
3. **Verify `Authorization/GrantAuthorizationHandler.cs:40`** before anything else in the unverified
   pile — if a *company-scoped* `SystemConfiguration` grant really does open the global Backup /
   DatabaseConsole / DataLifecycle pages, that outranks most of what is already confirmed.

### What was NOT done — read this before trusting any silence

- **No runtime testing** beyond the one page above. All other findings are code reading.
- **RTL layout and colour contrast: not concluded.** Key parity is exact and 15 undefined CSS tokens
  were found, but the contrast/RTL detectors produced 310 and 225 raw hits too noisy to be findings.
  **No page was ever rendered in Hebrew and looked at.**
- **No adversarial verification at all on C4–C7** (67 findings, incl. 5 critical).
- `/Calendar/Chores` beyond the assign path; `Pages/Requests/Swaps/*`; `Pages/Owner/Backup` restore
  semantics under load.
- **Nothing is committed** — the C1-F01 fix, its 4 tests, and all 12 reports are uncommitted on `dev`.

---

**Dates:** 2026-08-03 → 2026-08-04 · **Branch:** `dev`
**Directive:** audit broadly, fix later. One fix was made (C1-F01) before that directive was given.
**Nothing from this audit is committed** — the C1-F01 fix, its 4 tests, and all 11 reports are
uncommitted on `dev`. (`345c54b`, the Tabs admin redesign, was committed separately mid-session.)

Start here. Per-area detail is in the sibling files.

| Report | Covers |
|---|---|
| `cluster-1-calendars.md` | Shifts+Tabs, Team, Overview, OnCall, legacy Table/Month/Week/Day, MyTeam |
| `cluster-1-chores-hand-audit.md` | `/Calendar/Chores` |
| `cluster-2-access-permissions-PARTIAL.md` | grant engine, roles/grants admin, simulator, directors |
| `cluster-3-requests-and-idor-breadth.md` | app-wide IDOR, multi-company scope, Requests/Approvals |
| `cross-cutting-hand-audit.md` | localization, page-auth surface, company switcher, unread scope params |
| `hand-audit-part2-users-and-tenant-filters.md` | `/Admin/Users` privilege guard; the 115-site `IgnoreQueryFilters` work list |
| `hand-audit-part3-hierarchy-api.md` | `/Api/Hierarchy/*` — no target authorization on any endpoint |
| `hand-audit-part4-verified-healthy.md` | auth contracts, Database Console, Backup path validation |
| `hand-audit-part5-css-tokens-rtl.md` | undefined CSS custom properties; why the contrast/RTL sweeps are not findings |
| `cluster-4-7-planning-people-org-system.md` | Planning, Definitions, People, Organization, System, Integrations, cross-cutting |
| **`FIXES-2026-08-04-apikey-and-team.md`** | **the two fixes delivered, a corrected severity claim, and a new under-access finding** |

---

## 1. The single most important finding is a pattern, not a bug

**This codebase repeatedly PROVISIONS a security control and never WIRES it — then writes a comment
asserting the guarantee.** Ten verified instances:

| # | Control | Reality |
|---|---|---|
| 1 | `ViewAllShifts` grant | Seeded, granted to 6 templates at molecule scope, held by real users — **read by zero production code**. *(FIXED)* |
| 2 | `BusyService.GetBusyStatesAsync(int moleculeId)` | Parameter **never read**, under a `SECURITY-AUDITED` comment claiming molecule gating |
| 3 | `ScopeFilterService.ValidateScopeAccessAsync(int? scopeId)` | Parameter **never read** — every "may I see scope X?" answers scope-independently |
| 4 | `VacationApprovalService.ApproveAsync` / `DeclineAsync` | **Zero production callers**; the live path sets status directly, bypassing dual-approval thresholds |
| 5 | **57 of 138 grants** | Unenforced **and** not `IsDeprecated` — shown as live in the admin UI, held by real users |
| 6 | `LoadOnDutiesAsync(List<int> companyIds, …)` ×3 files | `companyIds` **never read**; `IgnoreQueryFilters()` with no replacement predicate |
| 7 | `<loc key="X">Fallback</loc>` | Tag helper **discards** the inner fallback; 47 keys render the raw key name |
| 8 | `RoleService.AssignRoleAsync` ↔ `Roles/Assign.cshtml.cs` | Each `SECURITY-AUDITED` comment defers the boundary **to the other**; neither checks the target |
| 9 | `/Api/Hierarchy/*` (4 endpoints) | `SECURITY-AUDITED: … SAFE — requires Grant:ManageHierarchy` — a policy that carries **no target scope** |
| 10 | `ChoreService.CreateChoreAsync` | Comment states *"ChoreType is molecule-scoped"* on the line that fails to enforce it |
| 11 | `/Director/ViewAsMode` | Writes a cookie **no data path reads**, behind a banner asserting the director is viewing another company |
| 12 | DutyRotation assignment engine | `AssignNextAsync` / `PreviewRotationAsync` / `GenerateAssignmentsAsync` — **zero production callers**; a configured rotation never assigns anyone |
| 13 | Import **"Overwrite Existing"** conflict policy | Dead control — silently inserts duplicates instead of overwriting |
| 14 | Bulk-import role blocklist | Checks the **legacy `UserRole` enum** while authorization runs on `RoleTemplateId` — blocks 3 of 10 templates |

**Why it matters beyond the individual bugs:** you cannot currently answer *"who can do what?"* from
this system's data. The answer depends on which grants happen to be wired, whether their scope
survives comparison, and who was able to self-issue them.

**But this is drift, not incompetence** — see §4. That distinction determines the right fix.

---

## 2. CRITICAL findings (each hand-verified in the main loop)

1. **~~Any authenticated user can steal a colleague's API key.~~ FIXED 2026-08-04 — and my severity
   claim was CORRECTED downward.** `RegenerateApiKeyAsync`/`RevokeApiKeyAsync` filtered on **company,
   not ownership**, so any user could rotate any colleague's key (denial of service) and the service
   *returned* another user's plaintext to an unauthorized caller. **But the page does NOT render it** —
   the handler `return RedirectToPage()`s and `GeneratedApiKey` does not survive the redirect, which I
   confirmed at runtime. "Credential theft via the UI" was my error. Real severity: **high** (DoS +
   broken service contract), not critical theft. → `FIXES-2026-08-04-apikey-and-team.md`

2. **Any `AssignRoles` holder can self-assign the globally-scoped Owner role.**
   95 users hold it (90 company-scoped). No target/role/scope check anywhere in the chain, and
   `ValidateScope` **fails open** on an unmatched `ScopeLevel` — Owner's `ScopeLevel=7` matches no
   case. → `cluster-2-…` C2-F01

3. **`/Api/Hierarchy/Delete|Rename|Move|Create` have zero target authorization.**
   All four gate only at page level; **companies are HARD deleted** (`_db.Companies.Remove`). Six
   `ManageHierarchy` holders (3 molecule-scoped, 3 project-scoped) can permanently destroy a company
   in any other area or project. → `hand-audit-part3`

4. **The core scope primitive discards Molecule/Area scope.**
   `GrantService.cs:276-280` returns true on a JobType match whenever the grant has no `CompanyId` —
   never reading its `MoleculeId` — three lines after the molecule branch correctly rejected the
   request. 268 grant rows / 21 types / 44 users hold the exploitable shape. → `cluster-2-…` C2-F02

5. **57 of 138 grants are silently unenforced**, including four `CanBeAssigned*Shifts` **eligibility**
   grants held by 609 users each. Revoking one does not stop assignment. → `cluster-2-…` C2-F03

6. **The vacation approval state machine is dead code.** Dual-approval thresholds never enforced; and
   approval status **commits before** side-effects run inside a log-only `try/catch`, so a
   side-effect failure leaves a permanently approved request with no shifts removed and no
   notification sent. → `cluster-3-…` B-F02

7. **Bulk CSV import mints privileged accounts.** The guard blocks Owner/Director/AreaAdmin by
   **legacy `UserRole` enum**, but the account is created with `RoleTemplateId` resolved from the CSV
   string. **MoleculeAdmin (102 grants), BRDirector (71), Lead (58) and Assigner (27) all pass the
   blocklist** because their derived role is merely `Manager`/`Assigner`. One CSV row creates a
   molecule-wide admin whose password the importer chose. → `cluster-4-7-…` C4-F01

### High, hand-verified
- **No handler on `/Admin/Users` compares the TARGET's privilege.** Company scoping is correct
  everywhere; privilege comparison is absent. A Manager with `EditCompanyUsers` for company X can
  reset the password of an Owner who sits in company X → account takeover. → `hand-audit-part2` H-F01
- **`/Calendar/Week|Month|Day` expose every duty assignment in the deployment** with assignee names.
  → `cross-cutting-…` X-S01
- **47 localization keys have no resx entry**; the UI renders the raw key name. Worst hit: Owner →
  Locked Users (6) and Owner → Role Templates edit (5). → `cross-cutting-…` X-L01
- **Chores: client-supplied `MoleculeId`/`ChoreTypeId` are never scope-validated** → cross-molecule
  injection, conflict-detection evasion, attacker-chosen fairness weight. → `cluster-1-chores-…`

### Critical, reported in the final clean run (NOT yet hand-verified)
- **`/Admin/Companies` rename/move/delete accept any company id with no scope check**, while the
  `EditCompany` grant gating the page is company/molecule-scoped — and the GET already lists all 37
  companies. → `cluster-4-7-…`
- **Deleting a molecule-shared blueprint silently cascade-deletes OTHER desks' Programs,
  ShiftInstances and assignments** (`Pages/Owner/Blueprints.cshtml.cs:480`) — irreversible schedule
  destruction across desks the deleter may not administer. → `cluster-4-7-…`

### Reported by agents, NOT yet hand-verified
`/Admin/Organization/Grants/Assign` minting unscoped `AdminAccess`; vacation approval gated on
`ManagerHomeAccess` rather than `ApproveVacations`; membership removal not invalidating a switched
session; `/Admin/HomeTypes` and the Organization admin pages acting system-wide; **global-effect Owner
danger pages (`Backup`, `DatabaseConsole`, `DataLifecycle`, `FeatureFlags`, `ExportUserData`)
accepting a COMPANY-scoped `AdminAccess`/`SystemConfiguration` grant**
(`Authorization/GrantAuthorizationHandler.cs:40`) — that last one would mean delegating "admin for
desk 7" silently grants deployment-wide destructive access, and is the highest-value item to verify
next. **Treat all as strong suspicion.**

Roughly **200 further findings** across the cluster reports were never independently checked — expect
false positives at a meaningful rate (my own sweeps ran ~⅓ false positives before correction).

---

## 3. VERIFIED HEALTHY — do not re-audit these blind

- **Localization parity is exact** — en 5775 = he 5775, **0 keys missing in either direction**.
- **Page authorization coverage is strong** — 165 PageModels: 146 `[Authorize]`, 18 explicit
  `[AllowAnonymous]`, 1 with neither (`My/HelpNavigation`, read-only, no handlers).
- **The company switcher is sound in both directions** — `SelectCompanyAsync` gates on `IsMemberAsync`
  before writing an `HttpOnly`/`SameSite=Strict`/`Secure` cookie, and `TenantResolver` re-validates it
  against the `MemberCompanyIds` claim on read. *(Documented residual: the claim is a login-time
  snapshot, so a revoked membership is honored up to the 12h TTL.)*
- **RoleTemplate seed integrity is clean** — 363 commented `G(templateId, grantId, …)` calls checked
  against the authoritative `id++` order: **0 mismatches**; the seeded DB agrees on all 138 ids.
- **The chore assign path authorizes correctly** — the grant is evaluated against the **assignee's**
  company, the correct target-based pattern.
- **All three auth contracts hold** — case-insensitive email lookup (18/18 sites), `PasswordHasher`
  everywhere (no raw HMAC/SHA), and zero `int.Parse` on claims.
- **`/Owner/DatabaseConsole` hardening is real** — `SELECT`-only, semicolons rejected, and a genuinely
  `ReadOnly` SQLite connection. Bypasses probed and rejected.
- **`/Owner/Backup` path validation is textbook** — blacklist **plus** canonicalize-and-verify-prefix,
  applied by **all three** client-input handlers (restore, delete, download).
- **Bare `[Authorize]` is not itself a defect here** — `Pages/Requests/Index.cshtml.cs` is the
  **in-repo reference implementation** of the target-scoped check
  (`ValidateAccessToRequestAsync(user, r.CompanyId)`). The fix for the calendar IDORs is to apply
  *that* pattern, not to invent one.

---

## 4. What I think is actually going on

`/Owner/DatabaseConsole` and `/Owner/Backup` are the tell. Both are correctly layer-defended, and
their comments match their code. This team can and does build real security controls.

So the fourteen unwired controls are not a skills gap — they are **drift**. Features were refactored
(the 8 per-type assign grants collapsed into `AssignShifts`; `CanBeAssigned*` eligibility replaced by
`DoesShifts`+`ShiftCategory`; approval rewritten to set status directly; **authorization migrated from
roles to grants**), and the *claims* around them — seed entries, method parameters, `SECURITY-AUDITED`
comments, blocklists — were never revisited. The old guarantees kept being asserted after the
enforcement moved or vanished.

**The bulk-import bug (§2 item 7) is this thesis in one function.** The blocklist was *correct* when
roles were the authorization model. The app moved to grant-based auth via RoleTemplates; the guard
never moved with it, and now faithfully checks an axis that no longer decides anything.

### That thesis predicts where the remaining bugs are

If the pattern is "a guard still reasoning about the old model", then every surviving
`Role == UserRole.X` / `IsInRole(nameof(UserRole.X))` used for an **authorization decision** is a
candidate. There are **37 such sites across 16 files**: `Admin/Companies`, `Admin/Directors`,
`Admin/Users`, `Auth/GriffinSignup`, `Auth/Login`, `Auth/Signup`, `Calendar/Table`, `Public/Feedback`,
`Services/CompanyFilterService`, `Services/DirectorService`, `Services/NotificationService`,
`Services/PersonalTimelineService`, `Services/TenantResolver`, `Services/TraineeService`,
`Services/UserCompanyTransferService`, `Services/VacationApprovalService`.

Not all are defects — `TenantResolver` legitimately uses `IsInRole(nameof(UserRole.Owner))` for the
Owner-selector rung, and some are display logic. But this is a **prioritized hunt list derived from a
root cause**, which is a far better starting point than another undirected sweep.

That is a much more tractable problem than "the codebase is insecure", and it points at the fix.

---

## 5. The two fixes worth more than any individual bug

1. **A guard test asserting every non-deprecated seeded grant key has ≥1 production reference.**
   Retires the entire dead-control class (items 1 and 5 in §1).
2. **A guard test asserting every `<loc key="X">` / `Localizer["X"]` has a resx entry.**
   Retires item 7.

Both already exist as working scripts (`scratchpad/dead_grants.py`, `scratchpad/resx_parity.py`);
promoting them to tests is small and permanent. A third worth considering: fail the build on any
`IgnoreQueryFilters()` that is not paired with a scope predicate **or** an explicit allow-list entry.

---

## 6. The one fix delivered (complete, to the §7 standard)

**C1-F01 — the dead `ViewAllShifts` grant.** Root-caused to a single missing enforcement point and
fixed via `IGrantService.GetShiftVisibleCompanyIdsAsync` (the union of `ViewShifts` + `ViewAllShifts`),
wired into **both** `TeamModel.OnGetAsync` and `OnPostAddViewAsync` — a GET-only fix would have offered
a desk the POST then 403s.

- 4 new real-SQLite regression tests, **RED verified before GREEN**
- Full serialized suite: **1985/1985**
- Runtime click-through: the Alhut lead's desk picker went **1 → 10 desks**; saving a view for a
  sibling desk returned 200; an out-of-scope id still 403s on GET **and** POST; a non-holder still
  sees exactly 1 desk (**no over-granting**)

---

## 7. NOT AUDITED — stated plainly

- **Clusters 4–7: all 8 dimensions returned, 0 errors** — 67 findings (5 critical, 22 high) in
  `cluster-4-7-planning-people-org-system.md`. This was the only fully clean multi-agent run of the
  audit. Its findings are **single-agent output and not independently verified** except where the
  report marks them MAIN-LOOP VERIFIED.
- **RTL layout is genuinely unaudited.** The *mechanical* half of localization is done (key parity is
  exact; glossary clean; 15 undefined CSS custom properties found — see
  `hand-audit-part5-css-tokens-rtl.md`). But the contrast and RTL detectors produced 310 and 225 raw
  hits that are **too noisy to be findings** (the contrast matcher also matches light `--primary-soft`
  tints; many physical `left/right` rules are legitimately direction-neutral). **No page was rendered
  in Hebrew and looked at.** That, not more static analysis, is the right next step — and mind the
  harness trap: the `.AspNetCore.Culture` cookie has **no effect for logged-in users**
  (`Users.PreferredLanguage` wins), so a cookie-based "Hebrew pass" silently tests English.
- **`/Calendar/Chores` beyond the assign path** — fairness maths, draft path, delete/restore
  endpoints, SignalR group, chore↔time-off interaction.
- **~150 carried findings** across the cluster reports were never independently verified.
- **Runtime coverage is thin.** Only `/Calendar/Team`'s picker, the 7-persona login matrix, and the
  language-preference behaviour were click-verified. Everything else is code reading.

---

## 8. Method note — and a warning about the sweeps

Large multi-agent workflows lost ~6M subagent tokens to network drops and session limits; a dead agent
returns nothing. The main loop survived every outage, and **every critical finding above was found or
confirmed by hand**, using techniques the agents did not apply: whole-repo mechanical sweeps,
line-by-line traces of a single method, and cross-referencing code against the seeded database.

**Three of my own sweeps produced wrong answers before they produced right ones**, and every one would
have been a large false alarm:

| Sweep | First answer | Cause | Final |
|---|---|---|---|
| Dead grants | 65 dead | missed `"Grant:X"` policy literals (a colon defeats a word-chars regex) | 64, of which **57** are not deprecated |
| Seed integrity | **442 mismatches** | parsed the *template* id as the grant id; then LATE-ADDITIONS comments name the template | **0** |
| Unread scope params | 12 hits | positional records use params implicitly; two "hits" were unimplemented stubs | **1** real (in 3 files) |

**A mechanical sweep produces a shortlist to inspect — it is never evidence.** Always validate a
sweep against a known-true fact before believing its count. Reusable scripts:
`dead_grants.py`, `resx_parity.py`, `authz_sweep.py`, `unread_params.py`, `seed_integrity.py`,
`ignorefilters_sweep.py` (session scratchpad).

---

## 9. Housekeeping still open

- **Nothing is committed.** The C1-F01 fix, its 4 tests, and all 8 reports sit uncommitted on `dev`.
- **`packages.lock.json`** lost its entire `net8.0/win-x64` target (80 entries). The portable
  `net8.0` section is byte-identical. This matters because
  `scripts/Update-FinalProductPublish.ps1:514-515` publishes with `-r win-x64 --self-contained true`
  and the csproj sets `RestorePackagesWithLockFile`. Recommended: `git checkout -- packages.lock.json`
  unless the regeneration was deliberate.
- **The Tabs admin redesign was committed mid-audit as `345c54b`** (~228 lines + ~15 resx keys ×2 +
  a `.ss-panel[hidden]` CSS fix). It was audited as part of the Definitions dimension while still in
  the working tree; treat any Definitions findings touching
  `Pages/Admin/Organization/Tabs/Index.cshtml` as applying to that commit.
- **v5.3.0 FinalProductPublish is stale** — regeneration was deferred and is now further out of date.
