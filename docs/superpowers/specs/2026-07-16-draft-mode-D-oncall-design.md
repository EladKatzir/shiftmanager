# Draft Mode — Sub-project D: On-Call calendar

**Status:** Design, pending review. **Depends on:** [F — Foundation](2026-07-16-draft-mode-F-foundation-design.md). **Verified by:** OnCallVerifier + ArchReviewer.

Extend Draft Mode to the On-Call calendar. The draft *machinery* ports cleanly and `DraftDutyCell` is actually *simpler* than `DraftCell` — but the **scope model is fundamentally different** and is the correctness landmine.

## 1. On-call model (verified)

- **Entity `OnDuty`** (`Models/OnDuty.cs:18-81`): `UserId` (non-null — one user per row), `Date`, `Type` (`OnDutyType` enum), `Notes`, soft-delete `CanceledAt/By` (`IsActive => CanceledAt == null`, `:80`).
- **GLOBAL entity — no CompanyId, no MoleculeId, no AreaId** (`AppDbContext.cs:799` "Global/Public Table", `:993` "OnDuty does NOT have query filter").
- Duty-type space: built-in `OnDutyType` (Hakam=0, Lead=1) **plus** custom `OnDutyTypeConfig` (`TypeValue > 1`, global, may `RequireOfficerRank`).
- **Cell = (dutyTypeValue, WorkDate)**, a **set of users** (`OnCall.cshtml.cs:427-519`; row `dutytype-{TypeValue}` `:431`). Multiple different users per (type,date) allowed (unique index is `(UserId,Date,Type,CanceledAt)` — only blocks the same user twice, `AppDbContext.cs:817-820`).
- **Scope: area is a FILTER, not a data boundary.** On-duties are fetched globally then filtered in memory to users whose company→molecule→area matches `AreaId` (`OnCall.cshtml.cs:349-374`). Any MoleculeId in the write path is only for HMAC override-token matching (`OnDutyService.cs:565-569`).
- Grant: **OR-chain, per duty type** — `ManageOnDuty || AssignHakamDuties || AssignKatzinDuties || EditOnCallCalendar` (`OnCall.cshtml.cs:157-160`, `OnDutyService.CanUserManageOnDutyAsync`). SignalR: `NotifyOnCallChangedAsync` + group `oncall-{areaId}` exist but **`NotifyOnCallChangedAsync` is never called today** — the live write path broadcasts nothing.
- **The On-Call page has ZERO draft awareness** — never sets `window.__draftSessionId`.

## 2. Data model — `DraftDutyCell`

Mirror `DraftCell` almost exactly (simpler — no instance/slot):

| Column | Type | Note |
|---|---|---|
| `Id`, `DraftSessionId` (FK) | | |
| `DutyTypeValue` | int | covers built-in enum + custom config uniformly (the int the calendar already keys on) |
| `WorkDate` | DateOnly | |
| `BaselineUserIds` | string | sorted CSV — **reuse `DraftCell.Encode/Decode` verbatim** |
| `StagedUserIds` | string | sorted CSV |

Unique filtered index `(DraftSessionId, DutyTypeValue, WorkDate)`.

## 3. Scope — the correctness landmine

**Baseline / staged-target / live-now MUST be computed over the GLOBAL active set for `(dutyTypeValue, date)`** — `_db.OnDuties.IgnoreQueryFilters().Where(o => (int)o.Type == dutyTypeValue && o.Date == date && o.CanceledAt == null)` — and **must NOT** apply the area filter. If baseline is captured over the area-filtered subset, commit silently clobbers or ignores out-of-area users on the same `(type,date)`. `DraftSession.AreaId` is a **UI convenience only** (which rows/pickers to show), never a data boundary. **Accepted consequence:** two owners "drafting different areas" for Hakam on the same date edit the **same global cell** and legitimately conflict at commit — correct, not a bug. **A regression test must assert global baseline** (an out-of-area user on the same (type,date) is preserved through commit).

`DraftSession` gains `AreaId` (F §3); for on-call `MoleculeId`/`JobTypeId` are null. Session scope key: `(Owner, OnCall, AreaId, WeekStart)`.

## 4. Staging (coordinate-keyed)

New `IDraftReconciler` for on-call (`DraftDutyService`) + page handlers on `OnCall.cshtml.cs`:
- `StageDutyAssignAsync(draftSessionId, dutyTypeValue, date, userId)`
- `StageDutyClearAsync(draftSessionId, dutyTypeValue, date, userId)`
- Handlers `OnPostEnterDraftAsync`/`OnPostDraftDutyAssignAsync`/`OnPostDraftDutyClearAsync`/`OnPostCommitDraftAsync`/`OnPostDiscardDraftAsync` (analogs of `Table.cshtml.cs:1874-1919`).

Coordinates already in the DOM (no markup change): `data-row-id="dutytype-{N}"`, cell `data-date`, chip `data-user-id` (`_CalendarRow.cshtml:85-86,139`). JS: `quickAddOnDuty` (`calendar-inline-edit.js:307-375`) and the on-duty `×` (`deleteItem('onduty')` `:785-789`, keyed by `OnDuty.Id` today) branch to the new handlers when `window.__draftSessionId` set, **before** the live `/Api/Calendar/*` call (pattern: shift `×` `:1120-1132`). **Bootstrap `window.__draftSessionId` on `OnCall.cshtml`** + draft UI. Note on-call writes go to `/Api/` today (no page-handler write host) — the draft handlers live on the OnCall page and the JS branches before the API.

Draft-entry auth gate = the OR-chain above.

## 5. Reconcile (implements F §4 for on-call)

Per changed cell (drift-checked per F, **globally**):
- `staged − live` → create an `OnDuty` row per userId (reuse officer-rank + BusyService validation in `CreateOnDutyAsync`; hard error / rank-ineligible → skip + `DraftValidationIssue`, per F).
- `live − staged` → find active `OnDuty` for `(userId, type, date)` → `CancelOnDutyAsync` (soft-delete). No empty-slot reuse.
- Add `CreateForActor/CancelForActor(actingUserId, …)` overloads so commit passes `actingUserId` explicitly instead of re-reading HttpContext + re-running the grant chain N times (`OnDutyService.cs:108-112,268`).
- **Commit auth (F §7):** re-check `CanUserManageOnDutyAsync` **per duty type** at commit.
- **SignalR:** on-call has **no obligation today** (`NotifyOnCallChangedAsync` never called — parity). Optionally wire it + `oncall-{areaId}` if live real-time is wanted later; not required for this spec.

## 6. Edge cases / rulings

- Officer-rank gate (`EnforceRankEligibility` → Lead/Katzin need rank ≥ SegenMishne, `OnDutyService.cs:272-287`): stage freely, enforce at commit → ineligible = skipped-with-issue.
- No Hakam/Lead exclusivity exists (per-`(user,date,Type)` uniqueness) → no draft-structural constraint; same-day concern is a BusyService warning handled by commit validation.
- "Built-in read-only" = the DutyTypes **admin config** page, not assignment; Hakam/Lead are fully assignable → no draft restriction.

## 7. Open decisions for the human

- **On-call session granularity (ArchReviewer Q3):** one session covering mixed duty types (Hakam + Katzin) whose assign grants differ per type, vs one session per duty type. Recommend **one session, per-cell grant by duty type** (matches how the page renders all duty rows together; F already re-authorizes per cell).
- **All-areas draft** (`AreaId = null`, `ViewAllAreas`): does it coexist with a per-area draft for the same week? Recommend `AreaId=null` is its own scope key value (a distinct "all" session).
- **Busy-override / vacation warnings at commit** (can't prompt): auto-skip-and-report [recommended] vs auto-force with audit flag.
- **Duty `Notes`:** in scope for staging (making the conflict unit `(userId,note)`) or out (staged assignments commit with null notes)? Recommend **out** for D (note-editing is a B-class concern).

## 8. Testing

- Unit (real SQLite): stage duty into empty cell → commit creates OnDuty; stage-clear → soft-delete; **global-baseline regression** (out-of-area user on same (type,date) preserved through commit); rank-ineligible Lead → skipped+issue; drifted cell → skipped+reported; commit re-checks per-duty-type grant.
- Browser: On-Call page draft → assign duty (private) → discard restores → re-stage → commit → live.
