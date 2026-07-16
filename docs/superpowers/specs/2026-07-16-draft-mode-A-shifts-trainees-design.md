# Draft Mode — Sub-project A: Shifts assignments full draft (trainees)

**Status:** Design, pending review. **Depends on:** [F — Foundation](2026-07-16-draft-mode-F-foundation-design.md). **Verified by:** ShiftsTraineeVerifier (findings G1–G10 referenced below).

Make the shifts board's assignment cell **fully** draft-safe: primary assignments (already done, commit `d6cc762`) **plus trainee shadowing**, plus the shift-clear paths that still leak. This is the headline sub-project and the user's concrete ask.

## 1. Scope

In: staging a trainee onto a staged-or-live primary; removing a staged trainee; rendering the staged trainee as a 2nd cube; committing primary+trainee together with per-cell drift/skip; both shift-`×` paths (desktop fallback hole + touch/bottom-sheet ×); coordinate-keying all three trainee entry paths.

Out: `IsTraineeShift` — **dropped** (G8: no live creation path exists; only a test sets it — `ShiftManager.Tests/.../WhoIsOnShiftServiceTests.cs:283`; staging it is speculative scope). Bulk fill (deferred E).

## 2. The two correctness-critical fixes (from review)

- **G1 (silent data loss):** the commit "changed cell" predicate is primary-only (`DraftModeService.cs:110` Phase-1, `:129` Phase-2). Adding a trainee to an already-assigned primary leaves the primary set unchanged → cell treated as no-op → trainee **discarded at commit**. The predicate must become `primariesEqual && traineesEqual`.
- **G2 (mechanism):** `DraftCell.Encode/Decode` is an int-set (`Models/DraftCell.cs:26-35`) and cannot represent primary→trainee pairs. Needs **new parallel columns**, not an overload.

Also note the **existing live bug** (F Risk #4): today's reconcile nulls `TraineeUserId` on any reassigned slot (`DraftModeService.cs:157-158`) and never restores it — a draft touching a live-trainee cell drops the trainee now. This spec's reconcile fixes it and A ships with a regression test "commit preserves an untouched live trainee."

## 3. Data model

Extend `Models/DraftCell.cs` with two parallel columns (keep the sorted-CSV, string-compare invariant):
- `BaselineTrainees` (string) — sorted CSV of `"primaryUserId:traineeUserId"` tokens for the cell's live pairings at first touch.
- `StagedTrainees` (string) — same, for the desired pairings.

`Encode/Decode` gain a pair-CSV variant. A primary shadows **at most one** trainee (single `TraineeUserId` column on `ShiftAssignment` — G confirmed; Q-D "one trainee per primary" = yes), so the staged structure is `Dictionary<primaryUserId,traineeUserId>`. The `DraftOverlayCell` record (`IDraftModeService.cs:6`, today `UserIds`-only) gains the trainee map so the render overlay can populate it.

## 4. Staging (coordinate-keyed handlers + JS)

New handlers on `Table.cshtml.cs`, keyed by natural coordinates (never `assignmentId`):
- `OnPostDraftAddTraineeAsync({ draftSessionId, shiftTypeId, date, primaryUserId, traineeUserId })` → `StageTraineeAsync`.
- `OnPostDraftRemoveTraineeAsync({ draftSessionId, shiftTypeId, date, primaryUserId })` → `StageTraineeClearAsync`.
Both gated by `OwnsActiveDraftAsync` (mirror `DraftClearRequest`, `Table.cshtml.cs:1929-1935`). `StageTraineeClear(primary)` **also drops that primary's staged trainee** (edge-case ruling 1: a trainee can't outlive its primary; also called when the primary is stage-cleared).

Service (`DraftModeService` as the shifts `IDraftReconciler`): `StageTraineeAsync(sessionId, shiftTypeId, date, primaryUserId, traineeUserId)` writes into `StagedTrainees` after capturing baseline on first touch (baseline must now capture trainees too — `GetLiveCellUsersAsync` gains a companion `GetLiveCellTraineesAsync` selecting `(a.UserId, a.TraineeUserId)` where both non-null).

JS (three paths today all `assignmentId`-keyed → G6/G7):
- Inline `+` picker (`calendar-inline-edit.js:631,675-682`) and bottom-sheet add (`calendar-bottom-sheet.js:673-682`): when `window.__draftSessionId` set, POST the new `DraftAddTrainee` with harvested `data-user-id` (primary) + `data-shift-type-id` off the chip and `data-date` off the cell. **`extractCellData` must be extended to read `data-user-id`** (G7: it doesn't today — `calendar-bottom-sheet.js:1124-1141`).
- Remove `×` on the trainee cube (`calendar-inline-edit.js:721-726`, `calendar-bottom-sheet.js:729-737`): branch to `DraftRemoveTrainee`.

Template gates (G6): the trainee `×` and add-`+` are gated `assignment.Id > 0` (`_CalendarRow.cshtml:195,216`), suppressing them on synthetic (negative-Id) staged chips. Extend both gates with the `isDraftShiftChip` pattern already used for the primary `×` (`_CalendarRow.cshtml:130-131`).

**Both primary-`×` paths (from LeakAuditor):** the desktop `×` falls back to live if `data-user-id`/`data-shift-type-id` are missing (`_CalendarRow.cshtml:227-232`) — ensure staged chips always emit them. The **touch/bottom-sheet `×` is hard-coded live** (`calendar-bottom-sheet.js:1051`) — add the `DraftClear` branch there too.

## 5. Rendering overlay

`ApplyDraftOverlayAsync` (`Shifts.cshtml.cs:988`) already sets `.User` on synthetic assignments (fix `d6cc762`). Add: from the overlay cell's staged trainee map, set `TraineeUserId` + `Trainee` nav (resolve names like the existing `stagedUsersById`, `Shifts.cshtml.cs:1021-1025`). `BuildCellsForShiftType` already reads `a.TraineeUserId`/`a.Trainee?.DisplayName` (`:1110-1111`) → renders the 2nd cube.

## 6. Reconcile (implements F §4 `IDraftReconciler` for shifts)

Extend `ReconcileCellAsync` with a **third pass** after primaries settle (G3): for each staged `primary→trainee`, if that primary was **successfully** assigned this cell (a slot exists), set `slot.TraineeUserId = trainee`; else skip and emit a `DraftValidationIssue` (G5 — trainee gated on primary success). Validation uses a **new overload** `ValidateTraineeAssignmentAsync(traineeUserId, primaryUserId, shiftInstanceId)` that doesn't need a persisted `assignmentId` (G4 — today it loads by id, `ShiftAssignmentService.cs:232-242`; a freshly created slot may be unsaved). Trainee-drift is caught by the F per-cell drift check now that baseline+staged include trainees (edge-case ruling 4).

## 7. Edge-case rulings (from review)

1. Primary stage-cleared but trainee staged → drop the trainee (StageTraineeClear on primary clear).
2. Trainee onto a primary that fails commit validation → skip trainee + `DraftValidationIssue` (G5).
3. `IsTraineeShift` + `TraineeUserId` both set → impossible; `IsTraineeShift` is out of scope (§1), so N/A.
4. Live trainee removed by another user between enter and commit → per-cell conflict → cell skipped + reported (F policy).
5. Cross-molecule/wrong-role trainee → `ValidateTraineeAssignmentAsync` only *warns* (`ShiftAssignmentService.cs:288-308`). **Open decision Q-B (below): how warnings surface at non-interactive commit.**

## 8. Open decisions for the human

- **Q-B (trainee validation warnings at commit):** self-training / wrong-role / different-molecule are warnings, not hard errors, and draft commit can't do the live interactive override handshake (`Table.cshtml.cs:1411-1431`). Options: (a) apply silently, (b) downgrade to a `DraftValidationIssue` shown in the commit report [recommended], (c) block. Recommend (b) — consistent with the per-cell skip/report UX.

## 9. Testing

- Unit (real SQLite): stage trainee on already-assigned primary → commit → trainee applied (regression for G1); commit preserves an untouched live trainee (regression for F Risk #4); trainee skipped when its primary hard-errors (G5); StageTraineeClear on primary clear drops the trainee; trainee-drift → cell skipped (F policy). Extend `DraftOverlayRenderTests` for the 2nd-cube render of a staged trainee.
- Browser: draft mode → add trainee via inline `+` and bottom sheet → 2nd cube renders → discard restores live (no trainee leak) → re-stage → commit → live shows trainee + other session notified.
