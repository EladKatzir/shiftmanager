# Draft Mode — Foundation (Spec F)

**Status:** Design, pending review. **Branch target:** `dev`. **Depends on:** nothing. **Depended on by:** A, B, C, D (and deferred E).

This is the shared contract for extending Draft Mode across all three calendars. Sub-projects A (shifts+trainees), B (other shifts-board entries), C (chores), D (on-call) all build on the machinery defined here. Authored from a 5-agent adversarial review of the existing shift-only draft system; every risk below is anchored to real code.

## 1. Goal & non-goals

**Goal.** A private, per-assigner sandbox over one calendar surface + scope + week, in which every mutation is staged (no live write, no SignalR) until an explicit Commit reconciles the touched cells into live with per-cell conflict detection, per-cell re-authorization, and per-surface real-time notification.

**Non-goals (this spec):** the per-entity cell models and staging/rendering for each surface (owned by A/C/D); disabling non-shift quick-adds on the shifts board (B); bulk fill (deferred E); the legacy `Table.cshtml` grid (out of scope — draft entry is gated behind the Excel-calendar flag, see §9).

## 2. Decisions locked (by product owner)

1. **Conflict policy = per-cell skip + report.** Commit applies every conflict-free cell and skips only cells whose live state drifted from the captured baseline, returning the skipped set for review/re-stage. Replaces today's all-or-nothing abort (`Services/DraftModeService.cs:115-116`).
2. **Commit scope = per-calendar (per-surface).** Each surface's draft commits independently. No cross-calendar atomic transaction. (A future "Save all" is a UI fan-out over independent commits — not in scope.)
3. **Deferred:** bulk fill (E).
4. **Adopted engineering defaults:** grant re-check at commit; commit fires per-surface SignalR; drift re-check inside the commit transaction; auto-discard in-flight Active drafts on the schema migration; draft entry gated behind the Excel-calendar flag.

## 3. Data model — shared `DraftSession`

Extend `Models/DraftSession.cs` (today: `OwnerUserId, MoleculeId(non-null), JobTypeId?, WeekStart, WeekEnd, Status, CreatedAt`).

Add:
- `DraftSurface Surface` — enum `{ Shifts = 0, Chores = 1, OnCall = 2 }`.
- Make `MoleculeId` **nullable** (`int?`) — on-call has no molecule.
- Add `int? AreaId` — on-call scope (presentation hint for on-call; see D). Null for shifts/chores.
- `JobTypeId` stays (shifts only; null for chores/on-call).

**Scope tuple per surface** (the "one active draft per …" key):
- Shifts: `(OwnerUserId, Surface=Shifts, MoleculeId, JobTypeId, WeekStart)`
- Chores: `(OwnerUserId, Surface=Chores, MoleculeId, WeekStart)` (JobTypeId null)
- On-Call: `(OwnerUserId, Surface=OnCall, AreaId, WeekStart)` (MoleculeId null; AreaId is a **UI hint only** — on-call data is global, see D)

**Uniqueness (fixes Risk #5 — single-active invariant unenforced).** Today the index `(OwnerUserId, MoleculeId, Status)` is **not unique** and omits period/jobType/surface (`Data/AppDbContext.cs:1452-1453`), so two tabs → two Active sessions → `GetActiveDraftAsync` FirstOrDefaults an arbitrary one. Replace with **per-surface filtered-unique indexes** (`WHERE Status = 0/Active`) over each surface's full scope tuple. SQLite supports filtered indexes. This makes "one active draft per scope" a DB invariant, not a check-then-insert race (`Services/DraftModeService.cs:21-29`).

## 4. Shared lifecycle helper (the thin core)

Per ArchReviewer: **three parallel draft features sharing a thin lifecycle helper**, not one service pretending three aggregates are one. Introduce a surface-agnostic orchestrator; each surface plugs in a reconciler.

```
interface IDraftReconciler            // implemented per surface (A: shifts, C: chores, D: on-call)
{
    DraftSurface Surface { get; }
    // Capture the live baseline for a cell (surface-specific canonical string).
    Task<string> CaptureBaselineAsync(DraftScope scope, CellKey cell);
    // Read the CURRENT live canonical string for drift detection (called again inside the tx).
    Task<string> ReadLiveAsync(DraftScope scope, CellKey cell);
    // Re-authorize this specific cell for the acting user AT COMMIT (grant may have been revoked).
    Task<bool> AuthorizeCellAsync(int actingUserId, DraftScope scope, CellKey cell);
    // Apply staged→live for one cell inside the shared transaction; return per-user issues.
    Task<IReadOnlyList<DraftValidationIssue>> ReconcileCellAsync(int actingUserId, DraftScope scope, DraftCellState staged);
    // The SignalR group(s) this cell's commit must notify.
    IEnumerable<string> NotifyGroupsFor(DraftScope scope, CellKey cell);
}

interface IDraftLifecycle             // shared; owns enter/discard/commit orchestration
{
    Task<DraftSession> EnterAsync(int ownerUserId, DraftScope scope);   // idempotent (returns existing Active)
    Task DiscardAsync(int draftSessionId, int ownerUserId);
    Task<DraftCommitResult> CommitAsync(int draftSessionId, int actingUserId);
}
```

`DraftScope` carries `{ Surface, MoleculeId?, JobTypeId?, AreaId?, WeekStart, WeekEnd }`. `CellKey` is the surface's coordinate (shifts: `(shiftTypeId, date)`; chores: `(userId, date)`; on-call: `(dutyTypeValue, date)`). The existing `DraftModeService` becomes the **shifts** `IDraftReconciler` implementation; the commit *orchestration* moves into `IDraftLifecycle`.

## 5. Commit algorithm (hardened)

`CommitAsync(draftSessionId, actingUserId)` for one surface:

1. Load the Active session (owner + status checked). If none → no-op result.
2. Load all touched cells for the session.
3. `await using tx = BeginTransactionAsync()`. **All of the following is inside the tx** (fixes Risk #6 — TOCTOU: today `liveNow` is read at `DraftModeService.cs:111` but reconcile writes at `:149-183` with no lock between; SQLite ignores `[Timestamp]` — `Models/ShiftAssignment.cs:46-49`).
4. For each touched cell where `staged != baseline`:
   a. **Drift check (per-cell skip):** `live = await reconciler.ReadLiveAsync(...)`. If `live != baseline` → add cell to `Skipped` (drifted) and **continue** (do NOT abort the commit). *This is the locked per-cell policy.*
   b. **Authorization (fixes Risk #2):** `if (!await reconciler.AuthorizeCellAsync(actingUserId, scope, cell))` → add to `Unauthorized` and continue. Grants are re-checked here, not trusted from stage time.
   c. **Reconcile:** `issues = await reconciler.ReconcileCellAsync(actingUserId, scope, staged)`. Collect `issues` (per-user hard-error skips, mirroring today's `DraftModeService.cs:162-170`). Record the cell in `Committed` and its `NotifyGroupsFor(...)`.
5. Mark session `Committed`. `SaveChangesAsync`. `tx.Commit()`.
6. **After commit succeeds:** fire SignalR once per distinct group in the union of `Committed` cells' notify groups (fixes Risk #3 — today `DraftModeService` has no hub dependency, `:12-19`, and commit fires nothing, `Table.cshtml.cs:1892-1907`).
7. Return `DraftCommitResult`.

**Revised `DraftCommitResult`** (extends today's `(Committed, AppliedCells, Conflicts, ValidationIssues)` in `Services/IDraftModeService.cs:19`):
```
record DraftCommitResult(
    bool Committed,                         // session moved to Committed
    IReadOnlyList<CellRef> Applied,         // cells reconciled to live
    IReadOnlyList<CellRef> Skipped,         // drifted since baseline — re-stage these
    IReadOnlyList<CellRef> Unauthorized,    // grant revoked since stage
    IReadOnlyList<DraftValidationIssue> ValidationIssues, // per-user hard-error skips within applied cells
    IReadOnlyList<string> NotifiedGroups);  // for diagnostics/tests
```
`CellRef` = surface + coordinate + a localized label, so the UI can render the "these N cells changed underneath you" report.

## 6. Coordinate-keyed handler convention (the core invariant)

When `window.__draftSessionId` is set, **every** calendar mutation routes to a `Draft*` handler keyed by **natural coordinates** (row-key + date + payload) — never a DB `assignmentId`/`instanceId`/`choreId`/`onDutyId`, because staged cells have no persisted rows. `DraftClear` already does this (`Table.cshtml.cs:1882`); A/B/C/D make every other path conform. The render templates already emit the needed coordinates as `data-*` (`Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml:85-86,139-142`); the JS must harvest them and branch to the `Draft*` handler *before* hitting the live endpoint (pattern: the desktop `×` at `calendar-inline-edit.js:1120-1133`).

Each surface page must **bootstrap `window.__draftSessionId`** (today only `Shifts.cshtml:706`; C/D add it to `Chores.cshtml`/`OnCall.cshtml`).

## 7. Security

- **Commit is the authorization boundary** (§5b). Per-surface, per-cell: shifts → `AssignShifts` scoped to the cell's company+jobType; chores → `AssignChores` (molecule); on-call → `CanUserManageOnDutyAsync` **per duty type** (OR-chain `ManageOnDuty || AssignHakamDuties || AssignKatzinDuties || EditOnCallCalendar`, `Pages/Calendar/OnCall.cshtml.cs:157-160`). Stage-time checks remain (fail fast) but are not trusted at commit.
- **Tenant scoping.** `IgnoreQueryFilters()` stays (molecule/global reconciliation) but every reconciler must gate by the draft's scope. Shifts: verify `request.ShiftTypeId` belongs to the draft's molecule (today unverified — `DraftModeService.cs:45-49`, warning-level Risk). On-call: baseline/live are **global** by design (D).
- **CompanyId stamping (Risk #7).** When creating new rows at commit, set `CompanyId` explicitly; add a test for the cross-company molecule case where `st.GetEffectiveCompanyId(actingCompanyId)` could yield 0 and `CompanyIdInterceptor` (`Data/CompanyIdInterceptor.cs:98`) would mis-stamp the acting user's tenant.

## 8. Migration

Additive schema: `DraftSession` gains `Surface`, nullable `MoleculeId`, `AreaId`; new per-surface cell tables (A/C/D); new filtered-unique indexes. **In-flight Active drafts are auto-discarded on deploy** (drafts are ephemeral sandboxes; back-filling baselines/trainees into a schema they were never captured under is unsafe and pointless — ArchReviewer Risk #4/§4). The migration `DELETE`s `DraftSession WHERE Status = Active` (cascade removes cells) before adding the unique indexes, so existing duplicable rows can't violate the new constraint.

## 9. Scope gate

Draft entry is available **only when the Excel-calendar feature flag is ON** (`Pages/Shared/_Layout.cshtml:47-55`). The legacy `Table.cshtml` grid (its ~13 live handlers, fill-handle, roster-dock) is **out of scope**; it shares endpoints with the Excel calendar, so the `Draft*` branches added by A–D live in the shared handlers but are only reachable from the Excel rendering (which sets `__draftSessionId`).

## 10. Testing

- Unit (real SQLite, per existing `DraftModeServiceTests` / `DraftOverlayRenderTests` pattern): per-cell-skip commit (some applied, some skipped, session still Committed); drift detected *inside* the tx (simulate a live write between capture and commit); commit re-authorization rejects a revoked-grant cell; `DraftCommitResult` carries correct Applied/Skipped/Unauthorized sets; filtered-unique index blocks a second Active session for the same scope.
- Migration test: an Active draft present pre-migration is gone post-migration; the unique index exists.
- SignalR: assert `NotifiedGroups` equals the union of applied cells' groups (no live hub needed — assert the computed set).
- Browser (per surface, in its own sub-project): stage → private (other session's board unchanged) → commit → live updates + other session sees it via SignalR → conflict path shows the skip report.

## 11. Open items deferred to sub-projects

- Exact per-surface cell table columns + canonical baseline serialization: A (`DraftCell` trainee columns), C (`DraftChoreCell` descriptor set), D (`DraftDutyCell`, global baseline).
- The "one active draft" filtered index column list per surface: finalized in each sub-project against this §3 tuple.
