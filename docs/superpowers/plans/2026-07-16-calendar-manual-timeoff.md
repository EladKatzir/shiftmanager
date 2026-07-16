# Manual Time-Off Entry — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let calendar-editors enter Vacation / "Day at X" / After time-off directly on the three people-row calendars (Overview, Team, Shifts?Mode=user), creating an already-Approved `TimeOffRequest` that runs the existing approval side-effects and HOME materialiser.

**Architecture:** One new primitive `quickAddTimeOff(date,userId,type,label)` → one endpoint `POST /Api/Calendar/QuickAddTimeOff` → one service method `CreateApprovedManualTimeOffAsync`. Two front-ends (Quick-Entry ON typing recognizer; Quick-Entry OFF bottom-sheet section) call it. All three grids already render the result via `_CalendarRow.cshtml`.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core + SQLite, xUnit + Moq + FluentAssertions, vanilla JS.

## Global Constraints

- Morning-after busy boundary stays **13:00** (shared `HOME_AM` shift-type); do NOT re-seed shift-types.
- New `/Api/Calendar/*` endpoint is authenticated: `[Authorize]` + `[IgnoreAntiforgeryToken]`, CSRF via `X-Requested-With` header; **no** `Program.cs` / `ApiAuthenticationMiddleware` change.
- Permission gate = **company-scoped** calendar-edit permission; also verify the target user is a member of the resolved company (anti-IDOR).
- Materialiser call is gated on `FF_HOME_UNIFICATION` (enabled by default).
- Every new resx key MUST be added to BOTH `SharedResources.resx` and `SharedResources.he-IL.resx` (loc parity tests).
- `int.TryParse` on claim values, never `int.Parse`.
- Tests: real SQLite (`new SqliteConnection("DataSource=:memory:;Foreign Keys=False")` + `EnsureCreated()`); all service deps except `AppDbContext` are Moq mocks; no `GrantType` seeding; no `ITenantResolver`.
- Recognizer keyword gate: cell `rowId` starts `user-` (works on all three people-boards; excludes by-shift mode).

---

### Task 1: Localization keys

**Files:**
- Modify: `Resources/SharedResources.resx` (QuickEntry cluster ~14456)
- Modify: `Resources/SharedResources.he-IL.resx` (QuickEntry cluster ~14420)
- Modify: `Pages/Shared/_LocalizationScript.cshtml` (~line 154, after `QuickEntry_DayNoteDeleted`)

**Keys (name → EN / HE):**
- `QuickEntry_TimeOff_Vacation` → `Vacation` / `חופש`
- `QuickEntry_TimeOff_DayAt` → `Day at…` / `יום ב…`
- `QuickEntry_TimeOff_After` → `After` / `אפטר`
- `QuickEntry_TimeOff_DayAtLocationPrompt` → `Where? (e.g. course, clinic)` / `?היכן (למשל קורס, מרפאה)`
- `QuickEntry_TimeOff_Saved` → `Time off added` / `חופשה נוספה`
- `QuickEntry_TimeOff_Failed` → `Could not add time off` / `הוספת החופשה נכשלה`
- `QuickEntry_TimeOff_Hint` → `Type: vacation · after · day at <place>` / `הקלד: חופש · אפטר · יום ב <מקום>`
- `TimeOff_Section_Title` → `Add time off` / `הוספת חופשה`
- `Error_TimeOff_DayAtLabelRequired` → `A location is required for "Day at".` / `יש להזין מיקום עבור "יום ב".`
- `Error_TimeOff_OverlapExists` → `This person already has approved time off on that day.` / `לאדם זה כבר יש חופשה מאושרת ביום זה.`
- `Error_TimeOff_NoPermission` → `You don't have permission to add time off here.` / `אין לך הרשאה להוסיף חופשה כאן.`
- `Error_TimeOff_UserNotInCompany` → `That person is not in this company.` / `אדם זה אינו שייך לחברה זו.`
- `Error_TimeOff_InvalidType` → `Unknown time-off type.` / `סוג חופשה לא מוכר.`
- `Error_CalendarApi_TimeOffFailed` → `Failed to add time off.` / `הוספת החופשה נכשלה.`

**JS-facing keys** (add to `_LocalizationScript.cshtml`): `QuickEntry_TimeOff_Vacation`, `_DayAt`, `_After`, `_DayAtLocationPrompt`, `_Saved`, `_Failed`, `_Hint`, `TimeOff_Section_Title`.

- [ ] Add all keys to both resx files (mirror the 2-space `xml:space="preserve"` format).
- [ ] Add the JS-facing keys to `_LocalizationScript.cshtml` using `@Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["KEY"].Value)),`.
- [ ] Verify: `dotnet build` succeeds; run loc-parity tests `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~Localization" --nologo` → PASS.
- [ ] Commit: `feat(timeoff): localization keys for manual time-off entry`.

---

### Task 2: GrantService — company-scoped calendar permission

**Files:**
- Modify: `Services/IGrantService.cs` (add 2 signatures near line 25)
- Modify: `Services/GrantService.cs` (add 2 methods near line 63)
- Test: `ShiftManager.Tests/UnitTests/Services/GrantServiceCalendarScopeTests.cs` (new)

**Produces:**
```csharp
Task<bool> HasCalendarEditPermissionForCompanyAsync(int userId, int companyId);
Task<bool> HasCalendarNotePermissionForCompanyAsync(int userId, int companyId);
```

**Implementation (GrantService.cs):**
```csharp
public async Task<bool> HasCalendarEditPermissionForCompanyAsync(int userId, int companyId)
{
    return await HasGrantForCompanyAsync(userId, "AdminAccess", companyId)
        || await HasGrantForCompanyAsync(userId, "AssignShifts", companyId)
        || await HasGrantForCompanyAsync(userId, "AssignChores", companyId)
        || await HasGrantForCompanyAsync(userId, "ManageOnDuty", companyId)
        || await HasGrantForCompanyAsync(userId, "EditOnCallCalendar", companyId);
}

public async Task<bool> HasCalendarNotePermissionForCompanyAsync(int userId, int companyId)
{
    return await HasGrantForCompanyAsync(userId, "WriteOverviewNotes", companyId)
        || await HasCalendarEditPermissionForCompanyAsync(userId, companyId);
}
```

- [ ] Step 1: Write failing test — a user with an `AssignShifts` grant scoped to company 5 returns true for company 5, false for company 6; seed via real `Grant` rows + `GrantType` (GrantService is the real service here, so seed the two grant types + a grant row). Use the SQLite harness.
- [ ] Step 2: Run → FAIL (method missing).
- [ ] Step 3: Add interface signatures + implementations above.
- [ ] Step 4: Run → PASS.
- [ ] Step 5: Commit `feat(grants): company-scoped calendar-edit permission checks`.

---

### Task 3: VacationApprovalService.CreateApprovedManualTimeOffAsync (TDD core)

**Files:**
- Modify: `Services/IVacationApprovalService.cs`
- Modify: `Services/VacationApprovalService.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/ManualTimeOffEntryTests.cs` (new)

**Produces:**
```csharp
Task<(bool Success, int? RequestId, string? ErrorKey)> CreateApprovedManualTimeOffAsync(
    int targetUserId, int companyId, TimeOffType type,
    DateOnly startDate, DateOnly endDate, string? label, int actorUserId);
```

**Behavior (implementation):**
```csharp
public async Task<(bool Success, int? RequestId, string? ErrorKey)> CreateApprovedManualTimeOffAsync(
    int targetUserId, int companyId, TimeOffType type,
    DateOnly startDate, DateOnly endDate, string? label, int actorUserId)
{
    // 1. Authorize: actor holds company-scoped calendar-edit permission
    if (!await _grantService.HasCalendarNotePermissionForCompanyAsync(actorUserId, companyId))
        return (false, null, "Error_TimeOff_NoPermission");

    // 2. Anti-IDOR: target user must be a member of this company
    if (!await _membershipService.IsMemberAsync(targetUserId, companyId))
    {
        // fallback: home-company users (single-company) may not have a CompanyMembership row
        var homeCompany = await _context.Users.IgnoreQueryFilters()
            .Where(u => u.Id == targetUserId).Select(u => (int?)u.CompanyId).FirstOrDefaultAsync();
        if (homeCompany != companyId)
            return (false, null, "Error_TimeOff_UserNotInCompany");
    }

    // 3. Normalize per type (mirror My/Requests)
    if (type == TimeOffType.After || type == TimeOffType.DayAt)
        endDate = startDate;
    if (type == TimeOffType.DayAt && string.IsNullOrWhiteSpace(label))
        return (false, null, "Error_TimeOff_DayAtLabelRequired");
    if (endDate < startDate)
        return (false, null, "Error_EndDateBeforeStartDate");

    // 4. Overlap guard vs existing Approved leave
    var hasOverlap = await _context.TimeOffRequests.IgnoreQueryFilters()
        .AnyAsync(r => r.UserId == targetUserId && r.Status == RequestStatus.Approved
                    && r.StartDate <= endDate && r.EndDate >= startDate);
    if (hasOverlap)
        return (false, null, "Error_TimeOff_OverlapExists");

    var now = DateTime.UtcNow;
    var request = new TimeOffRequest
    {
        CompanyId = companyId,
        UserId = targetUserId,
        StartDate = startDate,
        EndDate = endDate,
        Type = type,
        Label = type == TimeOffType.DayAt ? label?.Trim() : null,
        Reason = "Entered on calendar",
        Status = RequestStatus.Approved,
        ApproverId = actorUserId,
        FirstApprovalActorId = actorUserId,
        FirstApprovalActedAt = now,
        CreatedAt = now
    };

    await using var tx = await _context.Database.BeginTransactionAsync();
    try
    {
        _context.TimeOffRequests.Add(request);
        await _context.SaveChangesAsync();

        await ProcessApprovalSideEffectsAsync(request.Id);

        if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
            await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id);

        await _auditLogService.LogAsync(actorUserId, "TimeOffEnteredOnCalendar",
            "TimeOffRequest", request.Id.ToString(),
            $"Manual {type} for user {targetUserId} on {startDate:yyyy-MM-dd}");

        await tx.CommitAsync();
        return (true, request.Id, null);
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
}
```
(Confirm `IAuditLogService.LogAsync` signature during impl; adapt to the real one. `FeatureFlagSeed.Flags.HomeUnification` = `"FF_HOME_UNIFICATION"`. Confirm `IsEnabledAsync` overload — may need `(flag, userId?, companyId?)`.)

- [ ] Step 1: Failing test `Vacation_CreatesApproved_RemovesConflictingShift_Materialises` — seed company user + a `ShiftInstance`+`ShiftAssignment` on D; grant mock returns true; act; assert request.Status==Approved, assignment removed, `_materialiserMock.Verify(SyncMaterialisedHomeRowsAsync(id))`.
- [ ] Step 2..N: tests for After (EndDate=Start), DayAt (label persisted; blank→`Error_TimeOff_DayAtLabelRequired`), no-permission (grant mock false → `Error_TimeOff_NoPermission`), IDOR (membership false + home company mismatch → `Error_TimeOff_UserNotInCompany`), overlap (`Error_TimeOff_OverlapExists`), flag-off (materialiser NOT called).
- [ ] Run → FAIL, then implement, then PASS.
- [ ] Commit `feat(timeoff): approved manual time-off service method + tests`.

---

### Task 4: API endpoint `Pages/Api/Calendar/QuickAddTimeOff`

**Files:**
- Create: `Pages/Api/Calendar/QuickAddTimeOff.cshtml` (`@page` + `@model` shim)
- Create: `Pages/Api/Calendar/QuickAddTimeOff.cshtml.cs`

Model on `QuickAddDayNote.cshtml.cs`: `[Authorize]`, `[IgnoreAntiforgeryToken]`, inject `IVacationApprovalService`, `ITenantResolver`, `IStringLocalizer<SharedResources>`, `ILogger`. `OnPostAsync` reads JSON body `{ date, userId, type, label }`; parse actor via `ClaimTypes.NameIdentifier` (`int.TryParse`); tenant via `_tenantResolver.GetCurrentTenantId()`; map `type` string (`"vacation"|"after"|"dayat"`) → `TimeOffType` (else 400 `Error_TimeOff_InvalidType`); call service; map `ErrorKey` → localized message + status (403 for NoPermission, 409 for overlap, 400 otherwise); success `{ success:true, requestId }`.

- [ ] Create both files; map type strings; localized error responses.
- [ ] Verify: build; smoke via a page handler test OR defer to browser E2E (Task 12).
- [ ] Commit `feat(timeoff): QuickAddTimeOff API endpoint`.

---

### Task 5: `quickAddTimeOff` client primitive

**Files:** Modify `wwwroot/js/calendar-inline-edit.js` (after `quickAddDayNote` export, ~line 616).

```js
async function quickAddTimeOff(date, userId, type, label) {
    try {
        const response = await fetch('/Api/Calendar/QuickAddTimeOff', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            body: JSON.stringify({ date: date, userId: parseInt(userId), type: type, label: label || null })
        });
        if (!response.ok) {
            if (response.status === 401 || response.status === 403) { handleApiError(response); return; }
            var errJson = null; try { errJson = await response.json(); } catch (e) {}
            showToast((errJson && errJson.message) || (window.AppLocalizer?.QuickEntry_TimeOff_Failed) || 'Could not add time off', 'error');
            return;
        }
        const result = await response.json();
        if (result.success) {
            showToast(window.AppLocalizer?.QuickEntry_TimeOff_Saved || 'Time off added', 'success');
            triggerCalendarRefresh();
        } else {
            showToast(result.message || (window.AppLocalizer?.QuickEntry_TimeOff_Failed) || 'Error', 'error');
        }
    } catch (error) { handleApiError(null, error); }
}
window.quickAddTimeOff = quickAddTimeOff;
```
- [ ] Add + export. Commit `feat(timeoff): quickAddTimeOff client helper`.

---

### Task 6: Quick-Entry ON recognizer (typing)

**Files:** Modify `wwwroot/js/calendar-quick-entry.js` — (a) new `else if` branch in `updateDropdown` inserted BEFORE line 663 (the `isShiftsCalendar` day-note branch); (b) new `time-off` branch in `selectItem` before line 1216.

Recognizer (insert before the day-note `else if`), gated only on a `user-` row:
```js
} else if (query.trim().length > 0 &&
           activeInput._cellData && activeInput._cellData.rowId &&
           activeInput._cellData.rowId.indexOf('user-') === 0 &&
           parseTimeOffQuery(query.trim())) {
    var to = parseTimeOffQuery(query.trim());
    var toItem = { type: 'time-off', timeOffType: to.type, label: to.label,
                   text: query.trim(), date: activeInput._cellData.date };
    var toIdx = filteredItems.length;
    filteredItems.push(toItem);
    var toEl = document.createElement('div');
    toEl.className = 'quick-entry-item quick-entry-item--text-entry quick-entry-item--time-off';
    toEl.setAttribute('role', 'option');
    toEl.id = dropdown.id + '-item-' + toIdx;
    toEl.dataset.index = toIdx;
    var toIcon = document.createElement('span');
    toIcon.className = 'quick-entry-text-icon';
    toIcon.textContent = to.type === 'after' ? '🌅' : (to.type === 'dayat' ? '📍' : '🌴');
    toEl.appendChild(toIcon);
    var toLabel = document.createElement('span');
    var lblKey = to.type === 'after' ? 'QuickEntry_TimeOff_After'
               : to.type === 'dayat' ? 'QuickEntry_TimeOff_DayAt' : 'QuickEntry_TimeOff_Vacation';
    toLabel.textContent = getLocalizedLabel(lblKey) + (to.label ? (': "' + to.label + '"') : '');
    toEl.appendChild(toLabel);
    (function (capturedIdx) {
        toEl.addEventListener('mousedown', function (e) { e.preventDefault(); selectedIndex = capturedIdx; selectItem(filteredItems[capturedIdx]); });
    })(toIdx);
    dropdown.appendChild(toEl);
    selectedIndex = toIdx; // time-off keyword is explicit → auto-select for Enter
}
```
Helper `parseTimeOffQuery` (add near `getCurrentMode`), EN + HE:
```js
function parseTimeOffQuery(q) {
    var s = q.trim();
    if (/^(vacation|חופש|חופשה)$/i.test(s)) return { type: 'vacation', label: null };
    if (/^(after|אפטר)$/i.test(s)) return { type: 'after', label: null };
    var m = /^(?:day at|יום ב)\s+(.+)$/i.exec(s);
    if (m && m[1].trim()) return { type: 'dayat', label: m[1].trim() };
    return null;
}
```
`selectItem` branch (before line 1216):
```js
if (item.type === 'time-off') {
    var toUserId = parseInt(rowId.replace('user-', ''), 10);
    var currentCellTO = activeCell, doAdvanceTO = !!advance;
    var toPromise = window.quickAddTimeOff(date, toUserId, item.timeOffType, item.label);
    if (toPromise && typeof toPromise.then === 'function') {
        toPromise.then(function () { closeInput(); if (doAdvanceTO) advanceToNextCell(currentCellTO); });
    } else { closeInput(); if (doAdvanceTO) advanceToNextCell(currentCellTO); }
    return;
}
```
- [ ] Add helper + both branches. Commit `feat(timeoff): quick-entry typing recognizer for time-off`.

---

### Task 7: Quick-Entry OFF (bottom-sheet section) + extractCellData

**Files:** Modify `wwwroot/js/calendar-bottom-sheet.js`.

(a) `extractCellData` — add to the returned object: `canEnterTimeOff: cellEl.dataset.canEnterTimeoff === 'true'`.

(b) `populateContent` — change the add-section gate from `if (!cellData.isReadOnly)` to `if (!cellData.isReadOnly || cellData.canEnterTimeOff)`; keep the existing user/shift/chore selector inside a `if (!cellData.isReadOnly) { ... }`; then, when `cellData.rowId.indexOf('user-') === 0 && cellData.canEnterTimeOff`, append a self-contained Time-off block: a section title (`TimeOff_Section_Title`) + three buttons (Vacation / Day at… / After). "Day at…" toggles a text input (placeholder `QuickEntry_TimeOff_DayAtLocationPrompt`) then a confirm; the others call directly:
```js
function addTimeOffSection(bodyEl, cellData) {
    var uid = cellData.rowId.replace('user-', '');
    var sec = document.createElement('div'); sec.className = 'bottom-sheet__section';
    var h = document.createElement('h4'); h.className = 'bottom-sheet__section-title';
    h.textContent = window.AppLocalizer?.TimeOff_Section_Title || 'Add time off'; sec.appendChild(h);
    function mkBtn(labelKey, fallback, onClick) {
        var b = document.createElement('button'); b.type='button';
        b.className='btn btn-ghost bottom-sheet__action-btn';
        b.textContent = (window.AppLocalizer && window.AppLocalizer[labelKey]) || fallback;
        b.addEventListener('click', onClick); sec.appendChild(b); return b;
    }
    mkBtn('QuickEntry_TimeOff_Vacation','Vacation', function(){ window.quickAddTimeOff(cellData.date, uid, 'vacation', null); close(); });
    mkBtn('QuickEntry_TimeOff_After','After', function(){ window.quickAddTimeOff(cellData.date, uid, 'after', null); close(); });
    var locWrap = document.createElement('div'); locWrap.className='bottom-sheet__field'; locWrap.style.display='none';
    var locInput = document.createElement('input'); locInput.type='text'; locInput.className='bottom-sheet__input';
    locInput.placeholder = window.AppLocalizer?.QuickEntry_TimeOff_DayAtLocationPrompt || 'Where?';
    locWrap.appendChild(locInput);
    var dayAtBtn = mkBtn('QuickEntry_TimeOff_DayAt','Day at…', function(){
        if (locWrap.style.display === 'none') { locWrap.style.display=''; locInput.focus(); return; }
        var v = locInput.value.trim(); if (!v) { locInput.focus(); return; }
        window.quickAddTimeOff(cellData.date, uid, 'dayat', v); close();
    });
    sec.appendChild(locWrap);
    bodyEl.appendChild(sec);
}
```
Call `addTimeOffSection(bodyEl, cellData)` inside the widened add-section gate when the user-row + canEnterTimeOff conditions hold.

- [ ] Implement (a),(b). Commit `feat(timeoff): bottom-sheet time-off section (quick-entry OFF)`.

---

### Task 8: `CanEnterTimeOff` plumbing (shared partial)

**Files:**
- Modify: `ViewComponents/ExcelCalendarTableViewComponent.cs` (add `bool CanEnterTimeOff` to `ExcelCalendarTableViewModel`)
- Modify: `ViewComponents/ExcelCalendarRowViewModel.cs` (add `bool CanEnterTimeOff`)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` (pass `CanEnterTimeOff = Model.CanEnterTimeOff` at the 3 row-construction sites: 128, 147, 166)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml` (top: `bool canEnterTimeOff = Model.CanEnterTimeOff;`; on `<td>` add `data-can-enter-timeoff="@(canEnterTimeOff ? "true" : "false")"`; change add-button to `@if (!isReadOnly || canEnterTimeOff)`)

- [ ] Implement; build. Commit `feat(timeoff): CanEnterTimeOff capability on calendar cells`.

---

### Task 9: OverviewCalendarBuilder + Overview page wiring

**Files:**
- Modify: `Services/IOverviewCalendarBuilder.cs` + `Services/OverviewCalendarBuilder.cs` — add `bool canEnterTimeOff = false` param to `BuildAsync`; set `vm.CanEnterTimeOff = canEnterTimeOff`.
- Modify: `Pages/Calendar/Overview.cshtml.cs` — compute `CanEnterTimeOff = await _grantService.HasCalendarNotePermissionForCompanyAsync(currentUserId, CompanyId)`; pass to `BuildAsync`; expose as page property.
- Modify: `Pages/Calendar/Overview.cshtml` — in `@section Scripts`, add `window.CalendarPageConfig = { isTechMolecule:false, categoryEligibilityEnabled:false, moleculeId:0 };`, load `calendar-inline-edit.js` + `calendar-quick-entry.js` + `calendar-quick-entry.css`; add the `#quickEntryToggle` button (gated `@if (Model.CanEnterTimeOff)`, `data-can-assign="false"`) into the toolbar.

- [ ] Implement; build. Commit `feat(timeoff): enable time-off entry on Overview`.

---

### Task 10: Team page wiring

**Files:**
- Modify: `Pages/Calendar/Team.cshtml.cs` — compute `CanEnterTimeOff = await _grantService.HasCalendarNotePermissionForCompanyAsync(currentUserId, SelectedCompanyId)`; pass to `BuildAsync(..., canEditNotes:false, canEnterTimeOff: CanEnterTimeOff)`; expose page property.
- Modify: `Pages/Calendar/Team.cshtml` — same script/config/toggle additions as Overview (gated `@if (Model.CanEnterTimeOff)`).

- [ ] Implement; build. Commit `feat(timeoff): enable time-off entry on Team`.

---

### Task 11: Build + full test suite

- [ ] `dotnet build ShiftManager.sln -c Debug --nologo` → 0 errors.
- [ ] `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -c Debug --nologo --logger "console;verbosity=minimal"` → all green (baseline was green on dev).
- [ ] Fix any regressions at root cause.

---

### Task 12: Browser E2E (both modes × 3 surfaces × EN + HE)

Run the worktree app on a spare port: `dotnet run -c Debug --urls http://localhost:5100` (fresh isolated app.db, auto-seeded). Then, via Chrome automation, log in and for each of Overview, Team, Shifts?Mode=user:
- Quick-Entry ON: type `vacation`, `after`, `day at clinic` on a user cell → verify palm-tree/label + HOME chips appear after refresh; conflicting shift removed (Shifts).
- Quick-Entry OFF: `+` → time-off section → each of Vacation/After/Day-at → verify.
- Repeat one case in Hebrew (`.AspNetCore.Culture` cookie `c=he-IL|uic=he-IL`) using `חופש` / `אפטר` / `יום ב מרפאה`.
- Verify removal via the source-request chip (cancel/shorten dialog).

- [ ] Execute all; capture screenshots; note any Team-removal limitation in the backlog if the cancel affordance is unreachable on read-only grids.

---

### Task 13: Final validation

- [ ] Re-read spec §1-11; confirm each requirement has a landing task.
- [ ] Ask "did we do everything correctly?" — adversarial self-review of authz, tenant scoping, transaction, idempotency.
- [ ] Update `BACKLOG.md` with any user-decisions; update project memory.
- [ ] Final commit(s).

## Self-Review

- Spec §4.1 service → Task 3 ✓; §4.1 endpoint → Task 4 ✓; §4.2 client → Tasks 5-7 ✓; §4.3 wiring → Tasks 9-10 ✓; §4.4 partial → Task 8 ✓; §5 loc → Task 1 ✓; §7 security → Tasks 2,3 ✓; §8 tests → Task 3 ✓; §D3 browser → Task 12 ✓.
- Type consistency: `CreateApprovedManualTimeOffAsync` returns `(bool,int?,string?)` used identically in Tasks 3-4; `quickAddTimeOff(date,userId,type,label)` identical in Tasks 5-7; `CanEnterTimeOff` identical in Tasks 8-10.
- Placeholder scan: none.
