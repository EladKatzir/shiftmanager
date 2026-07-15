# UI/Bug Batch + Team Page — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a 10-item batch — four bug fixes, three small UI changes, one authorization fix, one per-user preference, one language-persistence fix, and one new Team calendar page.

**Architecture:** ASP.NET Core 8 Razor Pages + EF Core (SQLite) + vanilla JS. Fixes are surgical and follow existing patterns (theme DB+cookie mirror, `ExcelCalendarTable` view component, grant-scope helpers). One new feature page reuses an *extracted* shared calendar builder plus a new saved-view entity.

**Tech Stack:** .NET 8, EF Core, Razor Pages, xUnit, vanilla JS, CSS.

**Design source:** `docs/superpowers/specs/2026-07-14-ui-batch-and-team-page-design.md` — read it before starting any task.

## Global Constraints
- **Navigate by symbol, not line number** — cited lines drift ±2–15 in current source.
- **Locked-executable rule:** stop the running :5000 dev exe (`taskkill //F //IM ShiftManager.exe` or ask the user) BEFORE any `dotnet build`/`run`. Never rebuild against a locked exe.
- **Migrations are sequential on THIS branch only** (`feat/ui-batch-team-2026-07`) — shared EF `ModelSnapshot`. Task 4's `AppUser` migration MUST be created and applied before Task 8's `DeskTeamView` migration. Never scaffold migrations in parallel worktrees.
- **Localization:** every new user-facing string → BOTH `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`. **Grep the key first** — a duplicate key breaks loc tests.
- **Tenant/scope:** use `_tenantResolver.GetCurrentTenantId()` (switcher-aware), never the raw CompanyId claim. `IgnoreQueryFilters()` requires an explicit CompanyId filter + a security comment.
- **Tests:** xUnit in `ShiftManager.Tests/`. Full suite may need `-- xUnit.MaxParallelThreads=1`. Use the real-SQLite fixture, not `UseInMemoryDatabase`.
- **Bilingual "offline":** use `"אופליין"` (as in `OFFLINE`/`ShiftType_OFFLINE`), NOT `"לא מקוון"`.

---

### Task 1: #4 — Sticky-header CSS fix (Admin/Users first row)

**Files:**
- Modify: `wwwroot/css/site.css` (after the `.data-table--sticky-header thead th` rule, ~`:7267`)

**Interfaces:** none (pure CSS).

- [ ] **Step 1: Add the scoped rule.** Append after the existing sticky-header block:
```css
/* Sticky-header tables inside a LOCAL scroll container use the container as the
   sticky reference, so the 64px app-header offset must not apply (it would shove
   the thead onto row 1). Viewport-scrolled tables (.section-card) keep top:64px. */
.table-responsive      .data-table--sticky-header thead th,
.table-wrapper         .data-table--sticky-header thead th,
.results-table-wrapper .data-table--sticky-header thead th { top: 0; }
```
- [ ] **Step 2: Verify served CSS.** Stop the exe, rebuild, restart. `curl -s http://localhost:5000/css/site.css | grep -c "results-table-wrapper .data-table--sticky-header"` → Expected: `1`.
- [ ] **Step 3: Browser-verify.** Load `/Admin/Users`, scroll the table to top: the first user row is fully visible and clickable (not under the header). Load `/Admin/Organization/Grants` and `/Admin/Organization/Roles`: their headers still sit *below* the app header (no regression).
- [ ] **Step 4: Commit** — `git commit -am "fix(admin): sticky table header no longer hides first row (#4)"`

---

### Task 2: #5 — "This Week" card: shift count + offline/vacation split

**Files:**
- Modify: `Pages/Home/Index.cshtml.cs` (week-summary computation, ~`:166-178`; props ~`:41-43`)
- Modify: `Pages/Home/Index.cshtml` (~`:109-135`)
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx`
- Test: `ShiftManager.Tests/UnitTests/Pages/HomeWeekSummaryTests.cs` (create; or nearest existing Home test)

**Interfaces:**
- Produces: model props `int ShiftsThisWeek`, `int DaysWithShiftsThisWeek`, `int OfflineDaysThisWeek`, `int VacationDaysThisWeek` on `HomeIndexModel` (exact class name per file).

- [ ] **Step 1: Write the failing test** for disjoint day classification (the critical overlap case):
```csharp
// A week where one day has BOTH an approved vacation AND a HOME shift must not double-count.
[Fact]
public async Task WeekSummary_VacationOverlappingHomeShift_CountsAreDisjointAndSumTo7()
{
    // Arrange: seed user with 2 real (non-home/offline) shift days, 1 approved vacation day
    // that ALSO has a HOME shift, remaining days empty.
    // Act: run the week-summary computation.
    // Assert:
    Assert.Equal(2, model.ShiftsThisWeek);          // real shifts only
    Assert.Equal(2, model.DaysWithShiftsThisWeek);
    Assert.Equal(1, model.VacationDaysThisWeek);     // the overlap day is vacation, not shift
    Assert.Equal(4, model.OfflineDaysThisWeek);
    Assert.Equal(7, model.DaysWithShiftsThisWeek + model.VacationDaysThisWeek + model.OfflineDaysThisWeek);
    Assert.True(model.OfflineDaysThisWeek >= 0);
}
```
- [ ] **Step 2: Run it — Expected FAIL** (props/logic absent). `dotnet test --filter WeekSummary_VacationOverlappingHomeShift`
- [ ] **Step 3: Implement set-based classification** in the common summary method. Replace the count-subtraction logic with:
```csharp
var weekDays = Enumerable.Range(0, 7).Select(i => startOfWeek.AddDays(i)).ToList();
// Real shift days (exclude HOME + OFFLINE presence).
var shiftDays = weekShifts
    .Where(sa => sa.ShiftInstance.ShiftType != null
              && !sa.ShiftInstance.ShiftType.IsHome && !sa.ShiftInstance.ShiftType.IsOffline)
    .Select(sa => sa.ShiftInstance.WorkDate.Date).ToHashSet();
// Approved vacation days overlapping the week (NEW query — the :184 one is employee-only).
var approvedVac = await _context.TimeOffRequests
    .Where(r => r.UserId == userId && r.Status == RequestStatus.Approved
             && r.StartDate.Date <= weekDays[^1] && r.EndDate.Date >= weekDays[0])
    .Select(r => new { r.StartDate, r.EndDate }).ToListAsync();
var vacationDays = weekDays.Where(d =>
    approvedVac.Any(v => v.StartDate.Date <= d && v.EndDate.Date >= d)
    && !shiftDays.Contains(d)).ToHashSet();   // precedence: real shift beats vacation
ShiftsThisWeek           = weekShifts.Count(sa => sa.ShiftInstance.ShiftType != null
                              && !sa.ShiftInstance.ShiftType.IsHome && !sa.ShiftInstance.ShiftType.IsOffline);
DaysWithShiftsThisWeek   = shiftDays.Count;
VacationDaysThisWeek     = vacationDays.Count;
OfflineDaysThisWeek      = weekDays.Count(d => !shiftDays.Contains(d) && !vacationDays.Contains(d));
```
(Verify exact property names of `ShiftInstance`/`ShiftType`/`WorkDate`/`RequestStatus` against source; `IsHome`/`IsOffline` are on `ShiftType`.)
- [ ] **Step 4: Add loc keys** (grep first): `DaysOffline` = "Offline Days" / "ימי אופליין"; `VacationDays` = "Vacation Days" / "ימי חופש". Reuse existing `ShiftCount` for the shift-count label.
- [ ] **Step 5: Update the view** `Index.cshtml`: render `@Model.ShiftsThisWeek` with `<loc key="ShiftCount"/>` (drop the `HoursThisWeek` row); relabel the offline row to `<loc key="DaysOffline"/>` showing `@Model.OfflineDaysThisWeek`; add a Vacation row (`<loc key="VacationDays"/>` + `@Model.VacationDaysThisWeek`) wrapped in `@if (Model.VacationDaysThisWeek > 0) { … }`.
- [ ] **Step 6: Run test — Expected PASS.** Then `dotnet test --filter HomeWeekSummary`.
- [ ] **Step 7: Browser-verify** `/Home` for a user with an approved vacation this week: shift count, offline days, vacation days shown; numbers sum to 7.
- [ ] **Step 8: Commit** — `git commit -am "feat(home): This-Week card shows shift count + offline/vacation split (#5)"`

---

### Task 3: #8 — Week-view Total column → shift count

**Files:**
- Modify: `ViewComponents/ExcelCalendarTableViewComponent.cs` (`ExcelCalendarRow`, ~`:69`)
- Modify: `ViewComponents/ExcelCalendarRowViewModel.cs` (`HasWeeklyHours` mirror, ~`:10`)
- Modify: `Pages/Calendar/Shifts.cshtml.cs` (builders `BuildUserRow` ~`:804`, and ~`:726`, ~`:764`)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` (~`:15-16`, `:70-75`), `_CalendarRow.cshtml` (~`:233-241`)
- Modify: resx (both) — new key `TotalShifts`
- Test: `ShiftManager.Tests/UnitTests/.../ExcelCalendarRowTests.cs` or a Shifts builder test (create if none)

**Interfaces:**
- Produces: `ExcelCalendarRow.ShiftCount` (`int?`), `ExcelCalendarRowViewModel.HasShiftCount` (`bool`).

- [ ] **Step 1: Write the failing test** — a by-user row for a week with 3 real + 1 HOME + 1 OFFLINE assignment has `ShiftCount == 3`:
```csharp
[Fact]
public void BuildUserRow_ShiftCount_ExcludesHomeAndOffline()
{
    // Arrange assignments: 3 real, 1 IsHome, 1 IsOffline in [Start,End].
    // Act: build the user row.
    Assert.Equal(3, row.ShiftCount);
}
```
- [ ] **Step 2: Run — Expected FAIL** (property missing).
- [ ] **Step 3: Add `public int? ShiftCount { get; set; }`** to `ExcelCalendarRow`; add `public bool HasShiftCount { get; set; }` to `ExcelCalendarRowViewModel`.
- [ ] **Step 4: Set it at all 3 builders** (alongside/replacing `WeeklyHours`):
```csharp
row.ShiftCount = assignments.Count(a => a.UserId == user.Id
    && a.ShiftInstance.WorkDate >= StartDate && a.ShiftInstance.WorkDate <= EndDate
    && !a.ShiftInstance.ShiftType.IsHome && !a.ShiftInstance.ShiftType.IsOffline);
```
- [ ] **Step 5: Render.** In `Default.cshtml` drive the column by `Model.Rows.Any(r => r.ShiftCount.HasValue)` and header `@Localizer["TotalShifts"]`; in `_CalendarRow.cshtml` render `@row.ShiftCount` (no `h` suffix). Add `TotalShifts` = "Total Shifts" / "סה\"כ משמרות" to both resx.
- [ ] **Step 6: Run test — Expected PASS.**
- [ ] **Step 7: Browser-verify** `/Calendar/Shifts?...&ViewMode=week&Mode=user`: Total column shows integer counts, no `h`.
- [ ] **Step 8: Commit** — `git commit -am "feat(shifts): week-view Total column shows shift count not hours (#8)"`

---

### Task 4: #6 — Suppress success toasts (AppUser pref) — FIRST MIGRATION

**Files:**
- Modify: `Models/AppUser.cs` (~`:67-68`)
- Create: `Migrations/<stamp>_AddSuppressSuccessToasts.cs` (via `dotnet ef migrations add`)
- Modify: `Pages/My/Settings.cshtml` (+ `.cshtml.cs` bind/save, ~`:193`)
- Modify: `Pages/Shared/_Layout.cshtml` (emit `window.UserPrefs` before `toast-notifications.js` ~`:183`; or set cookie read)
- Modify: `wwwroot/js/toast-notifications.js` (~`:263`), `wwwroot/js/calendar-inline-edit.js` (~`:897`)
- Test: `ShiftManager.Tests/UnitTests/Pages/SettingsSuppressToastsTests.cs`

**Interfaces:**
- Produces: `AppUser.SuppressSuccessToasts` (`bool`); client global `window.UserPrefs.suppressSuccessToasts` (`bool`).

- [ ] **Step 1: Failing test** — POSTing the toggle persists it:
```csharp
[Fact]
public async Task Settings_Post_PersistsSuppressSuccessToasts()
{
    // Arrange model bound with SuppressSuccessToasts=true for a seeded user.
    await model.OnPostAsync();
    var reloaded = await db.Users.FindAsync(userId);
    Assert.True(reloaded.SuppressSuccessToasts);
}
```
- [ ] **Step 2: Run — Expected FAIL.**
- [ ] **Step 3: Add `public bool SuppressSuccessToasts { get; set; }`** to `AppUser`.
- [ ] **Step 4: Scaffold + apply migration** (exe stopped): `dotnet ef migrations add AddSuppressSuccessToasts` then `dotnet ef database update`. Verify the migration only adds this column.
- [ ] **Step 5: Bind + save** in `Settings.cshtml.cs`: add `[BindProperty] public bool SuppressSuccessToasts { get; set; }`; in `OnGetAsync` load from the user; in `OnPostAsync` set `user.SuppressSuccessToasts = SuppressSuccessToasts;` before `SaveChangesAsync`. Add a labeled toggle in `Settings.cshtml` (new loc key `SuppressSuccessToasts_Label`, bilingual).
- [ ] **Step 6: Surface to client** — in `_Layout.cshtml` head, before `toast-notifications.js`:
```html
<script>window.UserPrefs = Object.assign(window.UserPrefs||{}, { suppressSuccessToasts: @(((ClaimsPrincipal)User).... ? "true" : "false") });</script>
```
Preferred: render from a per-request value the layout already resolves, OR mirror a `suppress_success_toasts` non-HttpOnly cookie in `OnPostAsync` (like theme) and read it. Pick the cookie-mirror path for zero per-request DB hit (see spec).
- [ ] **Step 7: Gate BOTH source renderers** — at the top of `showToast` in `toast-notifications.js` AND the local `showToast` in `calendar-inline-edit.js`:
```js
if (type === 'success' && window.UserPrefs && window.UserPrefs.suppressSuccessToasts) return;
```
- [ ] **Step 8: Run test — Expected PASS.**
- [ ] **Step 9: Browser-verify** — enable the toggle; assign a user on the roster board (`/Calendar/Table`) AND inline on `/Calendar/Shifts`: no success toast either place; trigger an error → error toast still shows.
- [ ] **Step 10: Commit** — `git commit -am "feat(settings): per-user opt-out for success toasts (#6)"`

---

### Task 5: #3 — Scope-aware eligibility editor + close IDOR

**Files:**
- Modify: `Pages/Scheduling/Eligibility/Index.cshtml.cs` + `Index.cshtml`
- Test: `ShiftManager.Tests/UnitTests/Pages/EligibilityScopeTests.cs`

**Interfaces:**
- Consumes: `GrantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageShiftCategories")`.
- Produces: bindable `int? MoleculeId` (SupportsGet); scoped handlers.

- [ ] **Step 1: Failing tests** (two):
```csharp
[Fact] // Owner sees >1 molecule
public async Task Owner_Get_ListsAllProjectMolecules() { /* seed owner + 2 molecules; assert AvailableMolecules.Count >= 2 */ }

[Fact] // IDOR closed
public async Task Toggle_ForUserOutsideCallerScope_IsRejected()
{
    // caller has ManageShiftCategories for molecule A only; POST toggle for a molecule-B user+category
    var result = await model.OnPostToggleAsync(reqForMoleculeB);
    // Assert 403/Forbid or unchanged membership.
}
```
- [ ] **Step 2: Run — Expected FAIL.**
- [ ] **Step 3: Add molecule selector** — `[BindProperty(SupportsGet=true)] public int? MoleculeId { get; set; }`; in `OnGetAsync` compute `AvailableMolecules` from `GetAccessibleMoleculeIdsForGrantAsync(userId,"ManageShiftCategories")` (default `MoleculeId ??= _currentUser.MoleculeId`), and load `ShiftCategoryOptions`/`ChoreCategoryOptions`/`UserOptions` for `MoleculeId.Value`. Do NOT filter by area. Add the `<select>` to `Index.cshtml` (auto-submit on change).
- [ ] **Step 4: Add scope re-checks** to `OnPostToggleAsync`, `OnGetCategoryAsync`, `OnGetPersonAsync`: resolve the target's molecule (from the user or the category) and require `await _grantService.HasGrantWithScopeAsync(userId, "ManageShiftCategories", moleculeId: targetMolecule)` (mirror `Admin/Users.cshtml.cs:1795-1802`); else `Forbid()`/`return`.
- [ ] **Step 5: Run tests — Expected PASS.**
- [ ] **Step 6: Browser-verify** as Owner: molecule dropdown lists multiple molecules; switching shows that molecule's users/categories; toggling persists.
- [ ] **Step 7: Commit** — `git commit -am "fix(eligibility): scope-aware molecule selector for Owner/AreaAdmin + close IDOR (#3)"`

---

### Task 6: #10 — Persist UI language (login re-seed + Griffin + sync endpoint)

**Files:**
- Modify: `Pages/Auth/Login.cshtml.cs` (after theme re-seed, ~`:415`)
- Modify: `Pages/Auth/GriffinCallback.cshtml.cs` (load `PreferredLanguage`, re-seed)
- Create: `Pages/Api/My/Language.cshtml` + `.cshtml.cs`
- Modify: `Pages/Shared/Components/LanguageToggle/Default.cshtml`, `Pages/Auth/Login.cshtml` (call the endpoint before reload)
- Test: `ShiftManager.Tests/UnitTests/.../CultureReseedTests.cs`

**Interfaces:**
- Consumes: `CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture))`.
- Produces: `POST /Api/My/Language` (body `{ culture }`), writes `AppUser.PreferredLanguage`.

- [ ] **Step 1: Failing test** — re-seed helper writes the culture + explicit cookies from `PreferredLanguage`:
```csharp
[Fact]
public void ReseedCultureCookie_WritesAspNetCultureAndExplicitMarker()
{
    // Arrange httpContext + user.PreferredLanguage = "he-IL".
    LoginModel.ReseedCultureCookie(httpContext, "he-IL");   // extract as a testable static/helper
    Assert.Contains(".AspNetCore.Culture", setCookieHeaders);
    Assert.Contains("c=he-IL|uic=he-IL", cultureCookieValue);
    Assert.Contains(".culture_explicit", setCookieHeaders);
}
```
- [ ] **Step 2: Run — Expected FAIL.**
- [ ] **Step 3: Implement the re-seed helper** and call it after `SignInAsync` in `Login.cshtml.cs` (right after the theme block) when `!string.IsNullOrEmpty(user.PreferredLanguage)`:
```csharp
Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)),
    new CookieOptions { Path="/", Expires=DateTimeOffset.UtcNow.AddYears(1),
                        HttpOnly=false, SameSite=SameSiteMode.Lax, Secure=Request.IsHttps });
Response.Cookies.Append(".culture_explicit", "v2",
    new CookieOptions { Path="/", Expires=DateTimeOffset.UtcNow.AddYears(1), SameSite=SameSiteMode.Lax });
```
- [ ] **Step 4: Griffin parity** — in `GriffinCallback.cshtml.cs`, after sign-in, load the `AppUser` (a DB read — it currently doesn't) and call the same re-seed.
- [ ] **Step 5: Sync endpoint** — create `Pages/Api/My/Language.cshtml.cs` (`[Authorize][IgnoreAntiforgeryToken]`, self-scoped via `ClaimTypes.NameIdentifier`, POST writes `user.PreferredLanguage` from an allow-listed `{ "en-US", "he-IL" }` value). Update both toggles to `fetch('/Api/My/Language',{method:'POST',...})` before `location.reload()`. (No middleware registration needed — `/Api/My` already whitelisted.)
- [ ] **Step 6: Run test — Expected PASS.**
- [ ] **Step 7: Browser-verify** — set language to Hebrew, clear the `.AspNetCore.Culture` cookie, log out/in → UI comes back Hebrew.
- [ ] **Step 8: Commit** — `git commit -am "fix(i18n): persist UI language via login re-seed from AppUser.PreferredLanguage (#10)"`

---

### Task 7: #9.2 — Extract shared `IOverviewCalendarBuilder`

**Files:**
- Create: `Services/IOverviewCalendarBuilder.cs` + `Services/OverviewCalendarBuilder.cs`
- Modify: `Pages/Calendar/Overview.cshtml.cs` (delegate its private build to the service)
- Modify: `Program.cs` (DI registration)
- Test: `ShiftManager.Tests/UnitTests/Services/OverviewCalendarBuilderTests.cs`

**Interfaces:**
- Produces: `Task<ExcelCalendarTableViewModel> BuildAsync(int companyId, IReadOnlyList<AppUser> users, DateOnly start, DateOnly end, string viewMode, bool canEditNotes)` (adjust date types to match Overview).

- [ ] **Step 1: Failing test** — builder returns one row per user with correct dates:
```csharp
[Fact]
public async Task BuildAsync_ReturnsRowPerUser_WithOverviewShape()
{
    var vm = await builder.BuildAsync(companyId, new[]{u1,u2}, start, end, "week", canEditNotes:false);
    Assert.Equal(2, vm.Rows.Count);
    Assert.Equal("overview", vm.CalendarType);
}
```
- [ ] **Step 2: Run — Expected FAIL.**
- [ ] **Step 3: Move** the `LoadVacationsAsync/LoadShiftsAsync/LoadChoresAsync/LoadOnDutiesAsync/BuildCellsForUser`/notes-loading logic out of `OverviewModel` into `OverviewCalendarBuilder`, parameterized by (companyId, users, range, viewMode, canEditNotes). Keep behavior identical (same `CalendarType="overview"`, `RowMode`, notes, HOME fields).
- [ ] **Step 4: Refactor `OverviewModel`** to build its user set (`LoadUsersAsync`) then call `_builder.BuildAsync(...)` and assign `CalendarData`. Register the service in `Program.cs`.
- [ ] **Step 5: Run builder test — Expected PASS. Run existing Overview tests (if any) — Expected PASS (no behavior change).**
- [ ] **Step 6: Browser-verify** `/Calendar/Overview` renders exactly as before.
- [ ] **Step 7: Commit** — `git commit -am "refactor(calendar): extract IOverviewCalendarBuilder for reuse (#9)"`

---

### Task 8: #9.4 — `DeskTeamView` entity + migration + service — SECOND MIGRATION

**Files:**
- Create: `Models/DeskTeamView.cs`, `Services/IDeskTeamViewService.cs` + impl
- Modify: `Data/AppDbContext.cs` (DbSet + config + query filter), `Program.cs` (DI)
- Create: `Migrations/<stamp>_AddDeskTeamView.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/DeskTeamViewServiceTests.cs`

**Interfaces:**
- Produces: `DeskTeamView { int Id; int CompanyId; int OwnerId; int TargetCompanyId; int JobTypeId; string Name; int? SortOrder; bool IsDeleted; DateTime CreatedAt; DateTime UpdatedAt; }` (`IBelongsToCompany`); `IDeskTeamViewService` with `ListForOwnerAsync`, `CreateAsync(targetCompanyId, jobTypeId, name)`, `RenameAsync`, `DeleteAsync` (owner-scoped, soft-delete).

- [ ] **Step 1: Failing test** — create then list returns the view for its owner only:
```csharp
[Fact]
public async Task Create_ThenList_ReturnsOwnerScopedView()
{
    var v = await svc.CreateAsync(targetCompanyId: 5, jobTypeId: 2, name: "Alhut Tzafona");
    var mine = await svc.ListForOwnerAsync();
    Assert.Contains(mine, x => x.Id == v.Id);
    Assert.Equal(5, v.TargetCompanyId);
}
```
- [ ] **Step 2: Run — Expected FAIL.**
- [ ] **Step 3: Add entity + DbSet + config** (unique name per owner; `HasQueryFilter` for `CompanyId == tenant` + `!IsDeleted`, mirroring `TeamCalendar` config). **Ensure Task 4's migration is already applied** before scaffolding.
- [ ] **Step 4: Scaffold + apply migration** (exe stopped): `dotnet ef migrations add AddDeskTeamView` → `dotnet ef database update`. Verify it adds only the `DeskTeamView` table.
- [ ] **Step 5: Implement the service** (owner-scoped via `_tenantResolver` + `ClaimTypes.NameIdentifier`); register in `Program.cs`.
- [ ] **Step 6: Run test — Expected PASS.**
- [ ] **Step 7: Commit** — `git commit -am "feat(team): DeskTeamView entity + service for saved dynamic views (#9)"`

---

### Task 9: #9.3 — Team page (picker + filter + render + IDOR checks)

**Files:**
- Create: `Pages/Calendar/Team.cshtml` + `.cshtml.cs`
- Optional: `wwwroot/js/team.js` (picker) — or reuse Overview toolbar JS
- Modify: resx (both) — Team page strings
- Test: `ShiftManager.Tests/UnitTests/Pages/TeamPageTests.cs`

**Interfaces:**
- Consumes: `IOverviewCalendarBuilder.BuildAsync`, `IDeskTeamViewService`, `GrantService.GetAccessibleCompanyIdsForGrantAsync(userId,"ViewShifts")`, `JobTypeService.GetJobTypesForMoleculeAsync`.

- [ ] **Step 1: Failing tests** (filter + IDOR):
```csharp
[Fact] public async Task Get_ShowsOnlyStandardUsersOfChosenCompanyAndJobType() { /* assert rows == matching users */ }
[Fact] public async Task Get_ForCompanyOutsideViewShiftsScope_IsRejected() { /* crafted TargetCompanyId → Forbid/empty */ }
[Fact] public async Task RenderSavedView_AfterScopeRevoked_IsRejected() { /* stored DeskTeamView.TargetCompanyId no longer in scope → Forbid */ }
```
- [ ] **Step 2: Run — Expected FAIL.**
- [ ] **Step 3: Implement `TeamModel`** (`[Authorize]`): bind `SelectedCompanyId`/`SelectedJobTypeId` (default own company + own job type); build the picker lists from `GetAccessibleCompanyIdsForGrantAsync(...,"ViewShifts")` (+ own company) and `GetJobTypesForMoleculeAsync(company.MoleculeId)`; **re-check** the chosen company is in scope (Forbid otherwise); load users (`CompanyId==sel && JobTypeId==sel && AccountType.Standard`, `IgnoreQueryFilters()` + explicit filter + security comment); call `_builder.BuildAsync(...)`; render `@await Component.InvokeAsync("ExcelCalendarTable", Model.CalendarData)`.
- [ ] **Step 4: Saved views** — list the owner's `DeskTeamView`s (a select/tabs + "Add" button POSTing a new one from the current picker; delete/rename). On rendering a saved view, re-verify its `TargetCompanyId` is in `ViewShifts` scope.
- [ ] **Step 5: Add loc keys** (bilingual): page title, picker labels, "Add table", empty state.
- [ ] **Step 6: Run tests — Expected PASS.**
- [ ] **Step 7: Browser-verify** the Alhut-lead flow: page opens on own company's job type; switch Company → other company's same job type; add a saved table; reload → it persists; a non-lead sees only their own company.
- [ ] **Step 8: Commit** — `git commit -am "feat(team): JobType×Company Team calendar page with saved views (#9)"`

---

### Task 10: #9.1 — Nav: rename Overview→My Desk, replace Table→Team

**Files:**
- Modify: `Services/Navigation/NavRegistry.cs` (`:45`, `:46`)
- Modify: resx (both) — `MyDesk`, `Nav_Team`
- Test: existing `NavRegistryPolicyParityTests` / `NavRegistryTests` must pass.

- [ ] **Step 1: Add loc keys** `MyDesk` = "My Desk" / "הדסק שלי"; `Nav_Team` = "Team" / "צוות".
- [ ] **Step 2: Edit NavRegistry** — change `:46` to `Link("MyDesk", "/Calendar/Overview", icon:"eye")`; change `:45` to `Link("Nav_Team", "/Calendar/Team", policy: null, icon:"users")`.
- [ ] **Step 3: Run nav tests** — `dotnet test --filter NavRegistry` — Expected PASS (Team page is `[Authorize]`, so null nav policy is valid; parity holds).
- [ ] **Step 4: Browser-verify** the sidebar shows "הדסק שלי" and "צוות"; both links work; Team visible to a non-manager.
- [ ] **Step 5: Commit** — `git commit -am "feat(nav): rename Overview→My Desk, replace Shifts-Management→Team (#9)"`

---

### Task 11: #1/#2 + full verification

**Files:** none (verification), unless step 4 finds a real bug.

- [ ] **Step 1: Full rebuild** (exe stopped) + run the full test suite: `dotnet test -- xUnit.MaxParallelThreads=1`. Expected: all green (baseline was 1849/1849 pre-batch; expect that + new tests).
- [ ] **Step 2: Start the freshly built exe.**
- [ ] **Step 3: #1/#2 —** load `/Calendar/Shifts?MoleculeId=1&JobTypeId=2&ViewMode=week&Mode=user`, open the browser console. Expected: **no** "config is not defined" and **no** `error_boundary_triggered`. Hard-refresh to clear any cached JS.
- [ ] **Step 4: If the error persists on the fresh build** — capture the exact console `source:line`, open that file, fix the real bug, commit. (Per spec, this is unlikely — source is clean.)
- [ ] **Step 5: Regression sweep** — click through: Admin/Users (row 1), Home (This-Week card), Shifts week Total column, Settings toast toggle, Eligibility as Owner, language persistence, Team page + saved view, sidebar labels. Confirm `/MyTeam` still shows only its manual calendars.
- [ ] **Step 6: Commit** any fixes — `git commit -am "chore: batch verification + #1/#2 stale-asset confirmation"`

---

## Self-Review (completed)
- **Coverage:** #1/#2 → Task 11; #3 → Task 5; #4 → Task 1; #5 → Task 2; #6 → Task 4; #8 → Task 3; #9 → Tasks 7–10; #10 → Task 6. All spec sections mapped.
- **Migration ordering:** Task 4 (AppUser) precedes Task 8 (DeskTeamView) — sequential, one branch. ✓
- **Dependencies:** Task 9 depends on Tasks 7 (builder) + 8 (entity); Task 10 depends on Task 9 (route must exist). Tasks 1,2,3,5,6 are independent.
- **Type consistency:** `ShiftCount`/`HasShiftCount` (Task 3), `IOverviewCalendarBuilder.BuildAsync` (Tasks 7→9), `IDeskTeamViewService` (Tasks 8→9), `SuppressSuccessToasts`/`window.UserPrefs.suppressSuccessToasts` (Task 4) — names consistent across tasks.
