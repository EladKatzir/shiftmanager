# ShiftManager — Whole-App "Prove It Perfect" Audit (fresh-session prompt)

> Paste everything below the line into a brand-new Claude Code session in this repo. It is self-contained:
> it assumes you have only the auto-loaded memory (`MEMORY.md` + linked files) and this repo — no prior
> conversation. Run it in a fresh session so context is clean and the token budget is full.

---

You are auditing the ENTIRE ShiftManager application to prove — or disprove — that every feature is correct
end-to-end: right **design**, right **logic**, right **real-user journey**. Cost is not a constraint. The
user has explicitly authorized heavy multi-agent orchestration: **use Workflows** (say `ultracode` / "use a
workflow") and **`/goal`** to iterate until the app is verifiably solid. Do not conserve tokens; conserve
*correctness*.

## 0. First, read the hard-won lessons — DO NOT repeat the last session's failure

A prior session deeply verified ONE feature (Calendar Tabs on `/Calendar/Shifts`), then claimed "everything
is perfect / all edge cases verified." That was wrong: a **major** design flaw on `/Calendar/Team` (creating
a saved "table" gives no way to choose the team — it silently snapshots the top picker) sat unnoticed, plus
a delete-shows-wrong-error bug and a Hebrew string error. The blind spots that let confident language outrun
reality — **treat these as binding rules for this audit**:

1. **Audit DESIGN, not just implementation-vs-spec.** The Team bug matched its spec exactly — the *spec* was
   wrong. For every feature, ask "is what this is supposed to do actually correct for the real user?", not
   only "does the code do what the doc says?"
2. **Start from the real user's GOAL and full journey, not from the code.** Who uses this (military shift
   management: leads, kabar/BR directors, molecule admins, assigners, employees, trainees, directors,
   owners)? What are they trying to accomplish? Walk the whole journey, every entry point and state.
3. **Test UX / discoverability, not just mechanics.** "The dropdown groups / the POST persists" is not
   "a real Alhut lead can intuitively accomplish their goal." Mechanical correctness ≠ the feature working.
4. **Never let confidence language exceed the tested surface.** "No product bugs found" is only ever true
   *within the surface you actually exercised*. Say exactly what you tested and what you did not.
5. **A pass that finds only test-harness artifacts is not proof of perfection** — it can equally mean you
   were not looking at the right surface. When a sweep goes quiet, widen it before you trust it.

## 1. The app (architecture — details in memory files)

ASP.NET Core 8 + SQLite + Razor Pages, deployed **air-gapped** on Windows/IIS. Multi-tenant via
`IBelongsToCompany` + EF query filters + `_tenantResolver.GetCurrentTenantId()`. Hierarchy:
**Project → Area → Molecule → Company → Department**; JobType is Area-scoped. Authorization is a **grant
system (~138 grants)** + Roles + RoleTemplates; nav visibility == access (NavRegistry policy parity).
Multi-company membership (one AppUser ↔ many companies). Real-time via SignalR. Read `MEMORY.md` and its
linked topic files first — they hold the architecture, gotchas, and per-feature history. Key references:
`grant_and_calendar_architecture`, `busy_detection_architecture`, `ef_sqlite_translation_gotchas`,
`css_text_contrast_rules`, `griffin_auth_hardening`, `playwright_culture_cookie`.

## 2. Feature inventory to audit (from `Services/Navigation/NavRegistry.cs` — the user-facing map)

Audit every one of these end-to-end. Grouped by cluster (good workflow boundaries):

- **Scheduling / Calendars:** Shifts (`/Calendar/Shifts`, incl. the Tabs/לשונית feature), Chores
  (`/Calendar/Chores`), On-Duty (`/Calendar/OnCall`), **Team (`/Calendar/Team`)**, My Desk / Overview
  (`/Calendar/Overview`), plus legacy `/Calendar/Table`, `/Calendar/Month|Week|Day`, and `/MyTeam`.
- **Scheduling / Planning & Definitions:** Shift Plans (`/Owner/Programs`), Master Plans, Chore Templates,
  Duty Rotations, Blueprints, Shift Groupings, Chore Types, Duty Types, **Calendar Tabs admin**, Eligibility
  (`/Scheduling/Eligibility`), Rules/Settings.
- **Requests / Approvals:** My Requests / time-off (`/My/Requests`), Approvals (`/Requests/Index`),
  cancel-or-shorten flows, vacation/day-at/day-after side-effects + materializer.
- **Director tools:** Notification Hub / cross-company approvals, View-As-Manager, Company Filter.
- **People:** Users (`/Admin/Users`), Join Requests, Companies, Announcements, Home Types.
- **Organization:** Hierarchy, Job Types, Stores, Area Palette.
- **Access / permissions:** Roles, Grants, Role Templates, Permission Simulator, Directors, Locked Users.
- **Insights:** System Health, Telemetry, Audit Log, Analytics / Justice.
- **System:** Feature Flags, Email Config/Templates, SSO (Griffin/ADFS), Game Config, Localization,
  Area Config, Setup Tasks, Backup, Data Lifecycle, Export User Data, Database Console.
- **Me / personal:** Profile, Settings, My Eligibility, My Groups, Friends, Help; Home dashboard widgets
  (`/Home` — note: the "understaffed days" widget was just corrected, sanity-check the dashboard).
- **Cross-cutting (audit as their own "features"):** Auth/login + Griffin, Notifications + Mail, Multi-company
  membership, Draft Mode (across all calendars), localization/RTL (Hebrew) correctness everywhere,
  grant/scope enforcement (IDOR) on every POST handler that takes an id.

## 3. Methodology — for EACH feature (this is the standard of "perfect")

Run these dimensions. A feature is "verified" only when all are clean AND an adversary failed to break them:

1. **Goal & journey:** state the real user goal + map every step/entry/state.
2. **Design correctness:** is the intended behavior right for the goal? Challenge the spec. (This is where the
   Team bug lived.) Flag "works as designed but the design is wrong."
3. **Logic & edge cases:** empty/one/many, boundaries, concurrency/races, null/absent scope, cross-molecule/
   company, deleted-mid-session, re-entrancy, ordering, time zones/dates.
4. **Security / scope:** every POST/mutating handler that takes a client id must re-verify authorization
   scope (IDOR) — page-level `[Authorize]` is not enough. Tenant isolation via query filters; any
   `IgnoreQueryFilters()` must be scope-justified. Grant/role gating matches intent.
5. **Data integrity:** multi-step writes are transactional; migrations are safe/consistent
   (`dotnet ef migrations has-pending-model-changes`); no half-states on failure.
6. **UX / discoverability & localization:** a real persona can accomplish the goal intuitively; Hebrew (RTL)
   strings are correct and complete; no dark-on-dark contrast (see `css_text_contrast_rules`).
7. **Cross-feature interactions:** does it interact correctly with related features (e.g. Draft Mode,
   multi-company, notifications, the shared calendar builder)?
8. **Prove it at runtime:** actually click through the feature as the relevant persona (see §5), not just read
   code. Distinguish "verified by test", "verified by click-through", "reasoned only".

## 4. Orchestration — how to actually run this (Workflows + /goal + subagents)

- **One Workflow per feature-cluster** (fan-out → adversarial verify → synthesize). Pattern: parallel agents
  each audit one dimension of the cluster's features (design / logic / security / UX+RTL / cross-feature),
  producing structured findings; then a verify stage where independent skeptics try to REFUTE each finding
  (kill false positives); then synthesis dedupes + ranks CONFIRMED findings. Keep audit workflows READ-ONLY.
  Limits: 16 concurrent agents, 1000/run — so scope each workflow to one cluster, not the whole app.
- **Fixing is separate from finding.** After a cluster's workflow returns confirmed findings, YOU (main loop)
  triage and fix them, each with: root-cause fix (no workarounds — per user CLAUDE.md), a regression **test**
  (real-SQLite fixture, not InMemory — see gotchas), a rebuild, and a **runtime click-through** (§5). If you
  parallelize fixes across agents, give each `isolation: worktree` so they don't collide on the tree (a real
  hazard hit last time). Then re-run the cluster audit to confirm the finding is gone.
- **Drive the loop with `/goal`.** Set a measurable, transcript-checkable condition, e.g.:
  `/goal Every feature cluster in docs/superpowers/2026-08-03-whole-app-audit-prompt.md §2 has a completed
  audit workflow; every CONFIRMED finding is fixed with a passing regression test and a runtime click-through;
  the full serialized suite is green; and a final completeness-critic workflow reported zero new confirmed
  findings. Report progress each turn as an explicit cluster-by-cluster checklist.` The Haiku evaluator only
  reads the transcript, so keep an explicit running checklist in your turns. (Alternatively drive the loop
  manually if you want tighter control — the user asked for `/goal`, so prefer it, but keep the checklist
  visible either way.)
- **Completeness critic (last):** a final workflow that asks "what feature, journey, persona, or interaction
  was NOT audited or NOT click-verified?" — its output is the next round of work, not a sign-off.

## 5. How to build / run / test / E2E (learned the hard way — obey these)

- **Executable-lock rule:** the user's dev app runs from **`bin/Debug` on :5000/:5001** — never kill it, and a
  **Debug** build (default `dotnet test`/`dotnet build`) will collide with it. **Build/test in Release**
  (`-c Release`) which uses `bin/Release`; only *your own* E2E instance locks that. Never `taskkill` the
  user's app; if you launch an E2E instance, kill only that PID by port.
- **Full suite (serialized — parallel gives ~139+ spurious `:memory:` failures):**
  `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  Read the actual `Passed!/Failed!` summary line — never trust a pipeline's echo exit code.
- **Migration consistency:** `dotnet ef migrations has-pending-model-changes --configuration Release` must say
  "No changes." New tests that touch EF queries MUST use the **real-SQLite** `SqliteDbContextFixture`
  (`UseInMemoryDatabase` hides translation + transaction + NULL-index bugs).
- **Runtime E2E (Playwright, headless chromium):** launch a **Release** build on **:5080** against a **COPY**
  of the live DB — never touch the user's `app.db`:
  `cp app.db <scratch>/e2e.db` → `dotnet ef database update --configuration Release --connection "Data Source=<scratch>/e2e.db"`
  → run `bin/Release/net8.0/ShiftManager.exe` with `ASPNETCORE_ENVIRONMENT=Development`,
  `ConnectionStrings__Default="Data Source=<scratch>/e2e.db"`, `ASPNETCORE_URLS=http://localhost:5080`.
  Startup seeders + a DB-backup job can take several minutes on first run; wait for a 302 on `/`. Served JS is
  **minified at build** — rebuild to test JS changes; verify freshness via a NEW string literal in the served
  (minified) file, not a local function name (renamed by the minifier). Reusable helpers + prior scripts live
  in `%TEMP%/tabs_e2e/` (`lib.py` = login/goto helpers). Culture: set the `.AspNetCore.Culture` cookie
  (`c=he-IL|uic=he-IL`); `?culture=` does nothing. Use a unique `--user-data-dir`; never `taskkill chrome`.
- **Personas (all password `Test1234!`; 543 bulk-seeded across all 22 real companies + QA/@shifty.test sets):**
  `lead.alhut.hir@test`, `lead.alhut.city@test` (Alhut leads, Hir/City companies, Oren molecule = MoleculeId 1,
  Alhut JobType = 1). Roles enum: Owner=0, Manager=1, Employee=2, Director=3, Trainee=4, Assigner=5,
  AreaAdmin=6. RoleTemplate keys e.g. BRDirector (קב"ר), AlhutLead, MoleculeAdmin (מפק"מ), Lead (מפ"צ).
  `@shifty.test` accounts (test.owner/director/manager/member/assigner/nogrants) exist for role coverage.
  Audit EACH feature from MULTIPLE personas — the Team bug only shows from a *lead's* seat, not an owner's.

## 6. Current state + known handoff items (verify these too)

- **Branch `dev`** (tip ~`03d75ad`), NOT pushed. `feat/calendar-tabs-selectors` was fast-forward-merged in.
- **`v5.3.0` FinalProductPublish was built then went STALE** (Hebrew + Team-delete fixes landed after it) —
  regenerate it at the end: `.\scripts\Update-FinalProductPublish.ps1 -Version "5.3.x" -SkipTests -SkipGit
  -Confirm:$false` (Release publish; safe vs :5000). Do NOT git push or tag without the user asking.
- **Migration data-deletion GATE (must re-flag before any deploy):** `ReshapeShiftTabsForSelectors` runs
  `DELETE FROM ShiftTabs/ShiftTabCompanies/UserShiftTabPreferences` + drops on first startup. Written as
  "test-data only, user-confirmed." Confirm the real target DB has no live tab data to lose before deploying.
- **DECIDED fix to implement (user-approved) — Team "add table" frame:** replace the silent name-only add
  (`wwwroot/js/team.js addTeamTable` + `Team.cshtml` "team-view-add" + `TeamModel.OnPostAddViewAsync`) with an
  explicit dialog/frame where the lead chooses **Company + JobType + Name** for the new table (default to the
  current picker). ALSO investigate whether a lead's Company picker (`AccessibleCompanies` = `ViewShifts`
  grant scope ∪ own company) is even populated with the teams they'd expect — the real complaint may be scope,
  not just UI. Do this inside the Team-feature audit with full journey analysis.
- **Already fixed this handoff (spot-check, don't redo):** `UnderstaffedDays` HE string; Team delete drops
  stale `?ViewId=`.

## 7. Deliverables & guardrails

- **Per cluster:** a written findings report (CONFIRMED vs plausible, severity, file:line, failure scenario,
  and for each: is it a *design* flaw or an *implementation* bug). Save under `docs/superpowers/audit/`.
- **Per fix:** root-cause change + regression test (real-SQLite) + rebuild + runtime click-through; then
  re-audit that finding.
- **Model policy (user CLAUDE.md):** declare the model per task; use **Opus** for architecture / hard bugs /
  final validation, Haiku only for scoped extraction/verification. No workarounds — fix root causes. Disclose
  every deferral explicitly and keep a "Deferred Items" section.
- **Final report — HONEST and CALIBRATED:** exactly what was audited, from which personas, verified how
  (test / click-through / reasoned), what was fixed, what remains, and where residual risk lives. Do NOT say
  "perfect" as an absolute — state confidence with its scope. If a surface wasn't exercised, say so.

Begin by reading `MEMORY.md` + linked files, then this document, then propose the cluster order (suggest
starting with the **Calendars** cluster — highest-risk, where the last bug lived — then Access/permissions,
then outward) and set the `/goal`. Then run the first cluster's audit workflow.
