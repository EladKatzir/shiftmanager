# Account Types (mil/groupuser) + Analytics Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two orthogonal account types (`mil`, `groupuser`) enforced by one central capability gate across 7 surfaces, plus four analytics changes (lead+ access, DoesShifts shift-metric filter, ShiftCategory breakdown, chart tooltips).

**Architecture:** `AppUser.AccountType` enum (Standard/Mil/GroupUser) is orthogonal to JobType/Role. A static `UserCapabilities` surface holds ALL the rules; chokepoints call it. Analytics reuses the existing JusticeService/grant machinery — "lead+" is a one-line grant-assignment removal, not new policy code.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core (SQLite), xUnit (run SEQUENTIALLY: `dotnet test -- xUnit.ParallelizeTestCollections=false`), Playwright via `webapp-testing` skill. Dev app at http://localhost:5000.

**Spec:** `docs/superpowers/specs/2026-06-14-account-types-and-analytics-design.md`

---

## Environment & rules (read once)

- **Branch:** `dev`. `git add` ONLY the files each task names — the working tree has unrelated changes; never stage them.
- **Locked-executable rule (CLAUDE.md §3):** never `dotnet build`/`run` while the app runs. Stop first:
  ```bash
  powershell -Command "Get-Process -Name ShiftManager -ErrorAction SilentlyContinue | Stop-Process -Force"
  powershell -Command "Get-Process -Name ShiftManager -ErrorAction SilentlyContinue"   # expect no output
  ```
  Then build/test, and restart with `dotnet run --urls http://localhost:5000` (background; wait for "Now listening"). Stale static assets are served until restart — curl a marker before browser-verifying cshtml/css changes.
- **Tests run sequentially.** Always `dotnet test -- xUnit.ParallelizeTestCollections=false` (parallel produces ~hundreds of spurious SQLite :memory: failures). To run one test: `dotnet test --filter "FullyQualifiedName~UserCapabilitiesTests" -- xUnit.ParallelizeTestCollections=false`.
- **Login for browser checks:** owner2@test / Test1234! at /Auth/Login (Owner).
- **Localization:** every new user-facing string → a key in BOTH `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`. GREP THE KEY FIRST (a duplicate key has broken localization tests before).
- **EF translatability:** in `IQueryable` filters use enum equality on the column (`u.AccountType == AccountType.GroupUser`) — do NOT call the `UserCapabilities` extension methods inside a LINQ-to-SQL query (they won't translate). Use the extension methods for in-memory objects and the raw enum comparison inside queries. Each query task states the exact predicate.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `Models/Support/AccountType.cs` | The enum | Create |
| `Models/AppUser.cs` | + `AccountType` property | Modify (~line 95) |
| `Migrations/*_AddAccountTypeToAppUser.*` | Schema column, default 0 | Create (EF) |
| `Models/Support/UserCapabilities.cs` | Central capability gate (extension methods) | Create |
| `Pages/Calendar/Overview.cshtml.cs` | Roster excludes mil+groupuser | Modify (~200) |
| `Services/ShiftCalendarService.cs` | Roster excludes groupuser | Modify (~36) |
| `Pages/Calendar/Chores.cshtml.cs` | Roster excludes mil+groupuser | Modify (~349) |
| `Pages/My/Requests.cshtml.cs`, `Pages/Requests/Index.cshtml.cs` | Lockout | Modify |
| `Services/VacationApprovalService.cs` | Approver pool excludes mil+groupuser | Modify (~1508) |
| `Services/BusyService.cs` | Hard-block groupuser (shift) + mil/groupuser (chore) | Modify (~196, ~325) |
| `Pages/Admin/Users.cshtml(.cs)` | Inline AccountType editor + handler + badge + type-change cleanup | Modify |
| `Data/SeedData/QaTestUserSeed.cs` | mil.tz@test, groupuser.tz@test | Modify |
| `Data/SeedData/RoleTemplateSeed.cs` | Remove grant 133 from Assigner (8) | Modify (~1019) |
| `Services/JusticeService.cs` | Exclude groupuser; DoesShifts shift-metric filter; category filter | Modify (~200,440,512,595) |
| `Services/JusticeViewModels.cs` / `JusticeEnums.cs` | `JusticeQuery.ShiftCategoryId` | Modify |
| `Pages/Admin/Analytics.cshtml(.cs)` | Category dropdown; tooltips; captions | Modify |
| `Resources/SharedResources*.resx` | New keys | Modify |
| `ShiftManager.Tests/...` | New unit + behavior tests | Create/Modify |

---

## PHASE A1 — Foundation

### Task 1: AccountType enum + AppUser property + migration

**Files:**
- Create: `Models/Support/AccountType.cs`
- Modify: `Models/AppUser.cs` (~line 95, near `DoesShifts`)
- Create: EF migration via CLI

- [ ] **Step 1: Create the enum**

`Models/Support/AccountType.cs`:
```csharp
namespace ShiftManager.Models.Support;

/// <summary>
/// Account archetype, orthogonal to JobType/Role. Standard = a normal person.
/// Mil = shared reserve (מילואים) account: does shifts, no chores, no Requests, hidden from Overview.
/// GroupUser = administrative shared (יוזר קיבוצי) account: no shifts/chores/assignment, no Requests, hidden from all calendars+analytics.
/// Both Mil and GroupUser retain role-based powers and normal email notifications.
/// </summary>
public enum AccountType
{
    Standard = 0,
    Mil = 1,
    GroupUser = 2
}
```

- [ ] **Step 2: Add the property to AppUser**

In `Models/AppUser.cs`, after the `DoesShifts` property (~line 95), add (ensure `using ShiftManager.Models.Support;` is present):
```csharp
/// <summary>
/// Account archetype (Standard/Mil/GroupUser). Orthogonal to JobType + Role.
/// Drives capability/visibility via UserCapabilities. Defaults to Standard.
/// </summary>
public AccountType AccountType { get; set; } = AccountType.Standard;
```

- [ ] **Step 3: Create the migration (app must be stopped first)**

```bash
powershell -Command "Get-Process -Name ShiftManager -ErrorAction SilentlyContinue | Stop-Process -Force"
dotnet ef migrations add AddAccountTypeToAppUser
```
Expected: a new `Migrations/<timestamp>_AddAccountTypeToAppUser.cs` adding an `AccountType` INTEGER column to `Users` with default `0`. Open it and confirm `defaultValue: 0` (so existing rows backfill to Standard). If the column lacks a default, add `defaultValue: 0` to the `AddColumn<int>` call.

- [ ] **Step 4: Build + apply + verify**

```bash
dotnet build
```
Expected: succeeds. The migration auto-applies on app start (the project applies migrations at startup) — or run `dotnet ef database update`. Confirm no existing test regressed:
```bash
dotnet test --filter "FullyQualifiedName~AppDbContext" -- xUnit.ParallelizeTestCollections=false
```

- [ ] **Step 5: Commit**

```bash
git add Models/Support/AccountType.cs Models/AppUser.cs Migrations/
git commit -m "feat(accounts): add AccountType enum + AppUser.AccountType (default Standard) + migration"
```

---

### Task 2: UserCapabilities central gate + unit tests

**Files:**
- Create: `Models/Support/UserCapabilities.cs`
- Test: `ShiftManager.Tests/UnitTests/Models/UserCapabilitiesTests.cs`

- [ ] **Step 1: Write the failing test (full matrix)**

`ShiftManager.Tests/UnitTests/Models/UserCapabilitiesTests.cs`:
```csharp
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

public class UserCapabilitiesTests
{
    private static AppUser U(AccountType t, bool doesShifts = true) =>
        new() { AccountType = t, DoesShifts = doesShifts };

    [Theory]
    // CanBeAssignedShift: everyone except GroupUser
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, true)]
    [InlineData(AccountType.GroupUser, false)]
    public void CanBeAssignedShift_matrix(AccountType t, bool expected) =>
        U(t).CanBeAssignedShift().Should().Be(expected);

    [Theory] // CanDoChores: Standard only
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, false)]
    [InlineData(AccountType.GroupUser, false)]
    public void CanDoChores_matrix(AccountType t, bool expected) =>
        U(t).CanDoChores().Should().Be(expected);

    [Theory] // CanAccessRequests + CanBeVacationApprover: Standard only
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, false)]
    [InlineData(AccountType.GroupUser, false)]
    public void Requests_matrix(AccountType t, bool expected)
    {
        U(t).CanAccessRequests().Should().Be(expected);
        U(t).CanBeVacationApprover().Should().Be(expected);
    }

    [Theory] // Overview + Chores calendar visibility: Standard only
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, false)]
    [InlineData(AccountType.GroupUser, false)]
    public void OverviewAndChoreVisibility_matrix(AccountType t, bool expected)
    {
        U(t).IsVisibleOnOverview().Should().Be(expected);
        U(t).IsVisibleOnChoresCalendar().Should().Be(expected);
    }

    [Theory] // Shifts calendar + Analytics visibility: not GroupUser
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, true)]
    [InlineData(AccountType.GroupUser, false)]
    public void ShiftsAndAnalyticsVisibility_matrix(AccountType t, bool expected)
    {
        U(t).IsVisibleOnShiftsCalendar().Should().Be(expected);
        U(t).IsVisibleInAnalytics().Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run — verify it fails to compile (UserCapabilities not defined)**

```bash
dotnet test --filter "FullyQualifiedName~UserCapabilitiesTests" -- xUnit.ParallelizeTestCollections=false
```
Expected: compile error (UserCapabilities/methods missing).

- [ ] **Step 3: Implement the gate**

`Models/Support/UserCapabilities.cs`:
```csharp
namespace ShiftManager.Models.Support;

/// <summary>
/// Single source of truth for what each AccountType may do / where it is visible.
/// Chokepoints call these for in-memory AppUser instances. Inside EF IQueryable
/// filters use the raw enum comparison instead (these methods don't translate to SQL).
/// </summary>
public static class UserCapabilities
{
    public static bool CanBeAssignedShift(this AppUser u) => u.AccountType != AccountType.GroupUser;
    public static bool CanDoChores(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool CanBeAssignedAnything(this AppUser u) => u.AccountType != AccountType.GroupUser;
    public static bool CanAccessRequests(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool CanBeVacationApprover(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool IsVisibleOnOverview(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool IsVisibleOnShiftsCalendar(this AppUser u) => u.AccountType != AccountType.GroupUser;
    public static bool IsVisibleOnChoresCalendar(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool IsVisibleInAnalytics(this AppUser u) => u.AccountType != AccountType.GroupUser;
}
```

- [ ] **Step 4: Run — verify pass**

```bash
dotnet test --filter "FullyQualifiedName~UserCapabilitiesTests" -- xUnit.ParallelizeTestCollections=false
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Models/Support/UserCapabilities.cs ShiftManager.Tests/UnitTests/Models/UserCapabilitiesTests.cs
git commit -m "feat(accounts): UserCapabilities central gate + full matrix unit tests"
```

---

## PHASE A2 — Enforcement chokepoints

> For each chokepoint task: write a behavior test that builds users of each AccountType, exercises the surface, and asserts visibility/eligibility; then add the one-line query predicate (raw enum comparison). Run sequentially; commit per task.

### Task 3: Overview roster excludes mil + groupuser

**Files:** Modify `Pages/Calendar/Overview.cshtml.cs` (LoadUsersAsync ~195–209). Test: `ShiftManager.Tests/.../OverviewRosterAccountTypeTests.cs`.

- [ ] **Step 1: Write failing test** — seed 3 users (Standard, Mil, GroupUser) in one company; call the roster-loading path (or the query method); assert only the Standard user is returned.
```csharp
// Arrange 3 active users same company, AccountType Standard/Mil/GroupUser, DoesShifts=true.
// Act: invoke the Overview user-loading query.
// Assert: returned set contains the Standard user, excludes Mil and GroupUser.
result.Select(u => u.AccountType).Should().OnlyContain(t => t == AccountType.Standard);
```
- [ ] **Step 2: Run — fails** (all 3 returned today).
- [ ] **Step 3: Implement** — in the WHERE clause add `&& u.AccountType == AccountType.Standard`:
```csharp
// before: .Where(u => u.CompanyId == CompanyId && u.IsActive)
.Where(u => u.CompanyId == CompanyId && u.IsActive && u.AccountType == AccountType.Standard)
```
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `fix(overview): exclude mil + groupuser from the overview roster`.

### Task 4: Shifts roster excludes groupuser (mil stays)

**Files:** Modify `Services/ShiftCalendarService.cs:36` (GetUsersForCalendarAsync). Test: `ShiftCalendarServiceAccountTypeTests.cs`.

- [ ] **Step 1: Failing test** — 3 users in a molecule's company; assert Standard + Mil returned, GroupUser excluded.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — add `&& u.AccountType != AccountType.GroupUser` to the `.Where(u => u.IsActive && ...)`.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `fix(shifts-calendar): exclude groupuser from shift roster (mil retained)`.

### Task 5: Chores roster excludes mil + groupuser

**Files:** Modify `Pages/Calendar/Chores.cshtml.cs:349` (GetUsersForMoleculeAsync). Test: `ChoresRosterAccountTypeTests.cs`.

- [ ] **Step 1: Failing test** — assert only Standard returned.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — add `&& u.AccountType == AccountType.Standard` to the WHERE.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `fix(chores-calendar): only Standard accounts in chore roster`.

### Task 6: Requests lockout (mil + groupuser blocked from entire surface)

**Files:** Modify `Pages/My/Requests.cshtml.cs` (OnGetAsync + OnPostTimeOffAsync ~182), `Pages/Requests/Index.cshtml.cs` (OnGetAsync), nav partial (hide link). New resx key `Requests_Locked_AccountType`. Test: `RequestsLockoutTests.cs`.

- [ ] **Step 1: Failing test** — a Mil user and a GroupUser hitting `OnPostTimeOffAsync` get a non-success (Forbid/redirect) result and NO TimeOffRequest is created; a Standard user still succeeds.
- [ ] **Step 2: Run — fails** (mil/groupuser currently allowed).
- [ ] **Step 3: Implement** — at the top of `OnGetAsync` and `OnPostTimeOffAsync` in `My/Requests.cshtml.cs`, and `OnGetAsync` in `Requests/Index.cshtml.cs`, load the current user and guard:
```csharp
var me = await _db.Users.FindAsync(currentUserId);
if (me is null || !me.CanAccessRequests())
{
    // OnGet: friendly redirect with a localized message
    TempData["Error"] = _localizer["Requests_Locked_AccountType"].Value;
    return RedirectToPage("/Index");
    // OnPost: return Forbid();
}
```
Add resx key `Requests_Locked_AccountType` (EN: "This account type cannot use the Requests page." / HE: "סוג חשבון זה אינו יכול להשתמש בעמוד הבקשות."). Hide the Requests nav entry where the layout renders it for the current user when `!CanAccessRequests()`.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `fix(requests): lock mil + groupuser out of the entire Requests surface`.

### Task 7: Approver pool excludes mil + groupuser

**Files:** Modify `Services/VacationApprovalService.cs:1508` (GetGrantBasedApproverOptionsAsync). Test: extend `VacationApproverOptionsTests`.

- [ ] **Step 1: Failing test** — a Mil/GroupUser holding ApproveVacations grant must NOT appear in another user's approver options.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — add `&& u.AccountType == AccountType.Standard` to the final approver `.Where(...)`.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `fix(vacation): exclude mil + groupuser from approver options`.

### Task 8: BusyService assignment hard-blocks

**Files:** Modify `Services/BusyService.cs` (ValidateChoreAsync ~190 block, ValidateShiftAsync ~317 block). New resx keys `Error_GroupUserCannotBeAssigned`, `Error_AccountCannotDoChores`. Test: `BusyServiceAccountTypeTests.cs`.

- [ ] **Step 1: Failing test** — validating a GroupUser for a shift yields an Error issue; validating a Mil OR GroupUser for a chore yields an Error issue; a Standard user yields none of these.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — after the user-loaded/active checks:
  - In `ValidateShiftAsync` (the ~305 block): 
    ```csharp
    if (user is not null && !user.CanBeAssignedShift())
        errors.Add(new ValidationIssue("GROUPUSER_CANNOT_BE_ASSIGNED",
            _localizer["Error_GroupUserCannotBeAssigned"].Value,
            ValidationSeverity.Error, ValidationCategory.JobType));
    ```
  - In `ValidateChoreAsync` (the ~183 block):
    ```csharp
    if (user is not null && !user.CanDoChores())
        errors.Add(new ValidationIssue("ACCOUNT_CANNOT_DO_CHORES",
            _localizer["Error_AccountCannotDoChores"].Value,
            ValidationSeverity.Error, ValidationCategory.JobType));
    ```
  (Use the existing local `user`/`targetUser` variable name in each method; if there's no `IStringLocalizer` field, use the existing error-message pattern in that file. Use an existing `ValidationCategory` value — `JobType` if present, else the closest.) Add the two resx keys to both files (EN/HE).
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `fix(assignment): hard-block groupuser (shift) and mil/groupuser (chore) in BusyService`.

---

## PHASE A3 — Admin UI + cleanup + seed

### Task 9: Admin/Users inline AccountType editor + handler + badge

**Files:** Modify `Pages/Admin/Users.cshtml` (new editable cell + badge), `Pages/Admin/Users.cshtml.cs` (OnPostAccountTypeAsync, expose options + AccountTypeName). New resx: `Users_AccountType`, `AccountType_Standard`, `AccountType_Mil`, `AccountType_GroupUser`. Test: `UsersAccountTypeHandlerTests.cs`.

- [ ] **Step 1: Failing test** — `OnPostAccountTypeAsync(userId, (int)AccountType.Mil)` by an admin with EditCompanyUsers persists `AccountType=Mil`; a caller lacking the grant is rejected and no change persists.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement handler** in `Users.cshtml.cs`, mirroring `OnPostJobTypeAsync` (existence check → EditCompanyUsers grant check for target's company → set `u.AccountType = (AccountType)accountType` → `SaveWithConcurrencyHandlingAsync` → audit `LogUserActionAsync` → `RedirectToPage()`):
```csharp
public async Task<IActionResult> OnPostAccountTypeAsync(int id, int accountType)
{
    var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
    if (u is null) return NotFound();
    if (!await _grantService.HasGrantAsync(CurrentUserId, "EditCompanyUsers", ScopeForCompany(u.CompanyId)))
        return Forbid();
    if (!Enum.IsDefined(typeof(AccountType), accountType)) { TempData["Error"] = "Invalid account type"; return RedirectToPage(); }
    u.AccountType = (AccountType)accountType;
    await _concurrencyService.SaveWithConcurrencyHandlingAsync(_db);
    await _auditLogService.LogUserActionAsync(/* same args shape as OnPostJobTypeAsync */);
    TempData["Success"] = _localizer["Saved"].Value;
    return RedirectToPage();
}
```
(Match the exact grant-check + audit call shape used by `OnPostJobTypeAsync` in this file — copy its surrounding pattern, swapping JobTypeId→AccountType. Task 10 will extend this handler with cleanup; keep the method name stable.)
- [ ] **Step 4: Implement view** — add an editable cell mirroring the JobType cell (`Pages/Admin/Users.cshtml` ~959), `asp-page-handler="AccountType"`, a `<select name="accountType">` with the 3 options (localized), and a badge next to the name shown when `u.AccountType != AccountType.Standard`. Expose `AccountTypeName(u)` helper and the option list on the model. Add resx keys (EN/HE).
- [ ] **Step 5: Run handler test — passes.**
- [ ] **Step 6: Restart + browser-verify** the editor renders and persists in light/dark/RTL (curl-marker discipline). Commit `feat(admin/users): inline AccountType editor + badge + handler`.

### Task 10: Type-change assignment cleanup

**Files:** Modify `Pages/Admin/Users.cshtml.cs` (extend `OnPostAccountTypeAsync`). Test: `UsersAccountTypeCleanupTests.cs`.

- [ ] **Step 1: Failing test** — a user with a FUTURE shift + FUTURE chore: changing to Mil removes the future chore but keeps the future shift; changing to GroupUser removes both future shift and future chore; PAST assignments are untouched.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — after persisting the new AccountType in `OnPostAccountTypeAsync`, call a private helper that reuses the SAME assignment-removal logic the user-deactivation handler uses (locate it in this file — the deactivation path that nulls/removes active shift/chore assignments). Remove FUTURE (date >= today) assignments the new type can't hold:
  - → Mil: remove future chore assignments.
  - → GroupUser: remove future shift, chore, and on-call assignments.
  Write an audit entry per cleanup. Do NOT hand-roll new deletion SQL — call the existing removal helper(s).
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `feat(admin/users): clean up disallowed future assignments on account-type change`.

### Task 11: Seed mil + groupuser QA accounts

**Files:** Modify `Data/SeedData/QaTestUserSeed.cs`. Test: `SeedAccountTypesTests.cs` (optional — assert the two accounts exist with the right AccountType).

- [ ] **Step 1: Implement** — add (mirroring existing `AddUser` calls, ~line 230): a Mil account `mil.tz@test` ("Mil TZ", Tzafona/Alhut, role BRDirector/kabar, then set `AccountType=Mil`, `DoesShifts=true`) and a GroupUser `groupuser.tz@test` (DisplayName "groupusertzafona", Tzafona, role BRDirector, `AccountType=GroupUser`). If `AddUser` has no AccountType param, set it on the produced def/user after creation (follow how `DoesShifts` is set in seed).
- [ ] **Step 2: Build + run the seed path test** (or start the app and confirm both users exist). 
- [ ] **Step 3: Commit** `test(seed): add mil.tz@test + groupuser.tz@test QA accounts`.

---

## PHASE B — Analytics

### Task 12: Lead-and-above gate (remove ViewJusticeTable from Assigner)

**Files:** Modify `Data/SeedData/RoleTemplateSeed.cs` (~1019 `Grant(8, 133, ...)`), `ShiftManager.Tests/MasterTests/GrantAuthorization/RoleTemplateAutoGrantTests.cs` (InlineData Assigner 26→25; comment lines 184–185 drop "Assigner"). Follow `grant_change_checklist.md` (this is an assignment removal, NOT a grant-type removal → GrantTypeSeed and count-comments do NOT change).

- [ ] **Step 1: Update the test first** — change `[InlineData("Tzafona", "Assigner", 26)]` to `25`, and edit the comments at lines 184–185 to remove "Assigner" from the ViewJusticeTable holder list. Run:
```bash
dotnet test --filter "FullyQualifiedName~RoleTemplateAutoGrantTests" -- xUnit.ParallelizeTestCollections=false
```
Expected: FAILS (seed still grants 133 to Assigner → count is 26).
- [ ] **Step 2: Remove the grant assignment** — delete the `grants.Add(G(8, 133, ...));` line for ViewJusticeTable on the Assigner template (template id 8) in `RoleTemplateSeed.cs`.
- [ ] **Step 3: Run — passes** (Assigner now 25).
- [ ] **Step 4: Update MEMORY** — note ViewJusticeTable no longer on Assigner (lead+ only). (FinalProductPublish seed is regenerated by `scripts/Update-FinalProductPublish.ps1`, not hand-edited — flag as a deploy step, do not edit it here.)
- [ ] **Step 5: Browser-verify** — log in as `assigner.oren@test` / Test1234! and confirm `/Admin/Analytics` now returns 403/Access Denied; `owner2@test` still has access. (Requires re-seed or back-fill; if the running DB already seeded Assigner with 133, run the app's "Back-fill role template grants" owner action or re-seed the dev DB.)
- [ ] **Step 6: Commit** `fix(analytics): restrict ViewJusticeTable to lead-and-above (remove from Assigner)`.

### Task 13: Exclude groupuser + DoesShifts shift-metric filter in JusticeService

**Files:** Modify `Services/JusticeService.cs` (BuildUsersInCompanyAsync ~200, BuildUsersInMoleculeAsync ~440, and the headcount used for per-user expected). Test: `JusticeServiceAccountTypeAndDoesShiftsTests.cs`.

- [ ] **Step 1: Failing test** — in a company with a Standard (DoesShifts=true), a Mil (DoesShifts=true), a GroupUser, and a Standard-with-DoesShifts=false:
  - WorkType=Shift rows: include Standard + Mil; exclude GroupUser AND the DoesShifts=false user.
  - WorkType=Chore rows: include Standard, Mil(? mil does no chores but is visible in analytics — include in chore rows only if it has chore data; per spec mil IS visible in analytics, so include mil; exclude GroupUser; INCLUDE the DoesShifts=false user) — assert GroupUser always excluded; DoesShifts=false user excluded ONLY for Shift/All.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — in both builders' user enumeration:
```csharp
query = query.Where(u => u.AccountType != AccountType.GroupUser);   // groupuser never in analytics
if (q.WorkType == JusticeWorkType.Shift || q.WorkType == JusticeWorkType.All)
    query = query.Where(u => u.DoesShifts);                          // don't skew shift fairness
```
Apply the SAME conditional to any headcount/denominator used in per-user expected/capacity so numerator and denominator cover the same population.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** `feat(analytics): exclude groupuser; drop DoesShifts=off from shift-metric rows`.

### Task 14: ShiftCategory breakdown filter

**Files:** Modify `Services/JusticeViewModels.cs` (JusticeQuery + `int? ShiftCategoryId`), `Services/JusticeService.cs` (CountActualPerUserAsync ~512, SumShiftCapacityPerCompanyAsync ~595, user enumeration), `Pages/Admin/Analytics.cshtml.cs` (bound `ShiftCategoryId` + category options), `Pages/Admin/Analytics.cshtml` (dropdown). New resx `Justice_Filter_Category`, `Justice_Filter_AllCategories`. Test: `JusticeServiceCategoryTests.cs`.

- [ ] **Step 1: Failing test** — with two categories (Yekev, Hazon) and a user in Yekev only: setting `ShiftCategoryId = Yekev` returns only Yekev-shift counts and only DoesShifts users with a Yekev `UserShiftCategory` membership; a Hazon-only user is excluded.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — add `int? ShiftCategoryId` to `JusticeQuery`. When set:
  - shift count/capacity queries: `.Where(a => a.ShiftInstance.ShiftType.CategoryId == q.ShiftCategoryId)`.
  - user enumeration: restrict to `u.DoesShifts && _db.UserShiftCategories.Any(m => m.UserId == u.Id && m.ShiftCategoryId == q.ShiftCategoryId)`.
  - Bind `ShiftCategoryId` on the page; populate the dropdown from the molecule's active `ShiftCategory` rows (only when a molecule scope is resolved — guard otherwise). Add resx keys.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Restart + browser-verify** the dropdown filters to a category with populated data (level=MoleculesInArea / molecule scope, wide date range). Commit `feat(analytics): ShiftCategory breakdown filter (DoesShifts members of a category)`.

### Task 15: Chart tooltips + descriptive captions

**Files:** Modify `Pages/Admin/Analytics.cshtml` (donut ~519, gauge ~325, ribbon ~840). New resx `Justice_Tooltip_*`, `Justice_Caption_Donut`, `Justice_Caption_Gauge`, `Justice_Help_SpreadIndex`. Browser-verify only (no unit test for static markup).

- [ ] **Step 1: Implement** — add `<title>` child elements to the donut/gauge/ribbon `<circle>`/segment elements (segment name + value + deviation), a one-line `<p class="chart-caption">` under each chart, and a "?" help affordance with a `title` on the SpreadIndex explaining the 0–1 scale. All strings via new bilingual resx keys (grep each key first).
- [ ] **Step 2: Restart + curl marker + browser-verify** tooltips appear on hover and captions read correctly in light/dark/RTL with populated data.
- [ ] **Step 3: Commit** `feat(analytics): chart tooltips + descriptive captions (clarity)`.

---

## PHASE C — Verification

### Task 16: Full suite + cross-surface browser sweep

- [ ] **Step 1: Stop app, build, run full suite sequentially.**
```bash
powershell -Command "Get-Process -Name ShiftManager -ErrorAction SilentlyContinue | Stop-Process -Force"
dotnet build
dotnet test -- xUnit.ParallelizeTestCollections=false
```
Expected: all pass (new tests + the prior baseline). Report counts; investigate any failure touching our files.
- [ ] **Step 2: Restart app; browser sweep (owner2@test, plus mil.tz@test / groupuser.tz@test / assigner.oren@test):**
  - mil.tz@test: appears in `/Calendar/Shifts` + `/Admin/Analytics`; ABSENT from `/Calendar/Overview` + chores; `/My/Requests` blocked; not in another user's approver list.
  - groupuser.tz@test: ABSENT from all calendars + analytics; Requests blocked; cannot be assigned a shift (assignment shows the hard error).
  - assigner.oren@test: `/Admin/Analytics` denied (403); owner2@test allowed.
  - Analytics: category dropdown filters correctly; DoesShifts=off user absent from shift metrics; tooltips/captions render. Verify with POPULATED data (wide date range).
  - Admin/Users: AccountType editor persists + badge shows, light/dark/RTL.
  Save screenshots under `docs/superpowers/plans/artifacts/ab-final-*`.
- [ ] **Step 3: Confirm scoped git state** — `git log --oneline` shows the A+B commits; `git status` shows only pre-existing unrelated files. 

## Done criteria (maps to checklist)
- [ ] mil: shifts + role kept; no chores; Requests locked; in Analytics + Shifts; hidden from Overview. 
- [ ] groupuser: no shifts/chores/assignment; Requests locked; hidden from all calendars + analytics; role kept.
- [ ] Analytics: lead+ only; DoesShifts=off excluded from shift metrics; category breakdown; tooltips/captions.
- [ ] AccountType set via /Admin/Users; type-change cleans disallowed future assignments.
- [ ] Build clean; full suite green sequentially; bilingual resx for all new strings.
