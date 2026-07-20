# Draft Mode — All-Calendars: Implementation Handoff

**Status: IMPLEMENTATION COMPLETE + FULLY TEST-VERIFIED. One user-gated visual check remains (see §5).**
Date: 2026-07-16. All work is in git worktrees; **nothing merged to `dev`/`release`** (per your instruction — a separate session merges).

## 1. What was built
Draft Mode extended from shift-primary-only to **the entire calendar across all three surfaces**, on a hardened foundation. Six pieces (each its own spec in `docs/superpowers/specs/2026-07-16-draft-mode-*.md`):

- **F — Foundation:** shared `DraftSession` (Surface discriminator, nullable MoleculeId/AreaId, per-surface **filtered-unique** "one active draft" indexes), `IDraftReconciler` + `IDraftLifecycle` split, **per-cell-skip** commit (replaces all-or-nothing), commit **re-authorizes per cell**, commit fires **per-surface SignalR**, drift re-check **inside** the tx, one EF migration with **auto-discard** of in-flight Active drafts.
- **A — Shifts + trainees:** trainee staging (parallel baseline/staged columns), trainee-aware changed-cell test + 3rd reconcile pass (gated on primary success), coordinate-keyed `DraftAddTrainee`/`DraftRemoveTrainee` + both `×` paths, 2nd-cube render, dropped `IsTraineeShift`. Fixes the live "commit nulls TraineeUserId" data-loss.
- **B — Other shifts-board entries:** non-shift quick-adds (chore/duty/day-note/text) disabled in shifts draft; capacity widened at commit, never live.
- **C — Chores:** `DraftChoreCell` descriptor-set diff, `DraftChoreService` reconciler, overlay, service-mediated commit + notifications + `chores-{mol}` SignalR, `__draftSessionId` bootstrap on Chores page.
- **D — On-Call:** `DraftDutyCell`, **GLOBAL baseline** (area is a UI filter only — landmine handled + regression-tested), per-duty-type grants, `__draftSessionId` bootstrap on OnCall page.
- **E — Bulk fill/copy-week:** DEFERRED (your choice).

## 2. Verification (all GREEN)
- **Full Release test suite (integrated F+A+B+C+D): 1935/1935 pass** (`dotnet test -c Release -- xUnit.ParallelizeTestCollections=false`). Baseline was 1901; +34 new draft tests (A/B 7, C 15, D 12).
- **Integrated build: 0 errors.**
- **Migration deploy-verified:** the full chain incl. `DraftAllCalendarsFoundation` applies cleanly to a fresh DB (the :5100 app migrated+seeded successfully).
- **All 3 draft surfaces routed + auth-enforced on :5100**; draft JS deployed in served assets.
- Earlier this session, the **core shift-draft render + quick-entry + discard** was browser-verified on :5000 (the render-bug fix).

## 3. Branch / worktree map (for the merge session)
Everything is off `dev` @ `b0de254` (which has the committed specs).
| Piece | Worktree branch | Tip |
|---|---|---|
| F | `worktree-agent-a046c075c1ae159e3` | `68739d8` |
| A+B | `worktree-agent-a57d1e50f6acd513c` | `1cfe75b` |
| C | `worktree-agent-a87b54b148b546c24` | `06e2b2c` |
| D | `feat/draft-mode-D-oncall` | `c866308` |
| **INTEGRATED (all four merged, 1935/1935)** | **`draft-mode-integration`** | **`d2e2ace`** |

**Recommended merge: just merge `draft-mode-integration` (`d2e2ace`)** — it already contains F+A+B+C+D with conflicts resolved and the full suite green. (The individual branches are there if you prefer piecemeal.)

## 4. MERGE HAZARDS (read before merging)
1. **Trainee FK conversion:** F's migration deliberately does NOT convert `ShiftAssignments.TraineeId → TraineeUserId`. That conversion is a pre-existing dev need handled by existing migrations (`FixTraineeForeignKey`, `FixIndexFiltersAndRemoveOrphanedShadowFK`) and/or the unmerged `fix/calendar-molecule-gate` (`c3bff1c`). EF tried to fold it into F's migration off dev; I stripped it so F is deploy-safe standalone. **Ensure the conversion is applied exactly once** by whatever chain you merge.
2. **`packages.lock.json`:** all agents' builds churn it (strips the win-x64 runtime section); each restored it to the `b0de254` baseline. The integrated branch has the correct lockfile — **don't commit any build-time churn to it.**
3. Merging the individual branches (not the integrated one) will conflict in `calendar-inline-edit.js`, `Program.cs` DI, and `_CalendarRow.cshtml` — I already resolved these in `draft-mode-integration` (both additive; kept all sides).

## 5. The one remaining item — your 2-minute visual check
Automated verification is exhaustive, but I could not do the **logged-in browser click-through** of the new UI on the fresh :5100 instance, because it requires typing a password into the login form — which I don't do (safety rule), and you were asleep.

**To do it (the integration app is running on http://localhost:5100 with its own seeded DB):**
1. Open http://localhost:5100 and log in (e.g. `test.owner@shifty.test` / your test password, or `owner@test.com`).
2. **Shifts:** `/Calendar/Shifts?DraftMode=true` → Quick Entry → assign a worker (private), add a trainee (2nd cube), Discard (live restored), re-stage, Save Draft.
3. **Chores:** `/Calendar/Chores` → enter draft → stage a chore → Discard/Save.
4. **On-Call:** `/Calendar/OnCall` → enter draft → assign a duty → Discard/Save.

If the :5100 app is down, relaunch from the integration worktree:
`cd .claude/worktrees/draft-integration && ASPNETCORE_URLS=http://localhost:5100 dotnet run -c Release --no-launch-profile`
(It binds :5100 so it won't clash with your dev app on :5000.)

Or just tell me to proceed and I'll drive the walkthrough via your existing browser session.

## 6. Decisions & backlog
Product/technical sub-decisions I defaulted (none block) are in `DRAFT-MODE-BACKLOG.md`. Notable: trainee validation *warnings* at commit shipped as **option (a)** — consistent with the existing primary path (warnings don't block; hard errors skip+report). One deferral in C: `GetChoresData` left non-draft-aware because the chores page does a full reload (overlay always re-renders server-side) — adding it would be dead code.
