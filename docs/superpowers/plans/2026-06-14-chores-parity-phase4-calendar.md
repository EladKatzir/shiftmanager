# Chores↔ShiftType Parity — Phase 4 (Calendar) Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (- [ ]) syntax.
**Goal:** Bring the Chores by-user calendar to parity with the Shifts by-user calendar: (1) change the roster predicate so chore participation is `DoesChores`/category-membership driven (D9), (2) replace the flat ChoreType row-grouping with **category-accordion + mirrored rows** cloned from the shift calendar's `BuildCategoryGroupedRowsAsync`, and (3) surface **eligibility reasons** (gender/officer/exempt) in the assignment picker so a blocked or warned candidate renders greyed with a localized reason chip, fed by Phase 2's `IChoreService.GetEligibilityForCandidateAsync`.
**Architecture:** The Chores calendar (`Pages/Calendar/Chores.cshtml.cs`) builds an `ExcelCalendarTableViewModel` (rows + groups) rendered by the group-id-agnostic `ExcelCalendarTable` view component. Phase 4 only touches the **chores page + its picker JS + chore-eligibility endpoint + loc/CSS** — no shift page, no shared component contract changes. Row grouping mirrors the shift pattern by `ChoreCategory` (group id `chorecategory-{id}`) over the Phase-1 `UserChoreCategory` join, with a company-header fallback group. The picker enriches `(user × choreType)` options with an eligibility hint via a new JSON endpoint that calls the Phase-2 pure helper; a shared `renderEligibleCandidate(c)` helper (new file `wwwroot/js/eligibility-chip.js`) renders the greyed row + `.elig-chip` and is consumed by both the chore bottom-sheet and the justice drawer.
**Tech Stack:** ASP.NET Core 8.0 Razor Pages + vanilla JS, EF Core (SQLite), bilingual he/en + RTL.
**Depends on:** Phase 1 (entities: `ChoreCategory`, `UserChoreCategory`, `AppUser.DoesChores`, `AppUser.ChoreCategories`, `ChoreType.ChoreCategoryId`) + Phase 2 (`IChoreCategoryService`, `IChoreService.GetEligibilityForCandidateAsync`, `EligibilityResult`/`EligibilityViolation`). **Spec:** docs/superpowers/specs/2026-06-14-chores-shifttype-parity-design.md
---

## Phase 2 contract consumed (verbatim — namespace `ShiftManager.Services`)
```csharp
public enum EligibilityViolation { RequiresGender = 0, RequiresOfficerRank = 1, Exempt = 2 }
public sealed record EligibilityResult(IReadOnlyList<EligibilityViolation> Violations)
{
    public bool IsEligible => Violations.Count == 0;
    public static readonly EligibilityResult Eligible;
}
// On IChoreService:
Task<EligibilityResult> GetEligibilityForCandidateAsync(int userId, int choreTypeId);
// On IChoreCategoryService (clone of IShiftCategoryService — Phase 2):
Task<List<ChoreCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false);
Task<List<int>> GetUserCategoryIdsAsync(int userId);
```
Severity mapping the picker applies (spec §5): `RequiresOfficerRank` + `Exempt` → **hard block** (greyed, non-selectable); `RequiresGender` → **warning** (chip shown, still selectable — manager overrides on assign). The picker is an at-a-glance hint; the authoritative gate stays `BusyService.ValidateChoreAsync`.

---

## File Structure
```
Pages/
  Calendar/
    Chores.cshtml.cs        [MODIFY] GetUsersForMoleculeAsync (D9 predicate); BuildUserBasedCalendarAsync
                                     (replace ChoreType grouping → category-grouped mirrored rows via new
                                     BuildChoreCategoryGroupedRowsAsync); + IChoreCategoryService injection.
    Chores.cshtml           [MODIFY] (none structural) — picker JS + loc already wired via _LocalizationScript.
  Api/Calendar/
    GetChoreEligibilityForCandidate.cshtml(.cs)  [NEW] JSON endpoint → IChoreService.GetEligibilityForCandidateAsync
                                                       returns {userId, choreTypeId, isHardBlocked, hardReasons[], warnings[]}.
Pages/Shared/
  _LocalizationScript.cshtml  [MODIFY] add Elig_* reason loc keys (he+en already in resx).
Resources/
  SharedResources.resx        [MODIFY] Elig_RequiresGender / Elig_RequiresOfficerRank / Elig_Exempt / Elig_Blocked (en).
  SharedResources.he-IL.resx  [MODIFY] same keys (he).
wwwroot/js/
  eligibility-chip.js         [NEW] window.EligibilityChip.render(c) shared helper (greyed row + .elig-chip).
  calendar-bottom-sheet.js    [MODIFY] decorate chore-type/user options with eligibility via new endpoint + helper.
  justice-panel.js            [MODIFY] candidateRowHtml delegates chip rendering to EligibilityChip (optional, DRY).
wwwroot/css/
  components.css              [MODIFY] .elig-chip (bg+text+!important; dark override = color only).
ShiftManager.Tests/UnitTests/Pages/
  ChoresRosterAccountTypeTests.cs        [MODIFY] extend D9 cases (DoesChores / category-member / neither).
  ChoresCategoryGroupingTests.cs          [NEW] mirrored-row-per-membership + company fallback grouping.
Pages/Shared/Components/ExcelCalendarTable/
  Default.cshtml, _CalendarRow.cshtml     [VERIFY ONLY] group-id agnostic — confirm chorecategory-{id} renders.
```
> Phase 1/2 artifacts (`Models/ChoreCategory.cs`, `Models/UserChoreCategory.cs`, `Services/IChoreCategoryService.cs`, `GetEligibilityForCandidateAsync`) **must already exist** before this phase runs. Confirm in Task 0.

---

## Task 0 — Preflight: verify Phase 1 + Phase 2 are landed
**Files:** none (read-only verification).

- [ ] **Step 1** Confirm the Phase-1 entities exist and expose the members this plan calls:
  ```bash
  ls Models/ChoreCategory.cs Models/UserChoreCategory.cs
  grep -n "DoesChores" Models/AppUser.cs
  grep -n "ChoreCategories" Models/AppUser.cs
  grep -n "ChoreCategoryId" Models/ChoreType.cs
  ```
  Expect: `ChoreCategory` with `Id, MoleculeId, Name, DisplayName, Color, SortOrder, IsActive`; `UserChoreCategory { UserId, ChoreCategoryId }`; `AppUser.DoesChores` (bool), `AppUser.ChoreCategories` (List<UserChoreCategory>); `ChoreType.ChoreCategoryId`.
- [ ] **Step 2** Confirm the Phase-2 service contract:
  ```bash
  grep -n "GetEligibilityForCandidateAsync" Services/IChoreService.cs
  grep -n "record EligibilityResult\|enum EligibilityViolation" Services/*.cs
  grep -n "GetCategoriesForMoleculeAsync\|GetUserCategoryIdsAsync" Services/IChoreCategoryService.cs
  grep -n "AppDbContext.*UserChoreCategories\|DbSet<UserChoreCategory>" Data/AppDbContext.cs
  ```
  Expect all present. **If any is missing, STOP** — Phase 4 depends on it; do not stub. Report the gap to the user.
- [ ] **Step 3** Confirm `IChoreCategoryService` is registered in DI:
  ```bash
  grep -n "IChoreCategoryService" Program.cs
  ```
  Expect a `services.AddScoped<IChoreCategoryService, ChoreCategoryService>()` line (added in Phase 2). If absent, add it (mirror the `IShiftCategoryService` registration) and note it.

**Verification:** all greps return matches. **No commit** (read-only gate).

---

## Task 1 — Roster predicate (D9) in `GetUsersForMoleculeAsync`
**Files:**
- `Pages/Calendar/Chores.cshtml.cs` (`GetUsersForMoleculeAsync`, lines 340–361)
- `ShiftManager.Tests/UnitTests/Pages/ChoresRosterAccountTypeTests.cs` (extend)

The current predicate is `AccountType == Standard && IsActive && companyIds.Contains(CompanyId)`. D9 adds `&& (u.DoesChores || u.ChoreCategories.Any())`. Phase-1 backfill set `DoesChores=true` for all existing active Standard users, so the live roster is preserved; the new clause only ever *removes* a Standard user who has been explicitly opted out (DoesChores off AND no category).

- [ ] **Step 1 (TDD — write the failing test first).** Extend `ChoresRosterAccountTypeTests.cs`. The existing `SeedAsync` (lines 53–83) seeds 3 users (Standard/Mil/GroupUser) all with `DoesChores` defaulting to whatever Phase-1 sets — make the new test self-contained by seeding `DoesChores` and a category membership explicitly. Add a focused test:
  ```csharp
  [Fact]
  public async Task GetUsersForMoleculeAsync_D9_IncludesDoesChoresAndCategoryMembers_ExcludesNeitherAndNonStandard()
  {
      // Hierarchy
      var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
      _db.Areas.Add(area);
      _db.Molecules.Add(new Molecule { Id = MolId, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce });
      _db.Companies.Add(new Company { Id = 1, MoleculeId = MolId, Name = "Co", DisplayName = "Co" });
      await _db.SaveChangesAsync();

      var category = new ChoreCategory { Id = 1, MoleculeId = MolId, Name = "Physical", DisplayName = "Physical", IsActive = true };
      _db.ChoreCategories.Add(category);
      await _db.SaveChangesAsync();

      _db.Users.AddRange(
          // (A) Standard + DoesChores → INCLUDED
          new AppUser { Id = 1, Email = "does@test.com", DisplayName = "DoesChores", CompanyId = 1, IsActive = true,
                        AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = true },
          // (B) Standard + NOT DoesChores but category member → INCLUDED
          new AppUser { Id = 2, Email = "cat@test.com", DisplayName = "CategoryMember", CompanyId = 1, IsActive = true,
                        AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = false },
          // (C) Standard + neither → EXCLUDED
          new AppUser { Id = 3, Email = "neither@test.com", DisplayName = "Neither", CompanyId = 1, IsActive = true,
                        AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = false },
          // (D) Mil + DoesChores → EXCLUDED (account-type gate dominates)
          new AppUser { Id = 4, Email = "mil@test.com", DisplayName = "Mil", CompanyId = 1, IsActive = true,
                        AccountType = AccountType.Mil, Role = UserRole.Employee, DoesChores = true },
          // (E) GroupUser + category member → EXCLUDED
          new AppUser { Id = 5, Email = "grp@test.com", DisplayName = "GroupUser", CompanyId = 1, IsActive = true,
                        AccountType = AccountType.GroupUser, Role = UserRole.Employee, DoesChores = false });
      await _db.SaveChangesAsync();
      _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 2, ChoreCategoryId = 1 }); // makes (B) a member
      _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 5, ChoreCategoryId = 1 }); // (E) member but GroupUser
      await _db.SaveChangesAsync();

      var model = BuildModel();
      var users = await model.GetUsersForMoleculeAsync(MolId);

      users.Select(u => u.DisplayName).Should().BeEquivalentTo(new[] { "DoesChores", "CategoryMember" },
          "D9 includes Standard DoesChores + Standard category-members, excludes Standard-with-neither and all Mil/GroupUser");
  }
  ```
  Add `using ShiftManager.Models;` for `ChoreCategory`/`UserChoreCategory` if not already imported (the file already imports `ShiftManager.Models` and `ShiftManager.Models.Support`).
- [ ] **Step 2 (run the test — it must FAIL).** Because production still returns all active Standard users, the seeded "Neither" user (C) leaks in → assertion fails. Confirm red:
  ```bash
  dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ChoresRosterAccountTypeTests.GetUsersForMoleculeAsync_D9" -- xUnit.ParallelizeTestCollections=false
  ```
- [ ] **Step 3 (make it pass).** Edit `GetUsersForMoleculeAsync` (lines 349–360). Replace the `.Where(...)` predicate. Because the projection currently builds a fresh `AppUser` with only `Id/DisplayName/CompanyId`, the `ChoreCategories.Any()` clause must be evaluated **in the query** (before projection), which it is. New body:
  ```csharp
  // Get active Standard-account users who participate in chores (D9): Mil + GroupUser excluded;
  // a Standard user appears only if they opted-in via DoesChores OR belong to ≥1 ChoreCategory.
  // Phase-1 backfill set DoesChores=true for existing active Standard users, so the live roster is preserved.
  return await _db.Users
      .IgnoreQueryFilters()
      .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive
               && u.AccountType == ShiftManager.Models.Support.AccountType.Standard
               && (u.DoesChores || u.ChoreCategories.Any()))
      .OrderBy(u => u.DisplayName)
      .Select(u => new AppUser
      {
          Id = u.Id,
          DisplayName = u.DisplayName,
          CompanyId = u.CompanyId
      })
      .ToListAsync();
  ```
  > Note: `u.ChoreCategories.Any()` translates to an EXISTS subquery against `UserChoreCategories` — confirm the `AppUser.ChoreCategories` nav targets `UserChoreCategory` (Phase 1). If the nav is not mapped, replace with `_db.UserChoreCategories.Any(m => m.UserId == u.Id)` (same SQL, no nav dependency).
- [ ] **Step 4 (re-run — green).** The existing `GetUsersForMoleculeAsync_OnlyReturnsStandardAccounts` test (lines 119–134) seeds only a single Standard user **without** `DoesChores`. After D9 that user no longer passes → the legacy test would break. **Update its `SeedAsync` (line 67-ish) to set `DoesChores = true` on the Standard user** so the test still asserts the account-type gate (its actual purpose). Re-run the whole class:
  ```bash
  dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ChoresRosterAccountTypeTests" -- xUnit.ParallelizeTestCollections=false
  ```
  Expect all green.
- [ ] **Step 5** Commit.
  ```bash
  git add Pages/Calendar/Chores.cshtml.cs ShiftManager.Tests/UnitTests/Pages/ChoresRosterAccountTypeTests.cs
  git commit -m "feat(chores-calendar): D9 roster predicate — DoesChores/category participation gate"
  ```

**Verification:** new D9 test green; legacy account-type test green after the `DoesChores=true` seed fix.

---

## Task 2 — Category-grouped mirrored rows in `BuildUserBasedCalendarAsync`
**Files:**
- `Pages/Calendar/Chores.cshtml.cs` (constructor + injection; `BuildUserBasedCalendarAsync` lines 243–338; new `BuildChoreCategoryGroupedRowsAsync` cloned from `Shifts.cshtml.cs:796–896`)
- `ShiftManager.Tests/UnitTests/Pages/ChoresCategoryGroupingTests.cs` (new)

Replace the flat ChoreType groups (lines 300–306) + the single user-row loop (lines 308–321) with a category-accordion that renders **a mirrored row per `UserChoreCategory` membership** plus a **company-header fallback** for users with no category — exactly mirroring `BuildCategoryGroupedRowsAsync`. The chores page has no `WeeklyHours`/`SubLabel`/`HomeTypeId` notion to carry, so the clone is simpler than the shift version. Group id is `chorecategory-{id}` (spec §7.5); fallback id `company-{id}`.

- [ ] **Step 1** Inject `IChoreCategoryService` into `ChoresModel`. Add the field + ctor parameter (after `_choreTypeService`, line 27 / 39):
  ```csharp
  private readonly IChoreCategoryService _choreCategoryService;
  ```
  ctor signature add `IChoreCategoryService choreCategoryService,` and body `_choreCategoryService = choreCategoryService;`. Update **both** the production ctor (lines 36–58) and the test `BuildModel()` (ChoresRosterAccountTypeTests.cs:91–101 and the new ChoresCategoryGroupingTests) to pass `Mock.Of<IChoreCategoryService>()` / a real instance.
- [ ] **Step 2** Add the cloned grouping method to `ChoresModel`. It consumes the already-built per-cell data via the existing `BuildCellsForUser(...)` (lines 425–528) so chore cells/overlays/home/text/notes are unchanged.
  ```csharp
  /// <summary>
  /// Category-accordion grouping for the by-user Chores calendar (parity with the shift by-user
  /// calendar's BuildCategoryGroupedRowsAsync). A chore participant renders as a MIRRORED row under
  /// EACH ChoreCategory they belong to (UserChoreCategory); a participant with no category — and any
  /// non-DoesChores user surfaced via membership-only — falls under their Company header so nobody is lost.
  /// Group id = "chorecategory-{id}" (spec §7.5); fallback "company-{id}". Duplicate data-row-id across
  /// mirrored rows is safe (the calendar resolves interactions from the cell's data-row-id, not a DOM id).
  /// </summary>
  private async Task<(List<ExcelCalendarRow> rows, List<ExcelCalendarGroup> groups)> BuildChoreCategoryGroupedRowsAsync(
      List<AppUser> users,
      int moleculeId,
      List<Chore> chores,
      Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
      Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
      Dictionary<(int UserId, DateOnly Date), string> overviewNotes,
      Dictionary<(int UserId, DateOnly Date), List<HomeShiftItem>> homeShifts,
      bool isHebrew)
  {
      var rows = new List<ExcelCalendarRow>();
      var groups = new List<ExcelCalendarGroup>();

      // Company names: per-row badge for cross-company category members + company-group headers.
      // SECURITY-AUDITED: SAFE — scoped to the molecule-derived user set's CompanyIds.
      var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
      var companyLookup = await _db.Companies
          .IgnoreQueryFilters()
          .Where(c => companyIds.Contains(c.Id))
          .ToDictionaryAsync(c => c.Id, c => c.LocalizedName);

      // Categories (active, ordered) + memberships restricted to the participants in this view.
      var categories = await _choreCategoryService.GetCategoriesForMoleculeAsync(moleculeId);
      var participantIds = users.Select(u => u.Id).ToHashSet();
      var byCategory = new Dictionary<int, HashSet<int>>();
      if (participantIds.Count > 0)
      {
          var memberships = await _db.UserChoreCategories
              .Where(m => participantIds.Contains(m.UserId))
              .Select(m => new { m.ChoreCategoryId, m.UserId })
              .ToListAsync();
          byCategory = memberships
              .GroupBy(m => m.ChoreCategoryId)
              .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToHashSet());
      }

      var placedParticipants = new HashSet<int>();
      int sortOrder = 0;

      ExcelCalendarRow BuildRow(AppUser user, string groupId, string? companyName)
      {
          var row = new ExcelCalendarRow
          {
              Id = $"user-{user.Id}",
              Label = user.DisplayName,
              GroupId = groupId,
              CompanyName = companyName
          };
          row.Cells = BuildCellsForUser(user.Id, chores, overlays, textEntries, overviewNotes, homeShifts, isHebrew);
          return row;
      }

      // Category accordions in service order; the same participant can appear under several (mirrored rows).
      foreach (var cat in categories)
      {
          if (!byCategory.TryGetValue(cat.Id, out var memberIdSet))
              continue;
          var members = users.Where(u => memberIdSet.Contains(u.Id)).OrderBy(u => u.DisplayName).ToList();
          if (members.Count == 0)
              continue;

          var groupId = $"chorecategory-{cat.Id}";
          groups.Add(new ExcelCalendarGroup
          {
              Id = groupId,
              Name = cat.DisplayName,
              SortOrder = sortOrder++,
              Color = cat.Color,         // rendered as a dot/accent on the header, never a text background (spec §7.7)
              MemberCount = members.Count
          });
          foreach (var user in members)
          {
              placedParticipants.Add(user.Id);
              rows.Add(BuildRow(user, groupId, companyLookup.GetValueOrDefault(user.CompanyId)));
          }
      }

      // Company groups: any participant not mapped to a category (so nobody is lost).
      var companyGrouped = users
          .Where(u => !placedParticipants.Contains(u.Id))
          .GroupBy(u => u.CompanyId)
          .OrderBy(g => companyLookup.GetValueOrDefault(g.Key, ""));
      foreach (var grp in companyGrouped)
      {
          var groupId = $"company-{grp.Key}";
          var members = grp.OrderBy(u => u.DisplayName).ToList();
          groups.Add(new ExcelCalendarGroup
          {
              Id = groupId,
              Name = companyLookup.GetValueOrDefault(grp.Key, $"Company #{grp.Key}"),
              SortOrder = sortOrder++,
              MemberCount = members.Count
          });
          // Company badge is redundant inside a company group — suppress it.
          foreach (var user in members)
              rows.Add(BuildRow(user, groupId, null));
      }

      return (rows, groups);
  }
  ```
- [ ] **Step 3** Rewire `BuildUserBasedCalendarAsync`. **Delete** lines 299–321 (the `// Build groups by chore type` block + the user-row `foreach`) and replace with a call to the new method. Keep everything above (users/chores/overlays/home/text/notes loads) and below (the `CalendarData = new ...` assignment) intact. New replacement:
  ```csharp
  var isHebrew = CultureInfo.CurrentUICulture.Name.StartsWith("he", StringComparison.Ordinal);

  // Category-accordion + mirrored rows (parity with the shift by-user calendar). Replaces the
  // legacy flat ChoreType grouping. Group id = "chorecategory-{id}"; company-{id} fallback.
  var (rows, groups) = await BuildChoreCategoryGroupedRowsAsync(
      users, moleculeId, chores, overlays, textEntries, overviewNotes, homeShifts, isHebrew);
  ```
  Then the existing `CalendarData = new ExcelCalendarTableViewModel { ... Rows = rows, Groups = groups.Any() ? groups : null, ... }` (lines 323–333) consumes them unchanged. **`TotalRows` (line 336) already computes `Rows.Count + (Groups?.Count ?? 0) + 1`** — correct for mirrored rows because each mirrored row is a real element of `rows`.
  > The `ChoreTypeFilter` (lines 264–268) still applies to `chores` *before* grouping, so the type dropdown keeps narrowing visible chore chips; it no longer drives row grouping. Leave it.
  > `LocalizeChoreTypeName` (lines 530–531) is still used inside `BuildCellsForUser` for the chip label — keep it. Only the **group** name source changed (ChoreType → ChoreCategory.DisplayName).
- [ ] **Step 4 (TDD).** New `ShiftManager.Tests/UnitTests/Pages/ChoresCategoryGroupingTests.cs`, real SQLite, mirroring the roster test harness. Because `BuildChoreCategoryGroupedRowsAsync` is `private`, test it through `BuildUserBasedCalendarAsync` via a thin `internal` seam OR expose the grouping method as `internal`. **Choose `internal`** (consistent with `GetUsersForMoleculeAsync` being internal; `InternalsVisibleTo` is already configured per the roster test's class comment). Mark `BuildChoreCategoryGroupedRowsAsync` `internal async Task<...>`. Test:
  ```csharp
  [Fact]
  public async Task BuildChoreCategoryGroupedRows_MirrorsUserPerMembership_AndFallsBackToCompany()
  {
      // Seed: molecule M, company C. Categories Physical(1), Computer(2). User A in BOTH; user B in none.
      // Expect: groups [chorecategory-1, chorecategory-2, company-C]; A appears twice (one row per category),
      //         B appears once under company-C; A is NOT in the company fallback.
      ...
      var (rows, groups) = await model.BuildChoreCategoryGroupedRowsAsync(
          users, MolId, chores: new List<Chore>(), overlays, textEntries, overviewNotes, homeShifts, isHebrew: false);

      groups.Select(g => g.Id).Should().ContainInOrder("chorecategory-1", "chorecategory-2", "company-1");
      rows.Count(r => r.Id == "user-1").Should().Be(2, "user A is mirrored under both categories");
      rows.Where(r => r.GroupId == "chorecategory-1").Select(r => r.Id).Should().Contain("user-1");
      rows.Where(r => r.GroupId == "chorecategory-2").Select(r => r.Id).Should().Contain("user-1");
      rows.Where(r => r.GroupId == "company-1").Select(r => r.Id).Should().BeEquivalentTo(new[] { "user-2" });
      rows.Where(r => r.GroupId == "company-1").Select(r => r.Id).Should().NotContain("user-1");
      groups.Single(g => g.Id == "chorecategory-1").Color.Should().Be("#A1B2C3"); // category color carried to header
  }
  ```
  Seed empty dictionaries for `overlays`/`textEntries`/`overviewNotes`/`homeShifts` (the helper only indexes into them). Pass `chores: new List<Chore>()` so `BuildCellsForUser` produces empty cells. Build `users` via the same projected-AppUser shape the page uses (`Id`, `DisplayName`, `CompanyId`). Wire `_choreCategoryService` to a **real** `ChoreCategoryService(_db)` instance (not a mock) so `GetCategoriesForMoleculeAsync` reads the seeded categories.
- [ ] **Step 5 (run).** Red first (method doesn't exist / still flat), then green after Steps 1–3:
  ```bash
  dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ChoresCategoryGroupingTests" -- xUnit.ParallelizeTestCollections=false
  ```
- [ ] **Step 6 (browser-verify the markup on POPULATED data).** Start the app, log in (`@test` users, pwd `Test1234!`), open the chores calendar with a molecule that has ≥1 category + members:
  ```
  http://localhost:5000/Calendar/Chores?moleculeId=<id>&viewMode=week
  ```
  Confirm: category accordion headers render with the category name + count + color dot; a user who belongs to two categories appears under both; uncategorized chore participants appear under a company header; collapse/expand + Alt-arrow reorder still work (group-id agnostic). **Verify in light, dark, and Hebrew RTL** that the category color is a dot/accent on the header and never a text background (spec §7.7). Note: dev app serves stale static assets until restart — this task is server-rendered Razor so a fresh page load suffices, but restart the process to be safe.
- [ ] **Step 7** Commit.
  ```bash
  git add Pages/Calendar/Chores.cshtml.cs ShiftManager.Tests/UnitTests/Pages/ChoresCategoryGroupingTests.cs
  git commit -m "feat(chores-calendar): category-accordion mirrored rows (clone of shift BuildCategoryGroupedRowsAsync)"
  ```

**Verification:** grouping test green; browser shows mirrored category rows + company fallback in all three modes.

---

## Task 3 — Eligibility reasons in the assignment picker
**Files:**
- `Pages/Api/Calendar/GetChoreEligibilityForCandidate.cshtml` + `.cshtml.cs` (new)
- `wwwroot/js/eligibility-chip.js` (new shared helper)
- `wwwroot/js/calendar-bottom-sheet.js` (modify chore-assign flow, lines 364–500 / 705–729)
- `wwwroot/js/justice-panel.js` (modify `candidateRowHtml`, lines 395–424 — optional DRY)
- `Program.cs` (register the new page for anonymous-internal API bypass if required)

The chore picker is **inverted** from the shift picker: the row IS the assignee (`user-{id}`); the picker chooses a **chore type** + title for that user. Eligibility therefore depends on `(selectedUserId × selectedChoreTypeId)`. We surface the hint on **chore-type-dropdown change** and **user-select change**: when both are chosen, call the endpoint and render the chip via the shared helper next to the chore-type field; a hard block disables the Assign button and greys the chip.

- [ ] **Step 1 — new JSON endpoint.** Create `Pages/Api/Calendar/GetChoreEligibilityForCandidate.cshtml`:
  ```cshtml
  @page
  @model ShiftManager.Pages.Api.Calendar.GetChoreEligibilityForCandidateModel
  ```
  And `GetChoreEligibilityForCandidate.cshtml.cs` (mirror auth of `GetEligibleUsersForChore.cshtml.cs`):
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.AspNetCore.Mvc.RazorPages;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.Localization;
  using ShiftManager.Data;
  using ShiftManager.Resources;
  using ShiftManager.Services;
  using System.Security.Claims;

  namespace ShiftManager.Pages.Api.Calendar;

  /// <summary>
  /// At-a-glance eligibility hint for a (user × choreType) pair, for the assignment picker.
  /// Returns hard-block reasons (officer/exempt) and warnings (gender) as localized strings.
  /// This is a DISPLAY hint only — the authoritative gate is BusyService.ValidateChoreAsync.
  /// Mirrors the auth/scope pattern of GetEligibleUsersForChore.
  /// </summary>
  [Authorize(Policy = "Grant:AssignChores")]
  [IgnoreAntiforgeryToken]
  public class GetChoreEligibilityForCandidateModel : PageModel
  {
      private readonly IChoreService _choreService;
      private readonly IScopeFilterService _scopeFilterService;
      private readonly AppDbContext _db;
      private readonly IStringLocalizer<SharedResources> _localizer;
      private readonly ILogger<GetChoreEligibilityForCandidateModel> _logger;

      public GetChoreEligibilityForCandidateModel(
          IChoreService choreService,
          IScopeFilterService scopeFilterService,
          AppDbContext db,
          IStringLocalizer<SharedResources> localizer,
          ILogger<GetChoreEligibilityForCandidateModel> logger)
      {
          _choreService = choreService;
          _scopeFilterService = scopeFilterService;
          _db = db;
          _localizer = localizer;
          _logger = logger;
      }

      public async Task<IActionResult> OnGetAsync([FromQuery] int moleculeId, [FromQuery] int userId, [FromQuery] int choreTypeId)
      {
          try
          {
              var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
              if (string.IsNullOrEmpty(idClaim) || !int.TryParse(idClaim, out var currentUserId))
                  return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };
              if (moleculeId <= 0 || userId <= 0 || choreTypeId <= 0)
                  return new JsonResult(new { success = false, message = "Invalid parameters" }) { StatusCode = 400 };

              // Scope check mirrors GetEligibleUsersForChore: caller must have chores access to this molecule.
              var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", moleculeId, "chores");
              if (!hasAccess)
              {
                  var caller = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                  var callerCompany = caller != null ? await _db.Companies.FindAsync(caller.CompanyId) : null;
                  if (callerCompany?.MoleculeId != moleculeId)
                  {
                      _logger.LogWarning("SECURITY: User {UserId} probed chore eligibility for molecule {MoleculeId} outside scope", currentUserId, moleculeId);
                      return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                  }
              }

              // Defense-in-depth: the target user AND the chore type must both belong to this molecule.
              var userInMolecule = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId
                  && _db.Companies.IgnoreQueryFilters().Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));
              var typeInMolecule = await _db.ChoreTypes.AnyAsync(t => t.Id == choreTypeId && t.MoleculeId == moleculeId);
              if (!userInMolecule || !typeInMolecule)
                  return new JsonResult(new { success = false, message = "Out of scope" }) { StatusCode = 400 };

              var result = await _choreService.GetEligibilityForCandidateAsync(userId, choreTypeId);

              // Severity mapping (spec §5): officer + exempt are HARD; gender is a WARNING.
              var hardReasons = new List<string>();
              var warnings = new List<string>();
              foreach (var v in result.Violations)
              {
                  switch (v)
                  {
                      case EligibilityViolation.RequiresOfficerRank:
                          hardReasons.Add(_localizer["Elig_RequiresOfficerRank"].Value); break;
                      case EligibilityViolation.Exempt:
                          hardReasons.Add(_localizer["Elig_Exempt"].Value); break;
                      case EligibilityViolation.RequiresGender:
                          warnings.Add(_localizer["Elig_RequiresGender"].Value); break;
                  }
              }

              return new JsonResult(new
              {
                  success = true,
                  userId,
                  choreTypeId,
                  isHardBlocked = hardReasons.Count > 0,
                  hardReasons,
                  warnings
              });
          }
          catch (Exception ex) when (ex is not OperationCanceledException)
          {
              _logger.LogError(ex, "Error in GetChoreEligibilityForCandidate");
              return new JsonResult(new { success = false, message = "Internal error" }) { StatusCode = 500 };
          }
      }
  }
  ```
- [ ] **Step 2 — register the endpoint.** The path is under `/Api/Calendar/` which is already whitelisted in `ApiAuthenticationMiddleware.IsInternalWebUiEndpoint` (the `/Api/Calendar` entry covers the whole sub-tree — confirm `GetEligibleUsersForChore` works without its own entry; if it has one, mirror it). Confirm with:
  ```bash
  grep -n "GetEligibleUsersForChore\|/Api/Calendar" Program.cs Middleware/ApiAuthenticationMiddleware.cs
  ```
  Add an `AllowAnonymousToPage`/whitelist entry **only if** the existing `GetEligibleUsersForChore` page has one. Mirror exactly. (Per MEMORY: new anonymous-internal endpoints need BOTH `Program.cs` AND the middleware — but `Grant:AssignChores` here means it is NOT anonymous; it rides cookie auth like its sibling, which has neither.) Verify the sibling is reachable to decide; do not add dead entries.
- [ ] **Step 3 — shared chip helper.** Create `wwwroot/js/eligibility-chip.js` (IIFE, attaches `window.EligibilityChip`). It renders a single greyed/warned candidate row + a `.elig-chip` reason chip, consumed by both pickers:
  ```javascript
  // Shared eligibility-reason chip renderer. Consumed by calendar-bottom-sheet.js (chore picker)
  // and justice-panel.js (justice drawer). Pure render — no fetch, no DOM mutation outside the returned string.
  (function () {
      'use strict';

      function loc(key, fallback) {
          if (window.AppLocalizer && typeof window.AppLocalizer[key] === 'string') return window.AppLocalizer[key];
          return fallback || key;
      }
      function esc(s) {
          return String(s == null ? '' : s)
              .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
              .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
      }

      // c: { hardReasons: string[], warnings: string[] }
      // Returns an HTML string of zero or more <span class="elig-chip ..."> chips (already-localized text).
      function renderChips(c) {
          var html = '';
          (c.hardReasons || []).forEach(function (r) {
              html += '<span class="elig-chip elig-chip--block" title="' + esc(r) + '">⊘ ' + esc(r) + '</span>';
          });
          (c.warnings || []).forEach(function (r) {
              html += '<span class="elig-chip elig-chip--warn" title="' + esc(r) + '">⚠ ' + esc(r) + '</span>';
          });
          return html;
      }

      // For native <option> contexts (bottom-sheet user/type <select>): prefix glyph + disable on hard block.
      // Returns { prefix: string, disabled: bool, title: string }.
      function decorateOption(c) {
          var blocked = (c.hardReasons || []).length > 0;
          var warn = (c.warnings || []).length > 0;
          var glyph = blocked ? '⊘ ' : (warn ? '⚠ ' : '');
          var title = ((c.hardReasons || []).concat(c.warnings || [])).join(', ');
          return { prefix: glyph, disabled: blocked, title: title };
      }

      window.EligibilityChip = { renderChips: renderChips, decorateOption: decorateOption, loc: loc };
  })();
  ```
  Register it in the layout's script bundle next to `calendar-bottom-sheet.js` (load `eligibility-chip.js` **before** it). Find the include:
  ```bash
  grep -rn "calendar-bottom-sheet.js" Pages/Shared/_Layout.cshtml Pages/Calendar/Chores.cshtml
  ```
  Add `<script src="~/js/eligibility-chip.js" asp-append-version="true"></script>` immediately before the bottom-sheet include on whatever layout/page loads it for the chores calendar.
- [ ] **Step 4 — wire the chore picker.** In `calendar-bottom-sheet.js`, inside the chore-fields block (after the chore-type dropdown is created, lines 395–408), add an eligibility hint element + change handlers. Insert after line 407 (`ctFieldGroup.appendChild(choreTypeDropdown);`) and before `addSection.appendChild(ctFieldGroup);`:
  ```javascript
  // Eligibility hint (chore parity Phase 4): when both a chore type and a user are chosen, fetch the
  // (user × type) eligibility and render reason chips. Hard block (officer/exempt) disables Assign; a
  // gender warning shows a chip but stays selectable (manager overrides at assign time).
  var eligHint = document.createElement('div');
  eligHint.className = 'bottom-sheet__elig-hint';
  eligHint.id = 'bottom-sheet-elig-hint';
  ctFieldGroup.appendChild(eligHint);

  var refreshElig = function () {
      var cfg = window.CalendarPageConfig;
      var typeId = choreTypeDropdown ? parseInt(choreTypeDropdown.value, 10) : NaN;
      var uId = userSelect ? parseInt(userSelect.value, 10) : NaN;   // userSelect is created lower; closure-safe
      eligHint.innerHTML = '';
      if (assignBtn) assignBtn.disabled = false;
      if (!cfg || !(cfg.moleculeId > 0) || isNaN(typeId) || typeId <= 0 || isNaN(uId) || uId <= 0) return;
      fetch('/Api/Calendar/GetChoreEligibilityForCandidate?moleculeId=' + cfg.moleculeId +
            '&userId=' + uId + '&choreTypeId=' + typeId, { credentials: 'same-origin' })
          .then(function (r) { return r.ok ? r.json() : null; })
          .then(function (data) {
              if (!data || !data.success) return;
              eligHint.innerHTML = window.EligibilityChip.renderChips(data);
              if (assignBtn && data.isHardBlocked) assignBtn.disabled = true;
          })
          .catch(function (err) { console.warn('Eligibility hint failed:', err); });
  };
  choreTypeDropdown.addEventListener('change', refreshElig);
  ```
  Then, after `userSelect` is created and appended (line 484) and `assignBtn` is created (lines 489–500), attach `userSelect.addEventListener('change', refreshElig);` and call `refreshElig();` once to seed the hint for the row's default user. **Ordering note (CLAUDE.md §1 gate): `refreshElig` references `userSelect` and `assignBtn`, which are declared LOWER in the same function scope.** Because `refreshElig` is only *invoked* after those `var`s execute (via the change events / the seed call placed after `assignBtn` exists), the closure resolves them correctly — but the seed `refreshElig()` call MUST be placed after `assignBtn` is appended (after line 501), not inside the chore-fields block. Place the seed call right before `actionsEl.appendChild(cancelBtn)` (line 510).
  > Do NOT block the existing busy-decoration path (`decorateOptionsWithBusyAsync`) — eligibility is additive. The two are independent: busy = scheduling conflicts; eligibility = gender/rank/exempt.
- [ ] **Step 5 — DRY the justice drawer (optional, low-risk).** In `justice-panel.js` `candidateRowHtml` (lines 395–424), the existing hard-block/warn glyph logic already renders `c.hardBlockReason` / `c.warnings` as title attributes. For visible chips (not just tooltips), append `window.EligibilityChip.renderChips({ hardReasons: c.hardBlockReason ? [c.hardBlockReason] : [], warnings: c.warnings || [] })` into the `justice-candidate__meta` span. Guard with `if (window.EligibilityChip)` so the drawer still works if the helper failed to load. **This is a visual enhancement of an existing surface — keep the existing `--blocked`/`--warn` status glyphs; just add the chip text.** If it risks regressing the drawer's tested markup, defer it (flag explicitly) and keep chips bottom-sheet-only.
- [ ] **Step 6 — loc keys.** Add to `_LocalizationScript.cshtml` (after the Justice Panel block, line 316) so the JS `loc()`/`renderChips` can resolve them (they arrive pre-localized from the endpoint, but the JS fallback + any client-only label needs them):
  ```cshtml
  // Chore eligibility chips (eligibility-chip.js / calendar-bottom-sheet.js) — Phase 4
  "Elig_RequiresGender": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["Elig_RequiresGender"].Value)),
  "Elig_RequiresOfficerRank": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["Elig_RequiresOfficerRank"].Value)),
  "Elig_Exempt": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["Elig_Exempt"].Value)),
  "Elig_Blocked": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["Elig_Blocked"].Value))
  ```
  (Add a comma to the preceding last entry, line 316, before appending.)
- [ ] **Step 7 — browser-verify the picker.** Restart the app (JS changes are NOT served until process restart — see MEMORY `dev_app_stale_static_assets`). `curl http://localhost:5000/js/eligibility-chip.js` and confirm your marker is served before browser-testing. Then on `/Calendar/Chores?moleculeId=<id>`, open a cell's bottom sheet, pick a chore type that has an officer-rank rule with an enlisted user selected → expect a red ⊘ "officers only" chip + disabled Assign. Pick a gender-restricted type with a mismatched user → expect a ⚠ warning chip + Assign STILL enabled. **Verify in light/dark/RTL** the chip text is readable (Task 4 CSS).
- [ ] **Step 8** Commit.
  ```bash
  git add Pages/Api/Calendar/GetChoreEligibilityForCandidate.cshtml Pages/Api/Calendar/GetChoreEligibilityForCandidate.cshtml.cs \
          wwwroot/js/eligibility-chip.js wwwroot/js/calendar-bottom-sheet.js wwwroot/js/justice-panel.js \
          Pages/Shared/_LocalizationScript.cshtml
  git commit -m "feat(chores-calendar): eligibility reason chips in the chore assignment picker"
  ```

**Verification:** endpoint returns correct hard/warn split; picker greys + disables on hard block, shows warn chip but stays selectable on gender; helper shared, loaded before the bottom sheet.

---

## Task 4 — Localization + contrast/RTL for the reason chip
**Files:**
- `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` (add 4 keys)
- `wwwroot/css/components.css` (`.elig-chip`)

- [ ] **Step 1 — grep before adding (MEMORY gotcha: a dup key broke 6 loc tests).** Confirm none of the 4 keys exist:
  ```bash
  grep -n "Elig_RequiresGender\|Elig_RequiresOfficerRank\|Elig_Exempt\|Elig_Blocked" Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
  ```
  Expect no matches. If any exists, reuse it instead of adding.
- [ ] **Step 2 — add resx entries.** English (`SharedResources.resx`):
  | key | value |
  |---|---|
  | `Elig_RequiresGender` | `Gender restricted` |
  | `Elig_RequiresOfficerRank` | `Officers only` |
  | `Elig_Exempt` | `Exempt` |
  | `Elig_Blocked` | `Blocked` |

  Hebrew (`SharedResources.he-IL.resx`):
  | key | value |
  |---|---|
  | `Elig_RequiresGender` | `מוגבל מגדר` |
  | `Elig_RequiresOfficerRank` | `קצינים בלבד` |
  | `Elig_Exempt` | `פטור` |
  | `Elig_Blocked` | `חסום` |

  > Route the Hebrew through a `localization-qa` pass (spec §13.8) — these are draft strings; the inspector confirms phrasing/terminology. Flag in Deferred Items.
- [ ] **Step 3 — `.elig-chip` CSS.** Add to `components.css`. Mirror the `.justice-candidate__pill` token discipline (bg + explicit `--primary-contrast`/light text + `!important`); dark-mode override redeclares **only `color`** (per the RTL invariant). **CONTRAST TRAP (verified in tokens.css): `--warning-text` is `#1A1F2B` (dark) in BOTH light and dark themes, and `--warning-soft` is light `#FFF3CD` in light / dark `#3D3520` in dark — so `color: var(--warning-text)` on `--warning-soft` is dark-on-light (OK in light) but dark-on-dark (FAILS in dark).** Therefore set an explicit dark color override. Block:
  ```css
  /* Chore eligibility reason chips (eligibility-chip.js). Sensitive ≠ alarming: muted, bordered.
     Color is paired with bg + !important per the project's text-contrast rule. */
  .elig-chip {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.1rem 0.45rem;
      margin-inline-end: 0.25rem;
      border-radius: 999px;
      font-size: 0.75rem;
      line-height: 1.4;
      border: 1px solid var(--border);
      white-space: nowrap;
  }
  .elig-chip--block {
      background: var(--danger-soft);
      color: var(--danger-text) !important;   /* #FFFFFF in both themes — readable on danger-soft */
      border-color: var(--danger);
  }
  .elig-chip--warn {
      background: var(--warning-soft);
      color: var(--warning-text) !important;   /* #1A1F2B dark text — readable on LIGHT warning-soft */
  }
  /* Dark mode: warning-soft becomes dark (#3D3520); the dark warning-text would be dark-on-dark.
     Redeclare ONLY color (preserves any RTL/structural rules). */
  [data-theme="dark"] .elig-chip--warn,
  @media (prefers-color-scheme: dark) {
      .elig-chip--warn { color: var(--warning, #E0B341) !important; }
  }
  ```
  > Confirm the project's dark-mode selector convention before committing — grep an existing `[data-theme="dark"]` rule and match it exactly (it may be `html[data-theme="dark"]` or a `.theme-dark` class). Use whichever the codebase uses; do not introduce a new selector form.
  ```bash
  grep -rn "data-theme=\"dark\"\|prefers-color-scheme: dark" wwwroot/css/components.css wwwroot/css/tokens.css | head
  ```
- [ ] **Step 4 — browser-verify contrast on POPULATED data (MANDATORY pre-ship, spec §7.7 + MEMORY contrast rules).** With a hard-blocked and a warned candidate both visible in the picker, screenshot the chip in: light mode, dark mode, Hebrew RTL. Confirm: block chip = white text on red-soft; warn chip = readable in BOTH themes (dark text on light warning-soft in light; light amber text on dark warning-soft in dark); chip sits inline without overlapping the chore-type dropdown; RTL flips the `margin-inline-end` correctly. **The #1 recurring bug in this project is dark-on-dark — do not skip the dark-mode check.**
- [ ] **Step 5** Commit.
  ```bash
  git add Resources/SharedResources.resx Resources/SharedResources.he-IL.resx wwwroot/css/components.css
  git commit -m "feat(chores-calendar): elig-chip loc keys + contrast/RTL-safe styling"
  ```

**Verification:** 4 keys present in both resx (no dups); chip readable in light/dark/RTL on populated data.

---

## Task 5 — Full suite + regression sweep
**Files:** none (verification).

- [ ] **Step 1** Run the full suite **sequentially** (parallel `:memory:` SQLite contention produces ~221 spurious failures — MEMORY):
  ```bash
  dotnet test ShiftManager.Tests -- xUnit.ParallelizeTestCollections=false
  ```
  Confirm the new D9 + grouping tests pass and no existing Chores/Justice/Calendar/localization test regressed. Pay special attention to any test that constructs `ChoresModel` directly (the new `IChoreCategoryService` ctor param must be threaded into every such test).
- [ ] **Step 2** Grep for other `new ChoresModel(` call sites broken by the ctor change:
  ```bash
  grep -rn "new ChoresModel(" ShiftManager.Tests Pages
  ```
  Update each to pass the new arg.
- [ ] **Step 3** Final build with the app stopped (CLAUDE.md §3 — locked-exe gate): if the dev app is running on :5000, **ask the user to stop it** (or `taskkill` the verified process) before rebuilding; never rebuild against a locked binary.
- [ ] **Step 4** Commit any test-wiring fixups from Step 2.
  ```bash
  git add -A && git commit -m "test(chores-calendar): thread IChoreCategoryService through ChoresModel test ctors"
  ```

**Verification:** full sequential suite green; no `new ChoresModel(` call site left un-updated.

---

## Self-Review

**Spec coverage (§7.5 + §D9 + §13):**
- D9 roster predicate — Task 1 (`AccountType==Standard && IsActive && companyIds.Contains(CompanyId) && (DoesChores || ChoreCategories.Any())`), test mirrors `ChoresRosterAccountTypeTests` with the four required cases (DoesChores Standard ✓, category-member Standard ✓, Standard-with-neither ✗, Mil/GroupUser ✗). Covered.
- Category-grouped mirrored rows — Task 2 clones `BuildCategoryGroupedRowsAsync` → `BuildChoreCategoryGroupedRowsAsync`, group id `chorecategory-{id}`, mirrored row per `UserChoreCategory`, company-`{id}` fallback. `TotalRows` arithmetic confirmed correct for mirrored rows. Component is group-id agnostic (verified: `_CalendarRow.cshtml` emits `data-group-id="@(row.GroupId ?? "")"` and `Default.cshtml` filters `r.GroupId == group.Id`, no hard-coded prefix). Covered.
- Eligibility reasons in picker — Task 3: new endpoint calls `GetEligibilityForCandidateAsync`; severity split (officer/exempt hard, gender warn) per spec §5; shared `EligibilityChip` helper used by both the bottom sheet and (optionally) the justice drawer; blocked → greyed + disabled, warned → chip + still selectable. Covered.
- Localization + contrast/RTL — Task 4: 4 loc keys (grepped first), `.elig-chip` pairs colored bg + explicit light/dark text + `!important`, dark override redeclares only color, category color is a header dot/accent (never a text background), browser-verified on POPULATED data in light/dark/RTL. Covered.

**Type consistency vs the Phase 2 contract:**
- `GetEligibilityForCandidateAsync(int userId, int choreTypeId)` → `Task<EligibilityResult>` — called exactly with those args/return in the endpoint. ✓
- `EligibilityViolation` enum values `RequiresGender`/`RequiresOfficerRank`/`Exempt` — matched in the `switch`. ✓
- `EligibilityResult.Violations` (`IReadOnlyList<EligibilityViolation>`) — iterated, not `.IsEligible` shortcut (we need the per-violation kind to split severity). ✓
- `IChoreCategoryService.GetCategoriesForMoleculeAsync` returns `List<ChoreCategory>` exposing `Id/DisplayName/Color` — used for group headers. ✓ (Mirrors `IShiftCategoryService` exactly; confirmed against `ShiftCategoryService.cs`.)
- No invented members: `ChoreCategory.Color`, `UserChoreCategory.UserId/ChoreCategoryId`, `AppUser.DoesChores`, `AppUser.ChoreCategories`, `ChoreType.ChoreCategoryId/MoleculeId`, `Company.LocalizedName` — all from Phase 1 spec / verified existing (`LocalizedName` used identically in `BuildCategoryGroupedRowsAsync`). Task 0 hard-gates their existence.

**Placeholder scan:** No `TODO`/`...`/`<placeholder>` in any code block. Every method body, test body, endpoint, JS helper, CSS block, and resx table is complete. The only `...` is in the grouping test seed (Step 4, Task 2) explicitly marked as the seed scaffold to fill from the sibling roster test's `SeedAsync` — flagged inline, not load-bearing logic.

**Cross-file visibility gate (CLAUDE.md §1):** `refreshElig` in `calendar-bottom-sheet.js` references `userSelect`/`assignBtn` declared lower in the same function scope — Step 4 explicitly orders the seed invocation AFTER those `var`s execute (closure-safe), and the change-event invocations fire only post-render. `window.EligibilityChip` is loaded before `calendar-bottom-sheet.js` (Step 3) and guarded with `if (window.EligibilityChip)` in the justice drawer. No assumed cross-file globals.

**Did we do everything correctly?** The plan is internally consistent, every code reference is grounded in a file read during planning, the TDD order (red→green) is explicit for both server-side tasks, and the two pure-markup/JS tasks carry browser-verification with the exact URL + the restart-for-static-assets caveat. Remaining uncertainty is intentionally surfaced in Deferred Items.

## Deferred Items
- **Hebrew string QA** — the 4 `Elig_*` Hebrew values (Task 4 Step 2) are draft translations; spec §13.8 routes new resx keys through a `localization-qa` pass. DEFERRED to that pass — flag to the user to run the `localization-qa-inspector` after this phase. Not a blocker for the calendar to function.
- **Justice-drawer chip DRY (Task 3 Step 5)** — appending visible `.elig-chip`s to `justice-panel.js candidateRowHtml` is marked OPTIONAL: if it risks regressing the drawer's tested markup, keep chips bottom-sheet-only and the drawer keeps its existing title-attribute reasons. DEFERRED-conditional — the worker decides at implementation time based on whether justice-panel tests exist; if deferred, the shared helper is still created and used by the bottom sheet (the spec's "shared helper" requirement is satisfied).
- **`ChoreTypeFilter` semantics under category grouping** — the type dropdown still filters visible chips but no longer drives grouping (Task 2 Step 3 note). This is intentional per spec (grouping axis moved from type to category), but the dropdown label may now be slightly ambiguous to users. NOT changed here; flag for a possible UX follow-up if the filter feels disconnected from the new accordion. Ask the user if a category filter should replace/augment the type filter (out of Phase 4 scope).
- **Endpoint anonymous-bypass registration (Task 3 Step 2)** — left conditional on whether the sibling `GetEligibleUsersForChore` carries a `Program.cs`/middleware entry. The worker must verify at implementation time and mirror exactly; do not add dead entries. Not a deferral of work, a verification gate.
