# Draft Mode — Sub-project C: Chores calendar

**Status:** Design, pending review. **Depends on:** [F — Foundation](2026-07-16-draft-mode-F-foundation-design.md). **Verified by:** ChoresVerifier.

Extend Draft Mode to the Chores calendar. The lifecycle/commit machinery (F) transfers cleanly; the **cell representation** and **reconcile operations** do not — chores are a genuinely richer cell than shifts.

## 1. Chores model (verified)

- **The `Chore` row IS the assignment** — no instance+slot split (`Models/Chore.cs:9-92`). One `Chore` = one user, one date.
- Coordinates: `UserId` (`:29`), `Date` (`:34`), `MoleculeId?` (`:19`), `CompanyId` (`:13`).
- Rich content (the crux): `Title` free-text **required** (`:39`), `ChoreTypeId?` optional (`:24`), `StartTime?`/`EndTime?` (`:45,:50`), `Notes?` (`:55`), `WeightMinutes` frozen fairness weight (`:58`, derived at create from times/type — `ChoreService.cs:348, ResolveWeightMinutes:66`).
- Soft-delete: `CanceledAt`/`CanceledBy`; `IsActive => CanceledAt == null` (`:91`).
- **Cell = (user row, date column)** — opposite axis from shifts. `BuildCellsForUser` (`Pages/Calendar/Chores.cshtml.cs:533-636`) → a **list** of chores per (user,date). **0..N per cell** (index `(CompanyId,UserId,Date)` is non-unique, `AppDbContext.cs:555-556`; a duplicate is an overrideable warning, not an error — `BusyService.cs:324-338`).
- Grant: **`AssignChores`** (molecule scope, `GrantTypeSeed.cs:52`; page `Chores.cshtml.cs:162`). SignalR: `chores-{moleculeId}` (`CalendarHub.cs:376`), **no jobType axis**.
- **The Chores page has ZERO draft awareness** — no toggle, no lifecycle, never sets `window.__draftSessionId`.

## 2. Data model — `DraftChoreCell`

New table mirroring `DraftCell` but with a **descriptor-set** payload (not int-CSV):

| Column | Purpose |
|---|---|
| `Id`, `DraftSessionId` (FK cascade) | as DraftCell |
| `UserId` (int) | row coordinate |
| `WorkDate` (DateOnly) | column coordinate |
| `BaselineChores` (string) | canonical serialization of the live chore-descriptor set at first touch |
| `StagedChores` (string) | canonical serialization of the desired descriptor set |

Unique filtered index `(DraftSessionId, UserId, WorkDate)` (mirrors `AppDbContext.cs:1467-1469`).

**Descriptor** = user-authored identity only: `{ChoreTypeId, Title(trim), StartTime, EndTime, Notes(trim)}`. **Exclude** `Id`, `CreatedAt`, `CreatedBy`, `WeightMinutes` (volatile/derived). Serialize each descriptor → stable string; **sort the set, join** → equality is a plain string compare, preserving F's conflict primitive (`staged==baseline` no-op; `liveNow==baseline` safe; else drift → skip+report). `GetLiveCellChoresAsync(userId,date)` reads active chores → descriptor set → canonical string (analog of `DraftModeService.GetLiveCellUsersAsync:45-49`). **The diff unit is a set of rich descriptors, not ints** — the single most important adaptation.

## 3. Staging (coordinate-keyed)

New `IDraftReconciler` for chores (`DraftChoreService`) + page handlers on `Chores.cshtml.cs`:
- `DraftChoreStage({ draftSessionId, userId, date, title, choreTypeId?, startTime?, endTime?, notes? })` → add a staged descriptor.
- `DraftChoreClear({ draftSessionId, userId, date, descriptorKey })` → remove one staged descriptor. `descriptorKey` is a synthetic stable key the overlay emits per staged chore (`choreTypeId|title` or per-cell ordinal), since staged chores have **no `Chore.Id`** (the `×`/delete today is keyed by `Chore.Id` — `deleteItem` `calendar-inline-edit.js:785-789` — and cannot touch a staged chore). For the dominant one-chore-per-cell case, clearing the whole cell suffices.

JS: `quickAddChore` (`calendar-inline-edit.js:235-255`) and the `direct-chore` quick-entry path (`calendar-quick-entry.js:633,1153`) branch to `DraftChoreStage` when `window.__draftSessionId` set; the chore `×` branches to `DraftChoreClear`. **Bootstrap `window.__draftSessionId` on `Chores.cshtml`** (+ draft toggle/banner/commit/discard UI, mirroring `Shifts.cshtml:706-770`).

## 4. Rendering overlay

Add a `GetChoresData`/`Chores.cshtml.cs` draft overlay (analog of `ApplyDraftOverlayAsync`): for touched cells, replace the live chore list with the staged descriptor set rendered as chips (synthetic, non-persisted, carrying the descriptorKey for the `×`). `GetChoresData.cshtml.cs:93-101` rebuilds from live `_db.Chores` and must be intercepted too.

## 5. Reconcile (implements F §4 for chores)

Per touched cell (`staged != baseline`, drift-checked per F inside the tx):
1. Load live active chores for (user,date) → descriptor set.
2. **Remove:** each live descriptor not in staged → `CancelChoreAsync(chore.Id)` (soft-delete — NOT slot-null).
3. **Add:** each staged descriptor not live → `CreateChoreAsync(...)` (re-validates via BusyService, freezes weight, inserts). Hard error (e.g. user now has a live shift that day) → skip + `DraftValidationIssue`, apply the rest.
4. **Preserve:** descriptor in both → leave the existing `Chore` untouched (keeps Id/CreatedAt/weight, avoids re-firing notifications).

**Notifications (F Risk #3 + ChoresVerifier #3):** chore create/cancel fire notifications in the **API pages, not the service** (`QuickAddChore.cshtml.cs:213`, `DeleteChore.cshtml.cs:111`). Commit calls the service directly → must fire `CreateChoreAssignedNotification`/`CreateChoreCanceledNotification` itself, then broadcast `chores-{moleculeId}` once (per F §5.6). **Commit auth:** re-check `AssignChores` per cell (F §7).

## 6. Open decisions for the human

- **Descriptor identity:** include `Title` (retyping a title = different chore) vs type-only? Recommend include (matches what the user authored); two identical descriptors collapse to one (fungible — acceptable).
- **`DraftChoreClear` key** for the multi-chore-per-day case: ordinal-within-cell vs `choreTypeId|title` hash. Recommend ordinal (stable within a render).
- **Bulk-commit notifications:** one aggregated per affected user vs one per created/canceled chore (today's per-action behavior). Recommend per-action for parity, revisit if noisy.

## 7. Testing

- Unit (real SQLite): stage a chore into an empty cell → commit creates it; stage-remove a live chore → commit soft-deletes it; descriptor present in both → commit leaves the row (Id unchanged); drifted cell (live chore added elsewhere) → skipped + reported; commit re-checks `AssignChores`; commit fires the chore notification + `chores-{mol}` group.
- Browser: Chores page draft → add chore (private) → other session's chores board unchanged → discard restores → re-stage → commit → live + notified.
