# Backlog — decisions that need Elad (manual time-off feature)

This file collects **only** items that genuinely need your direct decision. Everything
else was resolved autonomously (via research + a decision-maker agent) and implemented.
If this list is empty when you wake up, nothing was blocked.

Format: `- [ ] <question> — <context / my provisional choice so nothing is blocked>`

## Open decisions

- [ ] **Permission tier was TIGHTENED from your literal D2 choice — please confirm.** You chose
  "anyone who can edit the calendar," which I described as `WriteOverviewNotes OR AssignShifts/...`.
  A final security review found that `WriteOverviewNotes` (and `EditOnCallCalendar`) are held by the
  **base Employee/Trainee role** (the grant service's own docstring says WriteOverviewNotes is one
  "which every role has"). So that gate effectively meant **every authenticated employee could
  create an already-*Approved* vacation for themselves (self-approval — which the normal flow
  forbids) or any molecule colleague, removing shifts with no manager**. That is almost certainly not
  what "shift managers mark people off" intended, so I made the **safe** choice: the server now
  requires the **assign/manager tier** — `AdminAccess / AssignShifts / AssignChores / ManageOnDuty`,
  scoped to the target's company — and **blocks self-entry** (enter your own via /My/Requests),
  matching `ApproveAsync`. Browser-verified: a base Employee (test.member) is blocked (no toggle,
  403); a manager (molecule-scoped AssignShifts) still works for colleagues; self-entry is blocked.
  **If you truly want the broader note-tier (every employee) gate, tell me and I'll loosen it — but
  I'd advise against it.** Impl: `IGrantService.HasCalendarAssignPermissionForCompanyAsync`.

- [ ] **Past-date entry is currently blocked.** The endpoint rejects entering time-off on a date
  before today (mirrors the existing `QuickAddTextEntry` endpoint, and Quick-Entry already blocks
  `--past` cells in the UI). If you want managers to be able to mark a *retroactive* absence
  (e.g. "they were out yesterday"), say so and I'll relax the guard. **Provisional: blocked** — so
  nothing is broken either way.

## Resolved autonomously (FYI — reversible if you disagree)

- **Authorization reuses the existing note-entry gate.** Instead of adding new company-scoped grant
  methods, the service authorizes with `HasCalendarNotePermissionAsync(actor)` +
  `CanReachUserForNoteAsync(actor, target)` — the exact pair the existing user-targeted
  `QuickAddTextEntry` endpoint uses (target-user-aware, molecule-scoped, self-target allowed) — plus
  a company-membership tie so the record lands in the viewed company. DRY; no new grant surface.
- **Side-effects are best-effort, matching `ApproveAsync`.** The approved record commits first, then
  shift-removal + the idempotent HOME materialiser run in try/catch (logged on failure), exactly like
  the real approval path — rather than wrapping everything in one transaction. Consistent with the
  codebase's deliberate "approval is source of truth, reconcile side-effects" design.
- **Page toggle gate is unscoped `HasCalendarNotePermissionAsync`.** The Quick-Entry toggle shows to
  anyone with calendar-note permission; the server still enforces per-target reach + membership, so
  an out-of-scope attempt fails with a clear localized error rather than silently succeeding.
- **Morning-after boundary kept at 13:00** (your explicit choice) — matches the shared `HOME_AM`
  shift-type used by every existing vacation; no shift-type re-seeding.
- **Cross-company HOME-chip render bug — FIXED (Fix B, render-side).** Browser E2E surfaced that
  HOME shift-types are molecule-scoped, so one `ShiftInstance` is shared across companies in a
  molecule and carries whichever company first materialised it. `OverviewCalendarBuilder.LoadShiftsAsync`
  referenced the required `ShiftInstance` nav under the global query filter, INNER-JOINing on the
  instance's company and dropping a cross-company-same-molecule-same-day HOME row (palm-tree still
  showed; the busy-chip vanished). A decision-maker agent researched this (no unique index on
  ShiftInstances; the alternative "per-company instances" fix would break the rotation generator +
  need a migration) and recommended the render-side fix: `IgnoreQueryFilters()` + explicit
  `sa.CompanyId == companyId` (the assignment's company is the true per-user tenant axis, not the
  shared instance's stamp). One method, no migration, fixes existing data, zero risk to the
  approval/rotation flows, provably equivalent for normal shifts. Added a regression test that runs
  with the tenant filter active. Browser-verified: the previously-missing HOME chips now render on
  both Overview and Team.

- **Self-approval blocked + TOCTOU closed (final review).** `CreateApprovedManualTimeOffAsync` now
  rejects `actor == target` up front (consistent with `ApproveAsync`), and wraps the overlap-check +
  insert in a transaction with a re-check so two concurrent entries can't double-book the same day.
  New tests: `SelfTarget_ReturnsCannotSelfApprove`, `NoEditPermissionInTargetCompany_ReturnsNoPermission`.

## Pre-existing follow-ups noticed (NOT fixed — out of this feature's scope; your call)

- The **Week/Month/Day personal calendars** filter HOME by the instance's company
  (`si.CompanyId IN companyIds`), so a collision user can miss their own HOME chip there too — same
  root cause, different (out-of-scope) surface.
- **Cross-company Team**: only shifts got the assignment-company fix here; vacations/chores/on-duties
  on a switched-company Team view still scope by the caller's ambient tenant (Team v1 cross-company
  parity is a separate pre-existing gap).
- Optional data-hygiene: consider making `HomeMaterialiserService` / `HomeTypeService` stop stamping
  molecule-scoped HOME instances with a company (or standardise the stamp). Not required for the fix.
