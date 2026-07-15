# Design — UI/Bug batch + Team page (2026-07-14)

Status: **APPROVED-WITH-CHANGES → revised** (two independent Opus reviews: coherence/coverage + technical-verification, both APPROVE-WITH-CHANGES; all blocking items folded in below). Branch base: `dev`.

A ten-item batch (user's list skips #7: 1–6, 8, 9, 10) mixing bug fixes, small UI changes, two per-user preferences, an authorization fix, and one new feature page. Source of truth for the implementation plan.

> **Line-number caveat:** cited line numbers drift ±2–15 vs current source — **navigate by symbol, not line**. Specific corrections from review are applied below.

## Terminology
- Hierarchy: Project → Area → Molecule → Company → Department. Hebrew "Company" = **דסק** (desk).
- "Team" = a **JobType within a Company** (Hebrew "צוות"). "Desk" = a whole Company. JobType is **area-scoped** (same JobType id spans companies in the area) — this is what makes the cross-company Team view work.
- Culture cookie: `.AspNetCore.Culture`, value form `c=he-IL|uic=he-IL`.

## Implementation grouping (order)
1. **Cheap, isolated UI** — #4 (CSS), #5 (This Week card), #8 (Total column).
2. **AppUser preference** — #6 (suppress success toasts). *(#10 needs no column.)*
3. **Authorization fix** — #3 (scope-aware eligibility editor + IDOR close).
4. **Language persistence** — #10 (login cookie re-seed, both auth paths).
5. **Feature** — #9 (My Desk rename + new Team page + shared builder + new saved-view entity).
6. **Build + verify** — resolves #1/#2 and validates everything end-to-end.

**Migration discipline:** the #6 (`AppUser`) and #9 (new entity) migrations share one EF `ModelSnapshot` — create them **sequentially on this one branch, never in parallel worktrees**. TDD where a seam exists; browser-verify every change against a **freshly rebuilt** exe (stop the :5000 dev exe before each rebuild — locked-executable rule).

---

## #1 — "ReferenceError: config is not defined" on /Calendar/Shifts
**Finding: not a source bug (verified twice).** An exhaustive `\bconfig\b` sweep of every script reachable from the page found **zero bare `config` reads** — all 47 occurrences are object keys (`config: CONFIG`), function params, or comments. The page's config is `window.CalendarPageConfig` (`Shifts.cshtml:454-458`), read **null-guarded** by `calendar-bottom-sheet.js:432,483` and `calendar-quick-entry.js:153`; a missing object degrades silently, cannot throw. The two different local aliases (`cfg`/`calPageConfig`) are the fingerprint of a refactor (bare `config` → `window.CalendarPageConfig`); a **stale served/cached JS asset** (documented `:5000` behavior) throws the old error.

**Resolution (no speculative source edit):**
1. **On first repro, capture the live console `source:line`** — positively confirm the failing frame points at a stale/cached file, not current source.
2. Rebuild → restart dev exe → hard-refresh → re-check console. Gone → confirmed stale.
3. Only if it survives a clean build: fix the exact source the stack names. Do **not** add a guard for a non-existent bug (root-cause policy).

**Optional (only if requested):** versioned asset URLs to structurally prevent stale-JS-after-refactor. Out of scope unless approved.

## #2 — "error_boundary_triggered" on /Calendar/Shifts
`error-boundary.js` installs a global `window.onerror`/`unhandledrejection` hook (`:540,553`); any uncaught error → `trackEvent('error_boundary_triggered', …)` (`:120`) + fallback toast; ≥5 errors/60s → full-page overlay. **It is a symptom-reporter of #1, not an independent bug.** Resolved when #1 is. No change to the boundary.

---

## #3 — Owner cannot edit eligibility for all users
**Root cause (verified):** `Pages/Scheduling/Eligibility/Index.cshtml.cs` pins data to `_currentUser.MoleculeId` (login home-molecule claim, `:52-53`) with **no molecule selector**; `OnGetAsync` loads categories/users for that one molecule. Owner holds `ManageShiftCategories` at **project** scope (seeded `Data/SeedData/RoleTemplateSeed.cs:1041` — `G(11,136,ETP,canGive:true)`); the `Grant:` policy (`GrantPolicyProvider.cs`) checks only grant possession, **no scope**, so it does **not** block Owner — the page just never shows anyone outside the home molecule. Molecule-scoped roles (Manager/Assigner) work by coincidence; bug manifests for **Owner (project)** and **AreaAdmin (area)**.

**Latent cross-molecule IDOR (must close):** `OnPostToggleAsync` calls `_shiftCats.SetUserCategoriesAsync(req.UserId,…)`; `SetUserCategoriesAsync` (`ShiftCategoryService.cs:139-168`) validates only id existence — no caller-scope check. The read handlers `OnGetCategoryAsync`/`OnGetPersonAsync` are also unscoped (info-disclosure).

**Design:**
1. Add `[BindProperty(SupportsGet=true)] int? MoleculeId`, default `_currentUser.MoleculeId` (back-compat).
2. Populate a **molecule selector** from `GrantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageShiftCategories")` (`GrantService.cs:403`; project→all, area→area's, molecule→one). **Do not** copy `Blueprints.cshtml.cs`'s `m.AreaId==areaId` filter (`:112`) — it would area-confine the Owner.
3. Load `ShiftCategoryOptions`/`ChoreCategoryOptions`/`UserOptions` for the **selected** molecule (`GetCategoriesForMoleculeAsync`, `GetMoleculeUsersAsync`).
4. Add scope re-checks to `OnPostToggleAsync`, `OnGetCategoryAsync`, `OnGetPersonAsync`: caller must have `ManageShiftCategories` for the **target's** molecule (mirror `Admin/Users.cshtml.cs:1795-1802`). Closes the IDOR *and* enables cross-molecule editing.

**Template:** `Pages/Owner/Blueprints.cshtml.cs:111-154` (same grant, molecule-selectable). **Tests:** Owner lists+toggles across two molecules; a molecule-A-scoped holder is blocked toggling a molecule-B user (IDOR regression).

---

## #4 — First user row hidden behind sticky `<thead>` (Admin/Users)
**Root cause (verified):** table is `.data-table--sticky-header` inside `.table-responsive` (`site.css:2719`, `overflow-x:auto`). One-axis `auto` makes the wrapper a scroll container on **both** axes → it becomes the sticky positioning reference, not the viewport. The sticky rule `site.css:7261-7267` uses `top: var(--header-height,64px)` (to clear the fixed app header) — correct only vs the viewport. Inside the local scroll box, 64px shoves the `thead` **down onto row 1**. Identical to the already-fixed calendar bug (`calendar.css:2780-2786`, `top:0`).

**Fix (CSS only, scoped):** add after `site.css:7267`:
```css
.table-responsive      .data-table--sticky-header thead th,
.table-wrapper         .data-table--sticky-header thead th,
.results-table-wrapper .data-table--sticky-header thead th { top: 0; }
```
Verified split: Users (reported) + **DatabaseConsole + Telemetry** (genuinely latent) get `top:0`; **Grants/Index:86 & Roles/Index:95** sit in `.section-card` (viewport-scrolled) and keep `top:64px` — the scoped selector doesn't touch them. *(AuditLog already carries its own inline `top:0`, so it isn't visibly bugged — harmless to include.)* **Do NOT** change the base rule globally.
**Verify:** row 1 fully visible/clickable at the top of the Users scroll; Grants/Roles headers still clear the app header.

---

## #5 — "This Week" card: hours→shift count; vacation→offline+vacation split
**Location:** `Pages/Home/Index.cshtml:109-135`; model `Pages/Home/Index.cshtml.cs` (props `:41-43`; `weekShifts` load `:148-153`; computations `:166-178`). Loc keys `HoursThisWeek`/`DaysOff` are used **only** here (safe to swap).

**(a) Hours → shift count:** add `ShiftsThisWeek`, render it instead of `HoursThisWeek.ToString("F1")`, relabel (reuse `ShiftCount`="מספר משמרות", exists `:1348`).

**(b) Offline vs vacation — SET-BASED classification (corrected per review; count subtraction was unsound).**
The three day-sets are **not disjoint**: HOME shifts are generated from approved TimeOffRequests (`ShiftAssignment.SourceTimeOffRequestId`), and there is an explicit **OFFLINE** shift-type (`ShiftType.IsOffline`, `ShiftType.cs:149`) — so a day can carry both a vacation and a shift, and the old `7 − shifts − vacation` could go **negative**. Instead, classify **each of the 7 weekdays into exactly one bucket by precedence**:
```
for each day d in the week:
    if d has a NON-home, NON-offline shift assignment      → shiftDay
    else if d overlaps an APPROVED TimeOffRequest           → vacationDay
    else                                                    → offlineDay
```
- Buckets are disjoint, non-negative, and sum to 7 by construction.
- Apply the **same home/offline exclusion** consistently across `ShiftsThisWeek`, `DaysWithShiftsThisWeek`, and offline (today `DaysWithShiftsThisWeek` at `:177` counts *all* assignments incl. home/offline — fix it too, for parity with #8).
- The approved-vacation query is **new code in the common path** (`LoadCommonDataAsync`/the `:166-178` method) — the `TimeOffRequests` reference at `:184` lives in `LoadEmployeeDataAsync` (employee-only, no date filter) and is **not** reusable here. Use `RequestStatus.Approved` + `StartDate`/`EndDate` overlap with the week.
- **Labels:** relabel the old "Days Off" row → **"Offline Days"**, new key `DaysOffline`="Offline Days"/**"ימי אופליין"** (mirror `OFFLINE`/`ShiftType_OFFLINE`="אופליין" at `:489/:744` — **not** `Status_Offline`="לא מקוון" `:6834`). Add a **"Vacation Days"** row (new key `VacationDays`="ימי חופש"), shown when > 0.

**Test:** a week with an approved vacation landing on a worked/home day still yields disjoint, non-negative counts summing to 7.

---

## #6 — Turn off success toasts (per-user)
**Two success-toast renderers, both gated at the SOURCE function** (not `window.showToast` — `calendar-inline-edit.js:1147` clobbers that): the engine `toast-notifications.js` `showToast` (`:263`; roster toast `roster-dock.js:392`) **and** the calendar's own `showToast` (`calendar-inline-edit.js:897`; inline assign/create/remove success sites).

**Design (mirror theme's DB + cookie pattern):**
1. Add `bool SuppressSuccessToasts` to `AppUser` (next to `ThemeMode`/`ThemeColor` `:67-68`) + EF migration.
2. Toggle on `/My/Settings`; `OnPostAsync` (`:193`) already resolves userId + loads AppUser (`:258`) — set + save.
3. Mirror to a non-HttpOnly cookie on save; surface as `window.UserPrefs.suppressSuccessToasts` in `_Layout` head **before** `toast-notifications.js` (`:183`).
4. Gate at the top of **both** source `showToast`s: `if (type==='success' && window.UserPrefs?.suppressSuccessToasts) return;`. Errors/warnings/info still show; settings-saved confirmation is a modal, unaffected.

**Trade-off (note):** the suppressed success toast (`toast-undo--success`) is also the assignment **undo** affordance — turning off success toasts removes inline undo-after-assign. This matches the user's explicit intent ("turn off success alerts"); flagged so it's a conscious choice.
**Verify:** with toggle on, roster-board *and* inline-calendar assignments show no success toast; an error still toasts.

---

## #8 — Week-view "Total" column: hours → shift count
Per-user **rightmost "Total" column** (confirmed with user — not a grand-total row; no `<tfoot>` exists). Shows only in `Mode=user`.
- Property `ExcelCalendarRow.WeeklyHours` (`ViewComponents/ExcelCalendarTableViewComponent.cs:69`).
- Summed at 3 by-user builders in `Pages/Calendar/Shifts.cshtml.cs`: **`:726` (`BuildUserRow` — the URL's path), `:764`, `:804`** — each excludes IsHome/IsOffline → `TimeHelpers.MergeAndSumHours`.
- Rendered: header `Default.cshtml:70-75` (`@Localizer["Total"]`); cell `_CalendarRow.cshtml:233-241` (`:238`); gate `Default.cshtml:15-16`.

**Design (clean):** add `public int? ShiftCount` to `ExcelCalendarRow` (do **not** overload `WeeklyHours`); set at the 3 sites `= assignments.Count(a => a.UserId==user.Id && WorkDate∈[Start,End] && !IsHome && !IsOffline)` (same exclusion; merging irrelevant for a count). Add `HasShiftCount` mirror flag (parallel to `HasWeeklyHours`); drive the column by it; render `@row.ShiftCount` (no "h"); header → new key `TotalShifts` (absent, safe). Blast radius verified contained to this column (other `WeeklyHours*` hits are unrelated `WeeklyHoursCap`). Retire `WeeklyHours` only if hours are wanted nowhere (confirm first).
**Verify:** Total column shows integer counts per user, no "h".

---

## #9 — "My Desk" rename + new "Team" page (JobType × Company)
Decisions: rename **Company Overview** → "My Desk"; new page **separate** (keep `/MyTeam` untouched); tables **saved & private**; visible to **everyone** (scope-limited); **replace** the `Calendar_ShiftsManagement` nav leaf (`/Calendar/Table` keeps working by URL — user OK'd losing its nav link).

### 9.1 Nav (`Services/Navigation/NavRegistry.cs`)
- **Rename** `Link("CompanyOverview","/Calendar/Overview",icon:"eye")` (`:46`) → new key `MyDesk`="My Desk"/"הדסק שלי".
- **Replace** `Link("Calendar_ShiftsManagement","/Calendar/Table",policy:G("ManagerHomeAccess"),icon:"table")` (`:45`) → `Link("Nav_Team","/Calendar/Team", policy: null /* everyone */, icon:"users")`.
- **Parity:** `NavRegistryPolicyParityTests` requires the route's PageModel policy ⊇ nav policy → the Team page must be `[Authorize]` (any auth) for a `null` nav leaf. Verified valid.

### 9.2 Shared calendar builder (the enabling refactor — corrected per review)
`OverviewModel.BuildOverviewCalendarAsync` + `LoadUsersAsync`/`LoadVacationsAsync`/`LoadShiftsAsync`/`LoadChoresAsync`/`LoadOnDutiesAsync`/`BuildCellsForUser` are **private/internal instance methods** bound to page state — **not** callable from a new page. Only the `ExcelCalendarTable` view component is drop-in (`@await Component.InvokeAsync("ExcelCalendarTable", Model.CalendarData)`, `Overview.cshtml:145`).
**Extract** the user-set→`ExcelCalendarTableViewModel` build into a shared service:
```
IOverviewCalendarBuilder.BuildAsync(int companyId, IEnumerable<AppUser> users, DateRange range, string viewMode, bool canEditNotes) → ExcelCalendarTableViewModel
```
Overview **and** Team both call it → "looks exactly like Overview" by construction. **This modifies `OverviewModel`** (refactor its private build to delegate to the service). File-touch list includes `Overview.cshtml.cs`.

### 9.3 New page `Pages/Calendar/Team.cshtml(.cs)`
- Model builds the user set, calls `IOverviewCalendarBuilder.BuildAsync`, renders `ExcelCalendarTable` → identical look.
- **Row filter:** `u.CompanyId==SelectedCompanyId && u.JobTypeId==SelectedJobTypeId && u.AccountType==AccountType.Standard`, `IgnoreQueryFilters()` + explicit CompanyId (SAFE pattern, as Overview annotates).
- **Picker:** Company DDL from `GrantService.GetAccessibleCompanyIdsForGrantAsync(userId,"ViewShifts")` (`:323`) + own company (precedent: `Table.cshtml.cs:2872` uses the *molecule* variant; we need *companies*). JobType DDL from `JobTypeService.GetJobTypesForMoleculeAsync(company.MoleculeId)` (`IJobTypeService.cs:28`). Defaults: user's own Company + JobType. → **Alhut-lead-in-Tzafona:** opens on own company's Alhut, switch Company to see Alhut elsewhere in `ViewShifts` scope.
- **Server-side scope check** on the chosen (Company, JobType) — caller must have `ViewShifts` for that company (anti-IDOR on crafted params).
- **No SignalR group** (dropped per review — the calendar refresh is `grid.replaceWith`, not realtime; a `team-…` group would be dead code). If ever added, coalesce null jobType→0 per `CalendarHub.cs:375`.

### 9.4 Saved & private dynamic tables — NEW entity (resolves the /MyTeam contradiction)
Extending `TeamCalendar` would leak dynamic rows into `/MyTeam` (its list endpoint `api/team-calendars` returns *all* owner calendars). To keep **/MyTeam genuinely untouched**, add a separate lightweight entity:
```
DeskTeamView : IBelongsToCompany   // owner-scoped saved (Company×JobType) view
  Id, OwnerId, CompanyId(tenant), TargetCompanyId, JobTypeId, Name(<=60), SortOrder?, IsDeleted, CreatedAt, UpdatedAt
```
+ EF migration + `AppDbContext` config (unique name per owner; pattern from `TeamCalendar` config `:1103-1138`). New `IDeskTeamViewService` (list/create/rename/delete, owner-scoped via `_tenantResolver`) + a thin API or page handlers. "Add a new table" = create a `DeskTeamView` with the picked (Company, JobType). `/MyTeam`, `TeamCalendar`, `TeamCalendarService` are **not modified**.
- **Render-time IDOR check (security, per review):** when rendering a saved `DeskTeamView`, re-verify `GetAccessibleCompanyIdsForGrantAsync(userId,"ViewShifts")` contains its `TargetCompanyId` — the stored id could outlive the owner's scope or be tampered. Add a revoked-scope test.

**Files:** `NavRegistry.cs`; new `Pages/Calendar/Team.cshtml(.cs)`; `Overview.cshtml.cs` (extract builder); new `Services/IOverviewCalendarBuilder`+impl; new `Models/DeskTeamView.cs` + migration + `AppDbContext` + `Services/IDeskTeamViewService`+impl; resx `MyDesk`/`Nav_Team`/Team strings; small `team.js` for the picker (or reuse Overview toolbar JS).
**Tests:** Team shows only (Company×JobType) standard users; picker company list == `ViewShifts` scope; crafted out-of-scope Company/JobType rejected (create *and* render); `DeskTeamView` CRUD; `/MyTeam` unchanged (shows only its `TeamCalendar`s); NavRegistry parity passes.

---

## #10 — Persist UI language (fix "switched to Hebrew, reverted to English")
**No new column** — `AppUser.PreferredLanguage` exists (`:77`, migration `20260609222835`); `PreferredLanguageLearningMiddleware` (`:56-77`) already **writes** it passively. Read only by background email today.
**Root cause:** nothing **re-seeds** the `.AspNetCore.Culture` cookie from `PreferredLanguage`; on a fresh browser the stored value is never consulted → default (en-US unless `FF_HEBREW_DEFAULT`).
**Why not a custom provider:** `UseRequestLocalization` (`Program.cs:1940`) runs **before** `UseAuthentication` (`:1950`) — user is anonymous at culture-resolution time. Login re-seed sidesteps this.

**Design (decisions: both YES):**
1. **Login re-seed (essential).** In `Pages/Auth/Login.cshtml.cs` after the theme re-seed (`:400-415`, after `SignInAsync` `:398`): if `PreferredLanguage` set, write `.AspNetCore.Culture` via `CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage))` — `Path="/"`, +1yr, **`HttpOnly=false`** (so `localization-api.js:146-159` + JS toggles keep working), `SameSite=Lax`, `Secure=Request.IsHttps`. **Also write `.culture_explicit`** (matches `LanguageToggle/Default.cshtml:24`; needs only to be non-empty) so `LegacyCultureCookieResetMiddleware` (registered only under `FF_HEBREW_DEFAULT`, `Program.cs:1936`) won't strip a re-seeded `en-US`. Note: **no server-side culture-cookie writer exists today** — this is the first; nail the `c=..|uic=..` format (`MakeCookieValue` produces it).
2. **Griffin/ADFS parity (YES).** `Pages/Auth/GriffinCallback.cshtml.cs` signs in **without loading the `AppUser` entity** — its re-seed must add a `PreferredLanguage` DB read, then write the same cookie. (Theme re-seed shares this gap; fixing both is root-cause-complete.)
3. **Synchronous persist (YES).** Add `[Authorize] POST /Api/My/Language` (mirror `Pages/Api/My/Theme.cshtml.cs`) writing `PreferredLanguage` synchronously; both toggle sites call it before reload. Verified: `/Api/My` is already whitelisted in `ApiAuthenticationMiddleware.cs:396` — **no new registration needed**. Closes the "toggle then leave before next request" gap.

**Files:** `Pages/Auth/Login.cshtml.cs`, `Pages/Auth/GriffinCallback.cshtml.cs`, new `Pages/Api/My/Language.cshtml(.cs)`, `LanguageToggle/Default.cshtml` + `Login.cshtml` toggle JS. **Tests:** returning user `PreferredLanguage="he-IL"` on a cookieless request → Hebrew after login (both auth paths); re-seed writes the marker; toggle persists synchronously.

---

## Cross-cutting
- **Localization:** bilingual resx for every new string; grep key before adding. Reuse `ShiftCount`; add `TotalShifts`, `DaysOffline`, `VacationDays`, `MyDesk`, `Nav_Team`, Team strings (all verified absent).
- **Migrations:** `AppUser.SuppressSuccessToasts` (#6) + `DeskTeamView` table (#9). **#10 needs no column.** Create **sequentially on this branch** (shared `ModelSnapshot`) — never parallel worktrees.
- **Build/verify:** stop the :5000 exe before every rebuild (locked-exe rule); suite may need `xUnit.MaxParallelThreads=1`. The rebuild that browser-verifies each change also confirms #1/#2.
- **Deploy:** `FinalProductPublish` is generated — regenerate via `scripts/Update-FinalProductPublish.ps1 -Version`, never hand-edit.

## Decisions locked
- #6 → `AppUser` (global). #8 → per-user Total column. #9 nav → `/Calendar/Table` loses nav link (OK'd); saved views → **new `DeskTeamView` entity** (keeps /MyTeam untouched); reuse → **extract shared `IOverviewCalendarBuilder`**. #10 → Griffin parity **yes**, sync endpoint **yes**. #5 region-grouping **not needed** (user said "another company").
