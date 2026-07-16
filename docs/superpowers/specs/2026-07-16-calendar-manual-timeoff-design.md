# Design: Manual Time-Off Entry on People-Row Calendars

**Date:** 2026-07-16
**Branch:** `feat/calendar-manual-timeoff` (worktree off `dev` @ `b0de254`)
**Status:** Approved design → implementation

---

## 1. Problem & Goal

Today a user's vacation can only be created by submitting a request at `/My/Requests` and having it approved. Managers want to **enter time-off directly on the calendar**, for a specific person and day, without the request/approval round-trip.

Three kinds of time-off, matching the existing `TimeOffType` enum exactly:

- **Vacation** — a full day off.
- **Day at `<location>`** — a full day off tracked with a free-text location label (e.g. "day at clinic"), not counted against the vacation quota.
- **After** — an afternoon-onward partial leave.

The feature applies **only to calendars whose rows are people**:

- `/Calendar/Overview`
- `/Calendar/Team`
- `/Calendar/Shifts?...&Mode=user` (the "by-people" mode)

It is **not** offered in by-shift modes (rows = shift types).

## 2. Key Decisions (settled with the user)

| # | Decision | Choice |
|---|----------|--------|
| D1 | What happens on entry | **Immediately effective (full):** create an already-**Approved** `TimeOffRequest`, run the same side-effects a real approval runs (remove the person's conflicting shift assignments, cancel trainee shadowing, notify), and materialise the HOME busy chips. The board tells the truth. |
| D2 | Who may enter it | **Anyone who can edit the calendar** — the existing calendar-edit gate (`WriteOverviewNotes` OR `AssignShifts`/`AssignChores`/`ManageOnDuty`/`EditOnCallCalendar`), i.e. `IGrantService.HasCalendarNotePermissionAsync`. **Server-side scope-checked against the target person's company** (IDOR guard). |
| D3 | Interaction | Mirror the existing **Quick-Entry toggle**: **ON → type** `vacation` / `after` / `day at <location>` into the inline combobox; **OFF → pick** Vacation / Day at… / After from the bottom-sheet dropdown, exactly like assigning a shift. Both on all three people-row boards. |
| D4 | Morning-after boundary | **Keep 13:00** — the shared `HOME_AM` shift-type is already `00:00–13:00` app-wide. No change to shift-type seeding. |

## 3. Reused domain (no new data model)

The vacation subsystem already exists; we add an **entry path**, not a new model.

- **Entity:** `Models/TimeOffRequest.cs` (`IBelongsToCompany`) — `UserId`, `CompanyId`, `StartDate`/`EndDate` (DateOnly), `Type` (`TimeOffType`), `Label` (DayAt location), `Status` (`RequestStatus`), `ApproverId`, dual-approval audit fields, `CreatedAt`.
- **Enum:** `Models/Support/Enums.cs` — `TimeOffType { Vacation = 0, After = 1, DayAt = 2 }`.
- **Approval side-effects:** `VacationApprovalService.ProcessApprovalSideEffectsAsync(requestId)` — removes overlapping `ShiftAssignments`, cancels trainee shadowing (`ITraineeService.CancelShadowingForTimeOffAsync`), sends the approval notification.
- **Materialiser:** `HomeMaterialiserService.SyncMaterialisedHomeRowsAsync(requestId)` — writes HOME/HOME_AM/HOME_PM `ShiftAssignment` rows tagged `SourceTimeOffRequestId`; auto-creates the HOME shift-type + shift-instance on demand; **idempotent** and **transaction-aware**. Gated by feature flag `FF_HOME_UNIFICATION` (**enabled by default** via `FeatureFlagSeed`).
- **Rendering (already done — no work):** the palm-tree / "יום {Label}" overlay is computed live from Approved `TimeOffRequests` (`OverviewCalendarBuilder.LoadVacationsAsync`, `ShiftCalendarService.GetOverlaysAsync`); HOME chips render from the materialised assignments. All three surfaces render through the same `_CalendarRow.cshtml`, so once the record + materialised rows exist, they display everywhere with zero rendering changes.

### 3.1 Day-math (produced by the materialiser — confirmed against tests)

For a single-day entry on day **D** (`StartDate == EndDate == D`):

| Type | Day D | Day D+1 |
|------|-------|---------|
| **Vacation** | `HOME` 00:00–23:59 (whole day) + live palm-tree overlay | `HOME_AM` 00:00–13:00 (busy morning) |
| **Day at `X`** | `HOME` 00:00–23:59 + "יום {X}" overlay | `HOME_AM` 00:00–13:00 |
| **After** | `HOME_PM` 16:00–23:59 (busy from 16:00) | `HOME_AM` 00:00–13:00 |

Conflicting shift assignments on D are removed by `ProcessApprovalSideEffectsAsync`. If `FF_HOME_UNIFICATION` were ever off, the palm-tree/label overlay still shows (it reads `TimeOffRequests` directly); only the busy chips would be absent — so we **call the materialiser flag-gated**, matching every existing caller.

## 4. Architecture

Both input modes converge on **one new primitive**: `window.quickAddTimeOff(date, userId, type, label)` → one new endpoint `POST /Api/Calendar/QuickAddTimeOff` → one new service method. Build that once; wire two thin front-ends and three page hookups.

### 4.1 Server

**New service method** — `IVacationApprovalService` / `VacationApprovalService`:

```
Task<(bool Success, int? RequestId, string? Error, string? ErrorKey)>
    CreateApprovedManualTimeOffAsync(
        int targetUserId, int companyId, TimeOffType type,
        DateOnly startDate, DateOnly endDate, string? label, int actorUserId);
```

Behavior:
1. Authorize: actor holds the calendar-edit grant (`HasCalendarNotePermissionAsync`) **scoped to `companyId`**, and the actor may act on that company (reuse existing company-access validation used by the approval pages). Reject otherwise.
2. Validate the **target user is a member of `companyId`** (anti-IDOR) and is active.
3. Normalize per type (mirror `Pages/My/Requests.cshtml.cs` OnPostTimeOff): **After** → `EndDate = StartDate`; **DayAt** → single day + **Label required** (reject if blank); **Vacation** → `Label = null`. For manual cell entry `StartDate == EndDate ==` the clicked date.
4. Reject if the user already has an **Approved** `TimeOffRequest` overlapping that date (avoid duplicate/whipsaw); return a localized error.
5. Create `TimeOffRequest { Status = Approved, ApproverId = actor, FirstApprovalActorId = actor, FirstApprovalActedAt = now, CompanyId, UserId, Type, StartDate, EndDate, Label(DayAt only), CreatedAt = now, Reason = "Entered on calendar" }` inside a transaction (`BeginTransactionAsync` + `SaveWithConcurrencyHandlingAsync`).
6. `await ProcessApprovalSideEffectsAsync(request.Id)` (removes conflicting shifts, trainee shadow cleanup, notification).
7. If `FeatureFlagService.IsEnabledAsync(FF_HOME_UNIFICATION)` → `await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id)`.
8. Commit; return the id.

No leave fan-out (manual entry is single-company by construction; the actor picks one company's calendar). No dual-approval state machine — this is an authorized direct action.

**New endpoint** — `Pages/Api/Calendar/QuickAddTimeOff.cshtml(.cs)`, cloned from `QuickAddDayNote.cshtml.cs`:
- `[Authorize]` + `[IgnoreAntiforgeryToken]`; `OnPostAsync` reads the JSON body manually.
- Request DTO: `{ date: "yyyy-MM-dd", userId: int, type: "vacation"|"after"|"dayat", label?: string }`.
- Resolve company via `_tenantResolver.GetCurrentTenantId()`; parse actor id from `ClaimTypes.NameIdentifier` (`int.TryParse`).
- Map `type` string → `TimeOffType`; call `CreateApprovedManualTimeOffAsync`.
- Audit via `IAuditLogService` (action `"TimeOffEnteredOnCalendar"`).
- Return `JsonResult`: success `{ success:true, requestId }`; failures set 400/401/403 with localized `ApiErrorResponse`.
- **No `Program.cs` / `ApiAuthenticationMiddleware` change** — the `/Api/Calendar` prefix is already whitelisted as an authenticated internal endpoint; CSRF is enforced by the `X-Requested-With` header requirement.

### 4.2 Client

**`wwwroot/js/calendar-inline-edit.js`** — add `quickAddTimeOff(date, userId, type, label)` cloning `quickAddTextEntry`: `POST /Api/Calendar/QuickAddTimeOff` with `Content-Type: application/json` + `X-Requested-With: XMLHttpRequest`, `credentials:'same-origin'`; on `success` show a localized toast + `triggerCalendarRefresh()`; on 4xx route through `handleApiError`. Export `window.quickAddTimeOff`.

**`wwwroot/js/calendar-quick-entry.js`** (Quick-Entry ON / type mode):
- In `updateDropdown`, **before** the Shifts day-note free-text branch, add a recognizer gated on `getCurrentMode() === 'user'` + cell `rowId` starting `user-`. Parse (English **and** Hebrew — Hebrew is the default UI language):
  - Vacation: `/^(vacation|חופש|חופשה)$/i`
  - After: `/^(after|אפטר)$/i`
  - Day at: `/^(day at|יום ב)\s+(.+)$/i` → location = capture group.
- Push `{ type:'time-off', timeOffType:'vacation'|'after'|'dayat', label, text, date }`; render list rows reusing the day-note option DOM; set it selectable so Enter commits.
- In `selectItem`, add a `time-off` branch (next to `day-note`) that resolves `userId = parseInt(rowId.replace('user-',''),10)` and calls `window.quickAddTimeOff(date, userId, item.timeOffType, item.label)`.

**`wwwroot/js/calendar-bottom-sheet.js`** (Quick-Entry OFF / dropdown mode):
- In `populateContent`, when the cell is a `user-` row (people-row), add "🌴 Vacation", "📍 Day at…", "🌅 After" as pickable time-off options in the add-section. "Day at…" reveals a small location text input (reuse the chore-title input pattern).
- In `handleAssign`, branch: if a time-off option is chosen, call `window.quickAddTimeOff(cellData.date, userIdFromRow, type, label)` instead of `quickAddShift`.

### 4.3 Per-surface wiring

All three grids emit identical cells (`<td class="excel-calendar__cell" data-row-id="user-{id}" data-date="…">`) and carry `data-calendar-type="overview"`. The quick-entry engine attaches document-level delegated click handlers and **ignores `--readonly`**.

| Surface | Change |
|---------|--------|
| **Shifts** (`Pages/Calendar/Shifts.cshtml`) | Already loads the full stack. Only inherits the new shared `time-off` recognizer + bottom-sheet options. No page change beyond confirming the toggle is present (it is, gated on `CanWriteNote`). |
| **Overview** (`Pages/Calendar/Overview.cshtml`) | Add `window.CalendarPageConfig` (`moleculeId:0`); load `calendar-inline-edit.js`, `calendar-quick-entry.js`, `calendar-quick-entry.css` (bottom-sheet.js already loaded). Add `#quickEntryToggle` gated on a new page flag `CanEnterTimeOff` (see 4.4), `data-can-assign="false"`. Coexists with the existing double-click note modal (dblclick = note; toggle = typing). |
| **Overview cs** (`Overview.cshtml.cs`) | Set `CanEnterTimeOff = HasCalendarNotePermissionAsync(user)` scoped to the company. Cells already non-readonly when `CanEditNotes`. |
| **Team** (`Pages/Calendar/Team.cshtml`) | Same script/config/toggle additions as Overview. |
| **Team cs** (`Team.cshtml.cs`) | Compute `CanEnterTimeOff = HasCalendarNotePermissionAsync(user)` (grant-gated — the page is visible to all authenticated users, so the flag must be a real grant check). Keep `canEditNotes:false` (Team stays read-only for chip editing). |

### 4.4 Shared partial change (the one Team-specific piece)

Team's cells are read-only, so the `+` add-button (OFF-mode trigger) is not rendered (`_CalendarRow.cshtml`: `@if (!isReadOnly)`). To give Team the dropdown mode **without** un-suppressing chip deletion, introduce a capability decoupled from `IsReadOnly`:

- Add `bool CanEnterTimeOff` to the calendar view-model (`ExcelCalendarTableViewComponent` view-model) and plumb it through `Default.cshtml` → `_CalendarRow.cshtml`.
- Change the add-button render condition to `@if (!isReadOnly || canEnterTimeOff)`. When it renders **only** because of `canEnterTimeOff` (a read-only grid), the bottom sheet opens with **only** the time-off options (Team has no `assignee-select`, so no shift options appear) — exactly the desired behavior.
- For Overview & Shifts, `CanEnterTimeOff` equals their existing edit capability, so the `+` already showed → **no behavior change** there.
- Quick-Entry ON (typing) already works on read-only cells, so it needs no partial change — only the toggle must be shown for permitted users.

This keeps `IsReadOnly` meaning "no chip editing" and adds time-off entry as an additive, capability-gated affordance.

## 5. Localization

Add keys to **both** `Resources/SharedResources.resx` and `SharedResources.he-IL.resx`, and surface the JS-facing ones on `window.AppLocalizer` via `Pages/Shared/_LocalizationScript.cshtml`:

- `QuickEntry_TimeOff_VacationOption`, `QuickEntry_TimeOff_DayAtOption`, `QuickEntry_TimeOff_AfterOption` (dropdown labels)
- `QuickEntry_TimeOff_DayAtLocationPrompt` (location input placeholder)
- `QuickEntry_TimeOff_Saved` / `QuickEntry_TimeOff_Failed` (toasts)
- `QuickEntry_TimeOff_Hint` (combobox helper text)
- `Error_TimeOff_DayAtLabelRequired`, `Error_TimeOff_OverlapExists`, `Error_TimeOff_NoPermission`, `Error_TimeOff_UserNotInCompany`, `Error_CalendarApi_TimeOffFailed`

Hebrew keyword recognition (`חופש`, `אפטר`, `יום ב <location>`) is built into the JS recognizer regardless of UI culture, so a Hebrew-mode user types Hebrew and an English-mode user types English.

## 6. Removal / undo

Once materialised, the entry's HOME chips carry `data-source-request-id`, which the existing `cancel-or-shorten-dialog.js` (`window.openCancelOrShortenDialog`) already handles → the user cancels the manually-entered leave through existing plumbing. On Team's read-only grid the per-chip `×` is suppressed; verify the source-request cancel affordance is reachable on read-only grids. If it is not, removal-from-Team is recorded as a **known limitation / follow-up** (entry still removable from Overview/Shifts). This will be flagged explicitly, not silently deferred.

## 7. Security

- **IDOR:** the endpoint receives an explicit `userId`; the service re-verifies (a) actor holds the calendar-edit grant scoped to the resolved company, and (b) the target user belongs to that company. Page-level `[Authorize]` is not sufficient (per project convention).
- **Tenant:** company always resolved server-side via `_tenantResolver.GetCurrentTenantId()`, never trusted from the client.
- **CSRF:** `X-Requested-With` header (enforced by `ApiAuthenticationMiddleware`); `[IgnoreAntiforgeryToken]` per the `/Api/Calendar` convention.
- **Auditing:** every entry logged via `IAuditLogService`.

## 8. Testing (TDD — red → green)

Service tests (`VacationApprovalService` / new test class), using the real-SQLite fixture (not `UseInMemoryDatabase`):
1. Vacation entry → request Status=Approved; overlapping shift assignment on D removed; materialiser produced `HOME` on D + `HOME_AM` on D+1.
2. After entry → `HOME_PM` on D + `HOME_AM` on D+1.
3. DayAt entry with label → Label persisted; blank label → rejected with `Error_TimeOff_DayAtLabelRequired`.
4. Actor lacking the calendar-edit grant → rejected (403 semantics).
5. IDOR: target user not in the resolved company → rejected.
6. Overlap: existing Approved leave on D → rejected.
7. `FF_HOME_UNIFICATION` off → request still Approved + overlay data present, no HOME chips (materialiser skipped).
8. Trainee target → trainee shadow cleanup invoked.

Endpoint tests: type-string mapping (`vacation`/`after`/`dayat`), missing/invalid fields → 400, unauthenticated → 401.

Front-end: no automated JS harness in-repo — verify via the browser E2E pass (both modes, all three surfaces, Hebrew + English keywords).

## 9. Edge cases & rules

- **Past dates:** Quick-Entry blocks `--past` cells today; keep that (time-off is prospective). Confirm the bottom-sheet path applies the same guard.
- **By-shift mode:** recognizer/options gated to `user-` rows only, so typing "vacation" in by-shift mode still falls through to the existing day-note behavior.
- **Range entry:** v1 is single-day (the clicked cell). Multi-day range entry is out of scope (the request form still supports ranges).
- **Coexistence on Overview:** the double-click note modal and the quick-entry typing mode must not conflict (distinct triggers).

## 10. Out of scope / deferred (v1)

- Multi-day range selection on the calendar (single-day only).
- A distinct HOME-chip icon for DayAt (currently falls through to the rotation icon — cosmetic, pre-existing).
- Removal-from-Team if the source-request cancel affordance proves unreachable on read-only grids (flagged, not silent).

## 11. File-by-file change list

**New:**
- `Pages/Api/Calendar/QuickAddTimeOff.cshtml` + `.cshtml.cs`
- Service method on `Services/IVacationApprovalService.cs` + `Services/VacationApprovalService.cs`
- Test class (e.g. `Tests/.../ManualTimeOffEntryTests.cs`)

**Modified — server:**
- `Services/IVacationApprovalService.cs`, `Services/VacationApprovalService.cs`
- `Pages/Calendar/Overview.cshtml`, `Overview.cshtml.cs`
- `Pages/Calendar/Team.cshtml`, `Team.cshtml.cs`
- `ViewComponents/ExcelCalendarTableViewComponent.cs` (add `CanEnterTimeOff`)
- `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, `_CalendarRow.cshtml` (plumb `CanEnterTimeOff`, add-button condition)
- `Resources/SharedResources.resx`, `SharedResources.he-IL.resx`
- `Pages/Shared/_LocalizationScript.cshtml`

**Modified — client:**
- `wwwroot/js/calendar-inline-edit.js` (`quickAddTimeOff`)
- `wwwroot/js/calendar-quick-entry.js` (recognizer + `selectItem` branch)
- `wwwroot/js/calendar-bottom-sheet.js` (options + `handleAssign` branch)

**No change:** `Program.cs`, `ApiAuthenticationMiddleware.cs`, `OverviewCalendarBuilder.cs` (cell markup already uniform), `HomeMaterialiserService.cs`, `ShiftType` seeding.
