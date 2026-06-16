# Category-Based Shift Eligibility (3b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the assignable-user candidate list (bottom-sheet `<select>` and quick-entry autocomplete) driven by `DoesShifts` + `ShiftCategory` membership instead of the legacy job-type filter, behind a default-off per-company feature flag, after backfilling `ShiftType.CategoryId`.

**Architecture:** A new idempotent migration backfills `ShiftType.CategoryId` from the categories the original `ShiftCategoryBackfillSql` synthesized (deterministic `"<DisplayName> [<id>]"` reverse-map). The two existing leaf eligibility methods (workforce `ShiftAssignmentService`, tech `ShiftCalendarService`) each gain a `categoryFilter` param — flag-free, unit-tested in isolation. A new `ShiftCandidateService` router reads the company-level flag, dispatches to the correct leaf method, applies the null-category fallback rules, and returns a uniform `{reason, users[{id,name,companyName}]}` projection. The generalized `GetEligibleUsersForShift` endpoint calls the router (authz unchanged). Both JS consumers wire to it; quick-entry gets the fetch-on-focus/cache/states/a11y/disambiguation overhaul.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core + SQLite, xUnit + FluentAssertions, vanilla JS (no JS unit harness — JS verified via server contract tests + browser sweep).

---

## Design decisions locked for this plan (read before coding)

**Canonical eligibility expression** (copy the shape from `Services/JusticeService.cs:214-218`):
```
u.IsActive && u.AccountType != GroupUser && <DoesShifts participant> &&
    (catId == null ? true : UserShiftCategories.Any(m => m.UserId == u.Id && m.ShiftCategoryId == catId))
```
Composed **inside** the grouping-derived (workforce) or `EligibleCompanyIds` (tech) company set. Officer-rank (`Rank >= 9`) preserved for tech.

**A user may belong to MANY categories** (`UserShiftCategory` is N:N — "Yekev AND Hazon"). A `ShiftType` belongs to **at most one** category (`ShiftType.CategoryId`, nullable). The `.Any(...)` membership test already returns a multi-category user for any shift whose single category they're in. **A Phase-2 test locks this in.**

**`DoesShifts` participant resolution (Decision 3):** within the candidate company set, a user is a participant **iff** they have a `CompanyMembership` row (in any candidate company) with `DoesShifts=true`, **OR** they have no `CompanyMembership` row in any candidate company AND their mirrored `AppUser.DoesShifts=true`. (A membership row with `DoesShifts=false` does NOT fall back to the mirror.) Materialize the participant set once and `Contains(...)`.

**Endpoint `reason` field & null-CategoryId fork (RESOLVED with the owner 2026-06-16):**

| Situation | `reason` | `users` returned |
|---|---|---|
| `CategoryId != null`, members exist | `category` | the category members (filtered) |
| `CategoryId != null`, zero members | `category` | `[]` (structural zero — "no eligible users") |
| `CategoryId == null`, **shared** (Key `HOME`/`OFFLINE`, or `JobTypeId == null`) | `sharedFallback` | all participants in molecule (render normally) |
| `CategoryId == null`, **assignable** (has `JobTypeId`, not HOME/OFFLINE), `allowFallback=false` | `noCategory` | `[]` (structural zero — "no category set, ask admin") + **escape-hatch button** |
| `CategoryId == null`, **assignable**, `allowFallback=true` | `sharedFallback` | all participants in molecule (the button promotes it to the shared path) |
| feature flag OFF (any of the above) | `category` | **legacy** set (behavior-preserving; jobType for workforce, EligibleCompanyIds+rank for tech) |

The escape-hatch button ("Show all shift workers") re-fetches with `allowFallback=true`. Never-primary types that legitimately stayed null but have a `JobTypeId` will surface as `noCategory` — that's acceptable: the admin badge nudges categorization, and the button unblocks the assigner.

**Flag is read in the router (the "service router"), not the leaf methods.** Company-level: `IsEnabledAsync(Flags.CategoryBasedShiftEligibility, userId: null, companyId: currentCompanyId)`. Default OFF.

**Molecule kind dispatch:** `molecule.Type == MoleculeType.Tech` → tech leaf; otherwise → workforce leaf.

**Test command (ALWAYS sequential):**
```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false
```
Single-test filter form used in steps below:
```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~<TestClass>" -- xUnit.ParallelizeTestCollections=false
```
**Build-lock rule:** if a build fails with `ShiftManager.exe ... used by another process`:
```
Get-Process -Name ShiftManager | Where-Object { $_.Path -like '*Downloads\ShiftManager*' } | Stop-Process -Force
```

---

## File Structure

**Create:**
- `Migrations/ShiftTypeCategoryBackfillSql.cs` — shared backfill SQL constants (mirrors `ShiftCategoryBackfillSql.cs`).
- `Migrations/<timestamp>_BackfillShiftTypeCategoryId.cs` — the migration (generated via `dotnet ef`, body replaced).
- `Services/IShiftCandidateService.cs` — router interface + `EligibleCandidateDto` / `EligibleCandidatesResult` records.
- `Services/ShiftCandidateService.cs` — router implementation.
- `ShiftManager.Tests/UnitTests/Migrations/ShiftTypeCategoryBackfillTests.cs`
- `ShiftManager.Tests/UnitTests/Services/ShiftCandidateServiceTests.cs`
- `ShiftManager.Tests/UnitTests/Services/CategoryEligibilityLeafTests.cs` (workforce + tech leaf-method tests)

**Modify:**
- `Services/ShiftAssignmentService.cs` — `GetEligibleUsersForShiftTypeAsync` gains `bool categoryFilter = false`.
- `Services/IShiftAssignmentService.cs` — signature update.
- `Services/ShiftCalendarService.cs` — tech `GetEligibleUsersForShiftTypeAsync` gains `bool categoryFilter = false`.
- `Services/IShiftCalendarService.cs` — signature update.
- `Data/SeedData/FeatureFlagSeed.cs` — add `CategoryBasedShiftEligibility` flag constant + `FDisabled` seed.
- `Program.cs` — register `IShiftCandidateService`.
- `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs` — call router; extend projection + `reason`; add `allowFallback`.
- `wwwroot/js/calendar-bottom-sheet.js` — generalize eligible-fetch to all molecule kinds; companyName suffix; reason rows + escape-hatch button.
- `wwwroot/js/calendar-quick-entry.js` — fetch-on-focus, two-tier cache, states, a11y, Enter buffering, no silent fallback, disambiguation line.
- `Pages/Owner/Blueprints.cshtml` — "No category" badge.
- `Resources/SharedResources.resx` + `SharedResources.he-IL.resx` — new loc keys.
- Docs + `MEMORY.md` + topic memory file (Phase 7).

---

## Task 1: Backfill `ShiftType.CategoryId` (Phase 1 — the blocker)

**Files:**
- Create: `Migrations/ShiftTypeCategoryBackfillSql.cs`
- Create: `ShiftManager.Tests/UnitTests/Migrations/ShiftTypeCategoryBackfillTests.cs`
- Create: `Migrations/<timestamp>_BackfillShiftTypeCategoryId.cs` (via `dotnet ef`)

- [ ] **Step 1: Write the shared SQL constants class**

Create `Migrations/ShiftTypeCategoryBackfillSql.cs`:

```csharp
namespace ShiftManager.Migrations;

/// <summary>
/// Idempotent backfill that stamps <c>ShiftType.CategoryId</c> from the categories the original
/// <see cref="ShiftCategoryBackfillSql"/> synthesized. That backfill created one ShiftCategory per
/// distinct *primary* shift type, stamping its Name as "&lt;DisplayName&gt; [&lt;sourceShiftTypeId&gt;]" —
/// a deterministic, collision-free tag. We reverse that EXACT tag to point each shift type at its own
/// category, WITHOUT inventing new categories.
///
/// Safeguards:
///   * Only NULL CategoryId rows are touched (idempotent; never clobbers an admin's manual choice).
///   * Molecule resolved via COALESCE(st.MoleculeId, company.MoleculeId), matching the forward SQL.
///   * The join uses the IDENTICAL Name expression the forward backfill used, so a type maps to its
///     own category and nothing else. Types that were never anyone's primary (incl. shared HOME/
///     OFFLINE) have no matching category and stay NULL — they resolve via the runtime D1 fallback.
/// </summary>
public static class ShiftTypeCategoryBackfillSql
{
    /// <summary>Stamp CategoryId from the "[id]"-tagged category synthesized by the user backfill.</summary>
    public const string StampCategoryId = @"
        UPDATE ShiftTypes
        SET CategoryId = (
            SELECT sc.Id
            FROM ShiftCategories sc
            LEFT JOIN Companies c ON c.Id = ShiftTypes.CompanyId
            WHERE sc.MoleculeId = COALESCE(ShiftTypes.MoleculeId, c.MoleculeId)
              AND sc.Name = COALESCE(NULLIF(TRIM(ShiftTypes.NameEn), ''), ShiftTypes.TechShiftType, ShiftTypes.Key) || ' [' || ShiftTypes.Id || ']'
        )
        WHERE ShiftTypes.CategoryId IS NULL
          AND EXISTS (
            SELECT 1
            FROM ShiftCategories sc
            LEFT JOIN Companies c ON c.Id = ShiftTypes.CompanyId
            WHERE sc.MoleculeId = COALESCE(ShiftTypes.MoleculeId, c.MoleculeId)
              AND sc.Name = COALESCE(NULLIF(TRIM(ShiftTypes.NameEn), ''), ShiftTypes.TechShiftType, ShiftTypes.Key) || ' [' || ShiftTypes.Id || ']'
          );";

    /// <summary>The forward backfill, in order.</summary>
    public static readonly string[] Forward = { StampCategoryId };
}
```

- [ ] **Step 2: Write the failing regression test**

Create `ShiftManager.Tests/UnitTests/Migrations/ShiftTypeCategoryBackfillTests.cs` (mirrors `ShiftCategoryBackfillTests` — real SQLite, production schema, runs the exact shipped SQL):

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Migrations;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

/// <summary>
/// Regression guard for the <c>BackfillShiftTypeCategoryId</c> migration. Runs the EXACT SQL it ships
/// (<see cref="ShiftTypeCategoryBackfillSql.Forward"/>) against real SQLite over a seed that exercises:
///   * a molecule-scoped type WITH a matching "[id]"-tagged category   -> CategoryId stamped,
///   * a company-scoped type WITH a matching category (molecule via company) -> stamped,
///   * a HOME type with no matching category                          -> stays NULL,
///   * an assignable type whose category was never synthesized        -> stays NULL,
///   * idempotency (re-run is a no-op) + a pre-set CategoryId is never clobbered.
/// </summary>
public sealed class ShiftTypeCategoryBackfillTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce });
        _db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });

        _db.ShiftTypes.AddRange(
            // 100: molecule-scoped, NameEn "Hanava" -> category "Hanava [100]" exists
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "HANAVA", NameEn = "Hanava" },
            // 101: company-scoped, NameEn "Morning" -> category "Morning [101]" exists (molecule via Co1)
            new ShiftType { Id = 101, Scope = ShiftScope.Company, CompanyId = 1, Key = "MORNING", NameEn = "Morning" },
            // 102: HOME shared type -> no category -> stays null
            new ShiftType { Id = 102, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "HOME", NameEn = "Home" },
            // 103: assignable type whose category was never synthesized -> stays null
            new ShiftType { Id = 103, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "NIGHT", NameEn = "Night" },
            // 104: a type with a PRE-SET CategoryId (admin choice) -> never clobbered
            new ShiftType { Id = 104, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "EVENING", NameEn = "Evening", CategoryId = 9001 });

        // Categories the user-backfill would have synthesized (note the "[id]" tag).
        _db.ShiftCategories.AddRange(
            new ShiftCategory { Id = 900, MoleculeId = 1, Name = "Hanava [100]", DisplayName = "Hanava" },
            new ShiftCategory { Id = 901, MoleculeId = 1, Name = "Morning [101]", DisplayName = "Morning" },
            new ShiftCategory { Id = 9001, MoleculeId = 1, Name = "Manual Cat", DisplayName = "Manual Cat" });
        await _db.SaveChangesAsync();

        foreach (var sql in ShiftTypeCategoryBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Stamps_CategoryId_For_Types_With_A_Matching_Tagged_Category()
    {
        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[100].CategoryId.Should().Be(900);
        types[101].CategoryId.Should().Be(901, "molecule resolved via Company(1).MoleculeId");
    }

    [Fact]
    public async Task Leaves_Shared_And_NeverPrimary_Types_Null()
    {
        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[102].CategoryId.Should().BeNull("HOME has no synthesized category -> runtime fallback");
        types[103].CategoryId.Should().BeNull("never anyone's primary -> no category -> runtime fallback");
    }

    [Fact]
    public async Task Never_Clobbers_A_PreSet_CategoryId()
    {
        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[104].CategoryId.Should().Be(9001, "WHERE CategoryId IS NULL guards the admin's manual choice");
    }

    [Fact]
    public async Task Is_Idempotent_On_Re_Run()
    {
        foreach (var sql in ShiftTypeCategoryBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);
        _db.ChangeTracker.Clear();

        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[100].CategoryId.Should().Be(900);
        types[101].CategoryId.Should().Be(901);
        types[102].CategoryId.Should().BeNull();
        types[104].CategoryId.Should().Be(9001);
    }
}
```

- [ ] **Step 3: Run the test — verify it PASSES**

The SQL constants already exist (Step 1), so this test should pass immediately (it validates the SQL, not yet the migration wrapper).
```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~ShiftTypeCategoryBackfillTests" -- xUnit.ParallelizeTestCollections=false
```
Expected: 4 passed. If `Stamps_CategoryId_...` fails, the Name expression drifted from `ShiftCategoryBackfillSql.MapUsers` (line 48) — they MUST be byte-identical except for the table alias.

- [ ] **Step 4: Generate the migration shell**

```
dotnet ef migrations add BackfillShiftTypeCategoryId --project ShiftManager.csproj
```
This produces an empty `Up`/`Down` (no model change — `CategoryId` already exists). If the dev app holds the build lock, stop it first (build-lock rule above).

- [ ] **Step 5: Replace the migration body**

Open the generated `Migrations/<timestamp>_BackfillShiftTypeCategoryId.cs` and replace `Up`/`Down`:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: stamp ShiftType.CategoryId from the "[id]"-tagged categories the user
            // backfill synthesized. SQL lives in ShiftTypeCategoryBackfillSql so this migration and its
            // regression test (ShiftTypeCategoryBackfillTests) run identical statements.
            foreach (var sql in ShiftTypeCategoryBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-null only the CategoryIds this backfill could have set (those whose category Name
            // carries the source-type "[id]" tag). Manual assignments to untagged categories are kept.
            migrationBuilder.Sql(@"
                UPDATE ShiftTypes
                SET CategoryId = NULL
                WHERE CategoryId IN (
                    SELECT sc.Id FROM ShiftCategories sc
                    LEFT JOIN Companies c ON c.Id = ShiftTypes.CompanyId
                    WHERE sc.MoleculeId = COALESCE(ShiftTypes.MoleculeId, c.MoleculeId)
                      AND sc.Name = COALESCE(NULLIF(TRIM(ShiftTypes.NameEn), ''), ShiftTypes.TechShiftType, ShiftTypes.Key) || ' [' || ShiftTypes.Id || ']'
                );");
        }
```
Add `using ShiftManager.Migrations;` if the generated namespace differs (it's `ShiftManager.Migrations`, so the constants class is in scope — no using needed).

- [ ] **Step 6: Build + full sequential suite**

```
dotnet build ShiftManager.csproj
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false
```
Expected: green (1758 + 4 new = 1762). If any migration-snapshot test fails, the `dotnet ef` step also touched `AppDbContextModelSnapshot.cs` — that's expected and should be committed.

- [ ] **Step 7: Commit**

```
git add Migrations/ShiftTypeCategoryBackfillSql.cs Migrations/*_BackfillShiftTypeCategoryId.cs Migrations/AppDbContextModelSnapshot.cs ShiftManager.Tests/UnitTests/Migrations/ShiftTypeCategoryBackfillTests.cs
git commit -m "feat(shifts): backfill ShiftType.CategoryId from tagged categories (3b phase 1)"
```

---

## Task 2: Workforce leaf — category-aware filtering (Phase 2)

**Files:**
- Modify: `Services/IShiftAssignmentService.cs` (signature)
- Modify: `Services/ShiftAssignmentService.cs:63-180`
- Create: `ShiftManager.Tests/UnitTests/Services/CategoryEligibilityLeafTests.cs`

- [ ] **Step 1: Write the failing test (workforce category branch + multi-category + per-company DoesShifts)**

Create `ShiftManager.Tests/UnitTests/Services/CategoryEligibilityLeafTests.cs`. Use the project's standard real-SQLite service-test setup (mirror an existing `Services` test's harness for DI of `ShiftAssignmentService`'s constructor — it needs `AppDbContext`, `IStringLocalizer<SharedResources>`, `ILogger`, `IHierarchySettingsService`, `IAuditLogService`, `IConfiguration`, `IAppConfigCacheService`, `IBusyService`; use the existing test fakes/mocks already present in `ShiftManager.Tests` — search for another test that constructs `ShiftAssignmentService` and copy its arrange block).

```csharp
[Fact]
public async Task Workforce_CategoryFilter_Returns_Only_Category_Members_Across_Multiple_Categories()
{
    // Arrange: molecule 1, company 1; shiftType 50 with CategoryId = 500.
    // user A: member of category 500 AND 501 (multi-category), DoesShifts.
    // user B: member of category 501 only, DoesShifts.
    // user C: member of category 500, DoesShifts.
    // Expect categoryFilter:true for shiftType 50 -> {A, C}, NOT B.
    var result = await _svc.GetEligibleUsersForShiftTypeAsync(shiftTypeId: 50, categoryFilter: true);
    result.Select(r => r.UserId).Should().BeEquivalentTo(new[] { /*A*/ 10, /*C*/ 12 });
}

[Fact]
public async Task Workforce_CategoryFilter_NullCategory_Returns_All_Participants()
{
    // shiftType 60 has CategoryId == null. Expect all DoesShifts participants in the company set.
    var result = await _svc.GetEligibleUsersForShiftTypeAsync(shiftTypeId: 60, categoryFilter: true);
    result.Select(r => r.UserId).Should().BeEquivalentTo(new[] { 10, 12, 13 }); // every participant
}

[Fact]
public async Task Workforce_CategoryFilter_Uses_PerCompany_DoesShifts_With_Mirror_Fallback()
{
    // user D: AppUser.DoesShifts=false BUT CompanyMembership(D, candidateCompany).DoesShifts=true -> INCLUDED.
    // user E: AppUser.DoesShifts=true, no membership row -> INCLUDED (mirror fallback).
    // user F: AppUser.DoesShifts=true BUT CompanyMembership(F).DoesShifts=false -> EXCLUDED (row overrides mirror).
    // (all three are members of category 500 / or test against a null-category shift for participant-only.)
    var result = await _svc.GetEligibleUsersForShiftTypeAsync(shiftTypeId: 60, categoryFilter: true);
    result.Select(r => r.UserId).Should().Contain(new[] { /*D*/ 14, /*E*/ 15 });
    result.Select(r => r.UserId).Should().NotContain(/*F*/ 16);
}

[Fact]
public async Task Workforce_LegacyBranch_Unchanged_When_CategoryFilter_False()
{
    // categoryFilter:false (default) -> jobType filter applies exactly as before (behavior-preserving).
    var result = await _svc.GetEligibleUsersForShiftTypeAsync(shiftTypeId: 50); // default false
    result.Select(r => r.UserId).Should().BeEquivalentTo(new[] { /* jobType-matched set */ });
}
```
Fill the seed + expected ids concretely from the arrange block. Keep GroupUser excluded and IsActive=true throughout.

- [ ] **Step 2: Run — verify it FAILS to compile**

```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~CategoryEligibilityLeafTests" -- xUnit.ParallelizeTestCollections=false
```
Expected: FAIL — `GetEligibleUsersForShiftTypeAsync` has no `categoryFilter` parameter.

- [ ] **Step 3: Update the interface signature**

In `Services/IShiftAssignmentService.cs`, change the method signature to:
```csharp
Task<List<EligibleUserDto>> GetEligibleUsersForShiftTypeAsync(
    int shiftTypeId,
    int? jobTypeId = null,
    int? shiftGroupingId = null,
    bool categoryFilter = false);
```

- [ ] **Step 4: Implement the category branch in `ShiftAssignmentService.cs`**

In `GetEligibleUsersForShiftTypeAsync` (line 63), after `companyIds` is resolved (line 106) and before the existing `usersQuery` (line 108), branch on `categoryFilter`. Replace lines 108-129 (the `usersQuery` build + jobType filter + materialization) with:

```csharp
        List<int> participantUserIds;
        if (categoryFilter)
        {
            // NEW (3b): category-membership eligibility. Drop the jobType filter; gate by DoesShifts
            // (per-company membership, mirror fallback) + the shift's single category.
            // SECURITY-AUDITED: SAFE — re-scoped by companyIds (grouping-derived or shift type's company).
            // Per-company DoesShifts participants: membership row with DoesShifts=true in any candidate
            // company, OR no membership row in candidate companies AND mirrored AppUser.DoesShifts.
            var doersFromMembership = (await _db.CompanyMemberships
                .Where(m => companyIds.Contains(m.CompanyId) && m.DoesShifts)
                .Select(m => m.UserId).ToListAsync()).ToHashSet();
            var anyMembershipUserIds = (await _db.CompanyMemberships
                .Where(m => companyIds.Contains(m.CompanyId))
                .Select(m => m.UserId).ToListAsync()).ToHashSet();

            var candidates = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive
                            && u.AccountType != AccountType.GroupUser
                            && companyIds.Contains(u.CompanyId))
                .Select(u => new { u.Id, u.DoesShifts })
                .ToListAsync();

            participantUserIds = candidates
                .Where(u => doersFromMembership.Contains(u.Id)
                            || (!anyMembershipUserIds.Contains(u.Id) && u.DoesShifts))
                .Select(u => u.Id)
                .ToList();

            var catId = shiftType.CategoryId;
            if (catId.HasValue)
            {
                var membersOfCat = (await _db.UserShiftCategories
                    .Where(m => m.ShiftCategoryId == catId.Value && participantUserIds.Contains(m.UserId))
                    .Select(m => m.UserId).ToListAsync()).ToHashSet();
                participantUserIds = participantUserIds.Where(id => membersOfCat.Contains(id)).ToList();
            }
            // catId == null -> all participants (the runtime D1 fallback)
        }
        else
        {
            // LEGACY (behavior-preserving): job-type filter exactly as before.
            // SECURITY-AUDITED: SAFE — re-scoped by ShiftGrouping-derived companyIds or shift type's own companyId
            var legacyQuery = _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && companyIds.Contains(u.CompanyId));
            if (effectiveJobTypeId.HasValue)
                legacyQuery = legacyQuery.Where(u => u.JobTypeId == effectiveJobTypeId.Value);
            participantUserIds = await legacyQuery.Select(u => u.Id).ToListAsync();
        }

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => participantUserIds.Contains(u.Id))
            .Include(u => u.JobType)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                JobTypeName = u.JobType != null ? u.JobType.DisplayName : null,
                u.CompanyId
            })
            .ToListAsync();
```
The rest of the method (company-name dict, week-assignment stats, `EligibleUserDto` projection at line 171) is unchanged.

- [ ] **Step 5: Run the workforce tests — verify PASS**

```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~CategoryEligibilityLeafTests.Workforce" -- xUnit.ParallelizeTestCollections=false
```
Expected: all 4 workforce tests pass.

- [ ] **Step 6: Commit**

```
git add Services/IShiftAssignmentService.cs Services/ShiftAssignmentService.cs ShiftManager.Tests/UnitTests/Services/CategoryEligibilityLeafTests.cs
git commit -m "feat(shifts): workforce eligibility category filter behind param (3b phase 2)"
```

---

## Task 3: Tech leaf — category-aware filtering (Phase 2)

**Files:**
- Modify: `Services/IShiftCalendarService.cs` (signature)
- Modify: `Services/ShiftCalendarService.cs:242-269`
- Modify: `ShiftManager.Tests/UnitTests/Services/CategoryEligibilityLeafTests.cs` (add tech cases)

- [ ] **Step 1: Add failing tech tests**

Append to `CategoryEligibilityLeafTests.cs` (construct `ShiftCalendarService` with its ctor deps: `AppDbContext`, `ILogger`, `ICompanyCacheService`, `ICompanyLocalizationService` — copy from an existing tech test arrange block):

```csharp
[Fact]
public async Task Tech_CategoryFilter_Keeps_OfficerRank_And_Adds_Category_Gate()
{
    // Tech molecule 2; shiftType 70 RequiresOfficerRank=true, CategoryId=700.
    // officer + member of 700 + DoesShifts -> INCLUDED. non-officer member -> EXCLUDED (rank).
    // officer NOT in 700 -> EXCLUDED (category).
    var result = await _techSvc.GetEligibleUsersForShiftTypeAsync(moleculeId: 2, shiftTypeId: 70, categoryFilter: true);
    result.Select(u => u.Id).Should().BeEquivalentTo(new[] { /* officer-member */ 20 });
}

[Fact]
public async Task Tech_CategoryFilter_NullCategory_Falls_Back_To_All_Participants_Within_EligibleCompanies()
{
    var result = await _techSvc.GetEligibleUsersForShiftTypeAsync(moleculeId: 2, shiftTypeId: 71, categoryFilter: true);
    result.Select(u => u.Id).Should().BeEquivalentTo(new[] { /* all DoesShifts in eligible companies */ });
}

[Fact]
public async Task Tech_LegacyBranch_Unchanged_When_CategoryFilter_False()
{
    var result = await _techSvc.GetEligibleUsersForShiftTypeAsync(moleculeId: 2, shiftTypeId: 70); // default false
    result.Select(u => u.Id).Should().BeEquivalentTo(new[] { /* EligibleCompanyIds + rank, no category */ });
}
```

- [ ] **Step 2: Run — verify FAIL to compile** (no `categoryFilter` param). Same filter command as Task 2 Step 2.

- [ ] **Step 3: Update the tech interface signature**

In `Services/IShiftCalendarService.cs`:
```csharp
Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId, bool categoryFilter = false);
```

- [ ] **Step 4: Implement in `ShiftCalendarService.cs:242`**

Replace the method body (242-269) with the version that adds the category gate AFTER the existing company + officer-rank filters:

```csharp
    public async Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId, bool categoryFilter = false)
    {
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + shiftTypeId; eligibility filters applied
        var shiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);

        if (shiftType == null) return new List<AppUser>();

        var query = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

        // Apply company eligibility filter
        var eligibleCompanyIds = shiftType.GetEligibleCompanyIdList();
        if (eligibleCompanyIds != null)
            query = query.Where(u => eligibleCompanyIds.Contains(u.CompanyId));

        // Apply officer rank filter (SegenMishne = 9)
        if (shiftType.RequiresOfficerRank)
            query = query.Where(u => (int)u.Rank >= 9);

        if (categoryFilter)
        {
            // NEW (3b): exclude GroupUser, gate by DoesShifts (per-company, mirror fallback) + category.
            query = query.Where(u => u.AccountType != AccountType.GroupUser);

            var companyIds = await query.Select(u => u.CompanyId).Distinct().ToListAsync();
            var doersFromMembership = (await _db.CompanyMemberships
                .Where(m => companyIds.Contains(m.CompanyId) && m.DoesShifts)
                .Select(m => m.UserId).ToListAsync()).ToHashSet();
            var anyMembershipUserIds = (await _db.CompanyMemberships
                .Where(m => companyIds.Contains(m.CompanyId))
                .Select(m => m.UserId).ToListAsync()).ToHashSet();

            var candidates = await query.Select(u => new { u.Id, u.DoesShifts }).ToListAsync();
            var participantIds = candidates
                .Where(u => doersFromMembership.Contains(u.Id)
                            || (!anyMembershipUserIds.Contains(u.Id) && u.DoesShifts))
                .Select(u => u.Id).ToHashSet();

            var catId = shiftType.CategoryId;
            if (catId.HasValue)
            {
                var membersOfCat = (await _db.UserShiftCategories
                    .Where(m => m.ShiftCategoryId == catId.Value && participantIds.Contains(m.UserId))
                    .Select(m => m.UserId).ToListAsync()).ToHashSet();
                participantIds.IntersectWith(membersOfCat);
            }

            return await _db.Users
                .IgnoreQueryFilters()
                .Where(u => participantIds.Contains(u.Id))
                .Include(u => u.JobType)
                .OrderBy(u => u.DisplayName)
                .ToListAsync();
        }

        return await query
            .Include(u => u.JobType)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }
```

- [ ] **Step 5: Run tech tests — verify PASS**, then the full suite:
```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false
```
Expected: green (1762 + new tech tests).

- [ ] **Step 6: Commit**
```
git add Services/IShiftCalendarService.cs Services/ShiftCalendarService.cs ShiftManager.Tests/UnitTests/Services/CategoryEligibilityLeafTests.cs
git commit -m "feat(shifts): tech eligibility category filter (keeps officer rank) behind param (3b phase 2)"
```

---

## Task 4: Feature flag + loc keys (prereqs for router + JS)

**Files:**
- Modify: `Data/SeedData/FeatureFlagSeed.cs`
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx`

- [ ] **Step 1: Add the flag constant + default-off seed**

In `Data/SeedData/FeatureFlagSeed.cs`, add to the `Flags` class (after `HebrewDefault`, line 257):
```csharp
        // Shift eligibility (3b) — default OFF; enable per-company after CategoryId backfill is verified.
        public const string CategoryBasedShiftEligibility = "FF_CATEGORY_BASED_SHIFT_ELIGIBILITY";
```
And in `GetFeatureFlags()` (after the `HebrewDefault` line, 127), add:
```csharp
            FDisabled(Flags.CategoryBasedShiftEligibility, "When enabled (per company): the assign user-picker is filtered to DoesShifts + the shift's ShiftCategory members instead of the legacy job-type list. Default OFF — enable only after ShiftType.CategoryId is backfilled and verified for the company.", now),
```

- [ ] **Step 2: Grep each loc key BEFORE adding (a duplicate key breaks ~6 localization tests)**
```
```
Run (via Grep tool, not bash) for each of: `QuickEntry_Loading`, `QuickEntry_NoEligibleUsers`, `QuickEntry_NoEligibleUsersHint`, `QuickEntry_NoCategorySet`, `QuickEntry_NoCategorySetHint`, `QuickEntry_NoTextMatch`, `QuickEntry_LoadFailedFallback`, `QuickEntry_ResultsAvailable`, `QuickEntry_ShowAllWorkers`, `Admin_ShiftType_NoCategory_Badge`, `Admin_ShiftType_NoCategory_Tooltip` in `Resources/`. Expected: zero matches. (`QuickEntry_NoMatches` already exists — KEEP it for chores/duty/slash; do NOT touch.)

- [ ] **Step 3: Add EN keys to `Resources/SharedResources.resx`** (alphabetical-ish, near other `QuickEntry_*`):
```xml
  <data name="QuickEntry_Loading" xml:space="preserve"><value>Loading eligible users…</value></data>
  <data name="QuickEntry_NoEligibleUsers" xml:space="preserve"><value>No eligible users for this shift</value></data>
  <data name="QuickEntry_NoEligibleUsersHint" xml:space="preserve"><value>No one is a member of this shift's category</value></data>
  <data name="QuickEntry_NoCategorySet" xml:space="preserve"><value>This shift has no category set</value></data>
  <data name="QuickEntry_NoCategorySetHint" xml:space="preserve"><value>Ask an admin to assign a category</value></data>
  <data name="QuickEntry_NoTextMatch" xml:space="preserve"><value>No eligible users match "{0}"</value></data>
  <data name="QuickEntry_LoadFailedFallback" xml:space="preserve"><value>Couldn't load eligible users — try again</value></data>
  <data name="QuickEntry_ResultsAvailable" xml:space="preserve"><value>{0} eligible users</value></data>
  <data name="QuickEntry_ShowAllWorkers" xml:space="preserve"><value>Show all shift workers</value></data>
  <data name="Admin_ShiftType_NoCategory_Badge" xml:space="preserve"><value>No category</value></data>
  <data name="Admin_ShiftType_NoCategory_Tooltip" xml:space="preserve"><value>Users can't be assigned to this shift until it has a category</value></data>
```

- [ ] **Step 4: Add HE keys to `Resources/SharedResources.he-IL.resx`:**
```xml
  <data name="QuickEntry_Loading" xml:space="preserve"><value>טוען עובדים זמינים…</value></data>
  <data name="QuickEntry_NoEligibleUsers" xml:space="preserve"><value>אין עובדים זמינים למשמרת זו</value></data>
  <data name="QuickEntry_NoEligibleUsersHint" xml:space="preserve"><value>אף עובד אינו משויך לקטגוריית המשמרת</value></data>
  <data name="QuickEntry_NoCategorySet" xml:space="preserve"><value>למשמרת זו לא הוגדרה קטגוריה</value></data>
  <data name="QuickEntry_NoCategorySetHint" xml:space="preserve"><value>פנו למנהל להגדרת קטגוריה</value></data>
  <data name="QuickEntry_NoTextMatch" xml:space="preserve"><value>אין עובדים זמינים התואמים ל-"{0}"</value></data>
  <data name="QuickEntry_LoadFailedFallback" xml:space="preserve"><value>טעינת העובדים נכשלה — נסו שוב</value></data>
  <data name="QuickEntry_ResultsAvailable" xml:space="preserve"><value>{0} עובדים זמינים</value></data>
  <data name="QuickEntry_ShowAllWorkers" xml:space="preserve"><value>הצג את כל עובדי המשמרת</value></data>
  <data name="Admin_ShiftType_NoCategory_Badge" xml:space="preserve"><value>ללא קטגוריה</value></data>
  <data name="Admin_ShiftType_NoCategory_Tooltip" xml:space="preserve"><value>לא ניתן לשבץ עובדים למשמרת זו עד שתוגדר לה קטגוריה</value></data>
```

- [ ] **Step 5: Build + run the localization tests**
```
dotnet build ShiftManager.csproj
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~Localization" -- xUnit.ParallelizeTestCollections=false
```
Expected: green (parity tests confirm every EN key has an HE counterpart).

- [ ] **Step 6: Commit**
```
git add Data/SeedData/FeatureFlagSeed.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(shifts): CategoryBasedShiftEligibility flag (default off) + 3b loc keys"
```

---

## Task 5: `ShiftCandidateService` router (Phase 3 core)

**Files:**
- Create: `Services/IShiftCandidateService.cs`
- Create: `Services/ShiftCandidateService.cs`
- Modify: `Program.cs` (DI registration)
- Create: `ShiftManager.Tests/UnitTests/Services/ShiftCandidateServiceTests.cs`

- [ ] **Step 1: Define the interface + DTOs**

Create `Services/IShiftCandidateService.cs`:
```csharp
namespace ShiftManager.Services;

/// <summary>One assignable candidate, uniform across workforce + tech leaves.</summary>
public record EligibleCandidateDto(int Id, string Name, string? CompanyName);

/// <summary>
/// Router result. <see cref="Reason"/> is "category" | "sharedFallback" | "noCategory" and drives the
/// UI empty-state messaging; see the 3b plan's reason table.
/// </summary>
public record EligibleCandidatesResult(string Reason, IReadOnlyList<EligibleCandidateDto> Users);

/// <summary>
/// The single per-shift candidate router. Reads the company-level CategoryBasedShiftEligibility flag,
/// dispatches to the preserved workforce/tech leaf methods (does NOT merge their bodies), applies the
/// null-category fallback rules, and projects a uniform list. Authorization stays in the endpoint.
/// </summary>
public interface IShiftCandidateService
{
    Task<EligibleCandidatesResult> GetEligibleCandidatesAsync(
        int moleculeId, int shiftTypeId, int currentCompanyId, bool allowFallback = false);
}
```

- [ ] **Step 2: Write the failing router tests**

Create `ShiftManager.Tests/UnitTests/Services/ShiftCandidateServiceTests.cs`. Inject a fake `IFeatureFlagService` (return configurable bool) + real `ShiftAssignmentService` + real `ShiftCalendarService` over real SQLite. Cover the reason table:
```csharp
[Fact] public async Task FlagOff_Returns_Legacy_Set_With_Reason_Category() { /* flag=false; expect jobType-filtered legacy + reason "category" */ }
[Fact] public async Task FlagOn_CategorySet_Returns_Members_Reason_Category() { /* flag=true; CategoryId!=null; reason "category" */ }
[Fact] public async Task FlagOn_NullCategory_HomeType_Returns_All_Reason_SharedFallback() { /* Key=HOME; reason "sharedFallback"; all participants */ }
[Fact] public async Task FlagOn_NullCategory_NullJobType_Returns_All_Reason_SharedFallback() { /* JobTypeId==null shared */ }
[Fact] public async Task FlagOn_NullCategory_Assignable_NoFallback_Returns_Empty_Reason_NoCategory() { /* has JobTypeId, allowFallback=false; users empty; reason "noCategory" */ }
[Fact] public async Task FlagOn_NullCategory_Assignable_WithFallback_Returns_All_Reason_SharedFallback() { /* allowFallback=true; all participants; reason "sharedFallback" */ }
[Fact] public async Task TechMolecule_Dispatches_To_Tech_Leaf_With_CompanyNames() { /* molecule.Type==Tech; result carries CompanyName per user */ }
```

- [ ] **Step 3: Run — verify FAIL** (interface unimplemented). Filter `~ShiftCandidateServiceTests`.

- [ ] **Step 4: Implement `Services/ShiftCandidateService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Data.SeedData;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters is SAFE here — molecule-scoped reads; the calling endpoint
// gates molecule access (ValidateScopeAccessAsync + shiftType-belongs-to-molecule) before this runs.
public class ShiftCandidateService : IShiftCandidateService
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _workforce;
    private readonly IShiftCalendarService _tech;
    private readonly IFeatureFlagService _flags;

    public ShiftCandidateService(
        AppDbContext db,
        IShiftAssignmentService workforce,
        IShiftCalendarService tech,
        IFeatureFlagService flags)
    {
        _db = db;
        _workforce = workforce;
        _tech = tech;
        _flags = flags;
    }

    public async Task<EligibleCandidatesResult> GetEligibleCandidatesAsync(
        int moleculeId, int shiftTypeId, int currentCompanyId, bool allowFallback = false)
    {
        var shiftType = await _db.ShiftTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);
        var molecule = await _db.Molecules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == moleculeId);
        if (shiftType == null || molecule == null)
            return new EligibleCandidatesResult("category", Array.Empty<EligibleCandidateDto>());

        var useCategoryRule = await _flags.IsEnabledAsync(
            FeatureFlagSeed.Flags.CategoryBasedShiftEligibility, userId: null, companyId: currentCompanyId);

        var isTech = molecule.Type == MoleculeType.Tech;

        // Flag OFF -> behavior-preserving legacy set.
        if (!useCategoryRule)
            return new EligibleCandidatesResult("category", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: false));

        // Flag ON -> category rule + null-category fork.
        if (shiftType.CategoryId.HasValue)
            return new EligibleCandidatesResult("category", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: true));

        var isShared = shiftType.Key is "HOME" or "OFFLINE" || shiftType.JobTypeId == null;
        if (isShared || allowFallback)
            return new EligibleCandidatesResult("sharedFallback", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: true));

        // Assignable type missing its category, no fallback requested -> structural zero + admin nudge.
        return new EligibleCandidatesResult("noCategory", Array.Empty<EligibleCandidateDto>());
    }

    private async Task<IReadOnlyList<EligibleCandidateDto>> DispatchAsync(
        bool isTech, int moleculeId, ShiftType shiftType, bool categoryFilter)
    {
        if (isTech)
        {
            var users = await _tech.GetEligibleUsersForShiftTypeAsync(moleculeId, shiftType.Id, categoryFilter);
            var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
            var names = await _db.Companies.IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            return users.Select(u => new EligibleCandidateDto(
                u.Id, u.DisplayName, names.GetValueOrDefault(u.CompanyId))).ToList();
        }

        var dtos = await _workforce.GetEligibleUsersForShiftTypeAsync(
            shiftType.Id, shiftType.JobTypeId, shiftType.ShiftGroupingId, categoryFilter);
        return dtos.Select(d => new EligibleCandidateDto(d.UserId, d.DisplayName, d.CompanyName)).ToList();
    }
}
```

- [ ] **Step 5: Register DI in `Program.cs`** — next to where `IShiftAssignmentService` is registered (grep `AddScoped<IShiftAssignmentService`):
```csharp
builder.Services.AddScoped<IShiftCandidateService, ShiftCandidateService>();
```

- [ ] **Step 6: Run router tests — verify PASS**, then full suite. Commit:
```
git add Services/IShiftCandidateService.cs Services/ShiftCandidateService.cs Program.cs ShiftManager.Tests/UnitTests/Services/ShiftCandidateServiceTests.cs
git commit -m "feat(shifts): ShiftCandidateService router — flag + reason + null-category fork (3b phase 3)"
```

---

## Task 6: Generalize the endpoint (Phase 3 wiring)

**Files:**
- Modify: `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs`

- [ ] **Step 1: Add an endpoint contract test (or extend an existing API page test)**

If the repo has a PageModel/integration test harness for `/Api/Calendar/*`, add a test asserting the JSON shape `{ success, reason, users:[{id,name,companyName}] }` and that `allowFallback=true` promotes a `noCategory` result to `sharedFallback`. If no such harness exists, rely on `ShiftCandidateServiceTests` (Task 5) for logic coverage and verify the endpoint shape in the Phase-4 browser sweep — **note this explicitly in the commit message** so it's not mistaken for full coverage.

- [ ] **Step 2: Rewrite the endpoint to call the router**

Replace the constructor deps and `OnGetAsync` body in `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs`. Swap `IShiftCalendarService` for `IShiftCandidateService`, add `allowFallback`, keep ALL existing authz (lines 48-74), and change the result projection (line 76-82):

```csharp
    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int shiftTypeId,
        [FromQuery] bool allowFallback = false)
    {
        try
        {
            // ... KEEP lines 48-74 unchanged: auth, param validation, ValidateScopeAccessAsync,
            //     molecule-match fallback, and the shiftType-belongs-to-molecule guard ...

            var currentCompanyId = 0;
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            int.TryParse(companyIdClaim, out currentCompanyId);

            var result = await _candidateService.GetEligibleCandidatesAsync(
                moleculeId, shiftTypeId, currentCompanyId, allowFallback);

            return new JsonResult(new
            {
                success = true,
                reason = result.Reason,
                users = result.Users.Select(u => new { id = u.Id, name = u.Name, companyName = u.CompanyName }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEligibleUsersForShift");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_GET_ELIGIBLE_USERS_FAILED",
                    _localizer["Error_CalendarApi_GetEligibleUsersFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            { StatusCode = 500 };
        }
    }
```
Update the constructor: replace `IShiftCalendarService _shiftCalendarService` field with `IShiftCandidateService _candidateService` (keep `_scopeFilterService`, `_db`, `_logger`, `_localizer` — they're still used by the unchanged authz block). Update the class XML doc to say "all molecule kinds" not "Tech only".

- [ ] **Step 3: Build, full suite, browser-smoke the endpoint**
```
dotnet build ShiftManager.csproj
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false
```
With the flag OFF (default), the endpoint must return today's set — confirm a tech shift still lists the same users (behavior-preserving). Commit:
```
git add Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs
git commit -m "feat(shifts): route eligible-users endpoint through ShiftCandidateService; +reason/+companyName (3b phase 3)"
```

---

## Task 7: Wire the bottom-sheet `<select>` (Phase 4 — desktop dropdown + mobile sheet)

**Files:**
- Modify: `wwwroot/js/calendar-bottom-sheet.js`

> JS has no unit harness — verify in-browser (webapp-testing skill). Restart the dev app first; dev serves `bin/Debug` static assets and will not pick up source JS until restarted (see memory `dev_app_stale_static_assets`). `curl` the served file for a marker before browser-testing.

- [ ] **Step 1: Generalize the eligible-fetch to all molecule kinds**

In `calendar-bottom-sheet.js`, the eligible path (lines 475-481) currently gates on `calPageConfig.isTechMolecule`. Change it so **shift-mode rows always** fetch per-shift (the endpoint now routes by molecule kind server-side):
```js
            // For shift-mode rows, fetch the per-shift eligible list (server routes workforce/tech).
            if (itemType === 'user' && cellData.rowId && cellData.rowId.indexOf('shift-') === 0) {
                var shiftTypeId = parseInt(cellData.rowId.replace('shift-', ''), 10);
                if (!isNaN(shiftTypeId) && shiftTypeId > 0) {
                    populateEligibleUsersAsync(userSelect, calPageConfig.moleculeId, shiftTypeId, cellData.date);
                    // skip the legacy getAvailableUsers page-list branch below
                }
            } else {
                // ... existing user-mode / non-shift branch unchanged ...
            }
```

- [ ] **Step 2: Extend `populateEligibleUsersAsync` for `reason`, companyName suffix, and the escape-hatch**

Rewrite `populateEligibleUsersAsync` (line 775) to read `data.reason` + `data.users[].companyName`, render the company suffix on each `<option>` (Decision 9: native `<select>` gets ` — {company}`), and handle the three reason states. Add an `allowFallback` param:
```js
    async function populateEligibleUsersAsync(selectEl, moleculeId, shiftTypeId, date, allowFallback) {
        var loadingOpt = document.createElement('option');
        loadingOpt.value = ''; loadingOpt.disabled = true; loadingOpt.textContent = loc('QuickEntry_Loading', '...');
        selectEl.appendChild(loadingOpt);
        selectEl.setAttribute('aria-busy', 'true');
        try {
            var url = '/Api/Calendar/GetEligibleUsersForShift?moleculeId=' + moleculeId + '&shiftTypeId=' + shiftTypeId
                + (allowFallback ? '&allowFallback=true' : '');
            var response = await fetch(url, { credentials: 'same-origin' });
            if (!response.ok) throw new Error('Server returned ' + response.status);
            var data = await response.json();
            if (loadingOpt.parentNode === selectEl) selectEl.removeChild(loadingOpt);
            selectEl.removeAttribute('aria-busy');
            if (!data.success) throw new Error('unsuccessful');

            if (Array.isArray(data.users) && data.users.length > 0) {
                data.users.forEach(function (user) {
                    var opt = document.createElement('option');
                    opt.value = user.id;
                    opt.dataset.userName = user.name;
                    opt.textContent = user.companyName ? (user.name + ' — ' + user.companyName) : user.name;
                    selectEl.appendChild(opt);
                });
                if (date && moleculeId) {
                    decorateOptionsWithBusyAsync(selectEl, data.users.map(function (u) { return u.id; }), date, moleculeId, null)
                        .catch(function (err) { console.warn('Busy decoration failed:', err); });
                }
            } else if (data.reason === 'noCategory') {
                // Structural zero: assignable shift missing a category. Nudge + escape hatch.
                appendDisabledOption(selectEl, loc('QuickEntry_NoCategorySet', 'This shift has no category set'));
                appendFallbackButton(selectEl, function () {
                    clearOptions(selectEl);
                    populateEligibleUsersAsync(selectEl, moleculeId, shiftTypeId, date, true); // allowFallback
                });
            } else {
                appendDisabledOption(selectEl, loc('QuickEntry_NoEligibleUsers', 'No eligible users for this shift'));
            }
        } catch (e) {
            if (loadingOpt.parentNode === selectEl) selectEl.removeChild(loadingOpt);
            selectEl.removeAttribute('aria-busy');
            console.error('Failed to load eligible users:', e);
            appendDisabledOption(selectEl, loc('QuickEntry_LoadFailedFallback', "Couldn't load eligible users — try again"));
        }
    }
```
Add the small helpers `appendDisabledOption(selectEl, text)`, `clearOptions(selectEl)` (remove all real options), and `appendFallbackButton` — since a native `<select>` can't host a button, render the "Show all shift workers" affordance as a sibling element next to the select (a `<button class="btn btn-link">` inserted after `selectEl` that calls the callback then removes itself). Use `loc('QuickEntry_ShowAllWorkers', 'Show all shift workers')`. (`loc` already exists at line 35.)

- [ ] **Step 3: Browser-verify (flag ON for the test company)**

Enable the flag for the test company (Owner > Feature Flags, or `SetFlagAsync`). With `webapp-testing`: open Shifts in shift-mode, tap a categorized shift cell → only category members, each with ` — Company` suffix + busy glyphs; tap an uncategorized assignable shift → "no category set" + "Show all shift workers" button → clicking lists all `DoesShifts` users. Verify in light/dark + Hebrew RTL. With flag OFF → unchanged legacy list.

- [ ] **Step 4: Commit**
```
git add wwwroot/js/calendar-bottom-sheet.js
git commit -m "feat(shifts): bottom-sheet picker uses category eligibility + company suffix + no-category nudge (3b phase 4)"
```

---

## Task 8: Quick-entry overhaul (Phase 5)

**Files:**
- Modify: `wwwroot/js/calendar-quick-entry.js`

> Largest task — split into sub-commits. Same dev-restart/static-asset caveat as Task 7.

- [ ] **Step 8a: Fetch-on-focus + two-tier cache + per-shift candidate set**

Add a module-scoped cache and a fetch function. Eligibility is cached per `molecule:shiftType` for the session; busy metadata is decorated separately and never cached (invalidate on the `calendar:grid-refreshed` event the page already emits).
```js
    var eligibleCache = {};        // key "mol:shift" -> { users:[{id,name,companyName}], reason }
    var eligibleInFlight = {};     // key -> Promise (dedupe concurrent focus)
    function eligibleKey(mol, st) { return mol + ':' + st; }

    function shiftTypeIdForCell(cellData) {
        if (cellData && cellData.rowId && cellData.rowId.indexOf('shift-') === 0) {
            var n = parseInt(cellData.rowId.replace('shift-', ''), 10);
            return (!isNaN(n) && n > 0) ? n : 0;
        }
        return 0; // user-mode rows have no per-shift user filtering
    }

    function fetchEligible(mol, st, allowFallback) {
        var key = eligibleKey(mol, st);
        if (!allowFallback && eligibleCache[key]) return Promise.resolve(eligibleCache[key]);
        if (!allowFallback && eligibleInFlight[key]) return eligibleInFlight[key];
        var url = '/Api/Calendar/GetEligibleUsersForShift?moleculeId=' + mol + '&shiftTypeId=' + st
            + (allowFallback ? '&allowFallback=true' : '');
        var p = fetch(url, { credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error('status ' + r.status); return r.json(); })
            .then(function (data) {
                if (!data.success) throw new Error('unsuccessful');
                var entry = { users: data.users || [], reason: data.reason || 'category' };
                eligibleCache[key] = entry;
                delete eligibleInFlight[key];
                return entry;
            })
            .catch(function (e) { delete eligibleInFlight[key]; throw e; });
        if (!allowFallback) eligibleInFlight[key] = p;
        return p;
    }
```
In `openInput` (line 595), after `input._cellData = cellData;` (line 616), kick off the fetch when the cell is a shift-mode user-assignment cell and set a loading flag:
```js
        input._eligState = 'idle';
        var stId = shiftTypeIdForCell(cellData);
        if (getCurrentMode() === 'shift' && !isChoresCalendar() && stId > 0 && calPageConfig && calPageConfig.moleculeId > 0) {
            input._eligState = 'loading';
            announce(getLocalizedLabel('QuickEntry_Loading'));
            fetchEligible(calPageConfig.moleculeId, stId, false)
                .then(function (entry) {
                    input._eligible = entry;
                    input._eligState = 'ready';
                    announce(formatLabel('QuickEntry_ResultsAvailable', entry.users.length));
                    loadItems();              // rebuild allItems from the per-shift list
                    if (activeInput === input) updateDropdown(activeInput.value || '');
                    flushBufferedEnter();
                })
                .catch(function () {
                    input._eligState = 'failed';
                    announce(getLocalizedLabel('QuickEntry_LoadFailedFallback'));
                    if (activeInput === input) updateDropdown(activeInput.value || '');
                });
        }
```
Add a `window` listener for `calendar:grid-refreshed` that clears `eligibleCache` (busy data is per-date and must re-decorate; eligibility is cheap to refetch on next focus).

- [ ] **Step 8b: Source `loadItems` from the per-shift list when present (no silent fallback)**

Change `loadItems` (line 136) so that when the active input has a ready per-shift eligible set, `allItems` is built from THAT (not the whole-molecule `assignee-select`). Keep the page-select path only for non-shift contexts:
```js
    function loadItems() {
        allItems = [];
        // Per-shift eligibility (3b): if the active cell loaded an eligible set, use it verbatim.
        if (activeInput && activeInput._eligible && Array.isArray(activeInput._eligible.users)) {
            activeInput._eligible.users.forEach(function (u) {
                allItems.push({ id: String(u.id), text: u.name, companyName: u.companyName || null, type: 'user', key: null, color: null });
            });
            // still load chore/duty/slash sources below (unchanged)
        } else {
            var assigneeSelect = document.querySelector('[data-role="assignee-select"]');
            if (assigneeSelect && assigneeSelect.dataset.quickentrySkip !== 'true') {
                // ... existing option-reading loop (lines 144-157) unchanged ...
            }
        }
        // ... existing chore/duty option loading (lines 160-184) unchanged ...
    }
```
**No silent fallback:** when `_eligState === 'failed'`, `updateDropdown` must render the `QuickEntry_LoadFailedFallback` row and NOT fall back to the page list (the server re-validates on assign regardless).

- [ ] **Step 8c: Non-`role=option` state rows + disambiguation line in `updateDropdown`**

In `updateDropdown` (line 287) for the user group, add rendering branches BEFORE the normal results, driven by `activeInput._eligState` / `activeInput._eligible.reason`:
- `loading` → one row, text `QuickEntry_Loading`, class e.g. `quick-entry-state`, NO `role="option"`.
- `failed` → row `QuickEntry_LoadFailedFallback`, no `role=option`.
- ready + `reason==='noCategory'` → row `QuickEntry_NoCategorySet` + sub-hint `QuickEntry_NoCategorySetHint`, plus a "Show all shift workers" actionable row (clicking calls `fetchEligible(mol, st, true)` then `loadItems()/updateDropdown`), none with `role=option`.
- ready + users empty + `reason==='category'` → `QuickEntry_NoEligibleUsers` + hint `QuickEntry_NoEligibleUsersHint`, no `role=option`.
- ready + users non-empty + query matches nobody → `QuickEntry_NoTextMatch` with the query in a `<bdi>{0}</bdi>`, no `role=option`.

For each rendered user `role="option"`, add the **disambiguation secondary line** (Decision 8): a muted `<span>` with company name + the busy glyph for that user on `cellData.date`. Reuse the busy vocabulary by calling `/Api/Calendar/GetBusyStates` once per dropdown render for the visible user ids (mirror `decorateOptionsWithBusyAsync` in bottom-sheet — glyphs 🏠📴⏱🧹🛡🌴). **Omit the category name.** Carry `hasHardError` onto the item.

Because `moveSelection` (line 531) already queries `[role="option"]`, the state rows are automatically skipped by arrow-nav. Verify the "Show all shift workers" row is reachable by click (mousedown) even though it's not an option.

- [ ] **Step 8d: Buffer Enter during the load window + exclude hard-conflicted from auto-commit**

In `handleKeydown` (line 692), when `e.key === 'Enter'` and `activeInput._eligState === 'loading'`, set `activeInput._bufferedEnter = true; e.preventDefault();` and return (do NOT commit against a stale/empty list). `flushBufferedEnter()` (called from 8a on ready) replays the commit using the now-loaded top match. In `selectItem` (line 794) / the Enter-top-match path, if the chosen item carries `hasHardError`, do NOT auto-commit it (require an explicit click) — matches Decision 8.

Add the small a11y + label helpers near `getLocalizedLabel` (line 64):
```js
    var liveRegion = null;
    function announce(msg) {
        if (!liveRegion) {
            liveRegion = document.createElement('div');
            liveRegion.setAttribute('aria-live', 'polite');
            liveRegion.className = 'sr-only';      // visually-hidden utility already in site CSS
            document.body.appendChild(liveRegion);
        }
        liveRegion.textContent = msg || '';
    }
    function formatLabel(key, n) { return (getLocalizedLabel(key) || '').replace('{0}', n); }
```
Set `aria-busy` on the combobox input while `_eligState === 'loading'`.

- [ ] **Step 8e: Browser-verify all quick-entry states (flag ON)**

webapp-testing sweep: focus a categorized shift cell → loading row → results with company+busy secondary line; uncategorized assignable → "no category set" + hint + "Show all shift workers" (works); empty category → "no eligible users" + hint; type gibberish → "No eligible users match …"; kill the network (DevTools offline) → "Couldn't load…" and NO page-list fallback; fast-type Enter during load → commit lands on the loaded top match, not empty. Verify arrow-nav skips all state rows; screen-reader/aria-live announces loading + count + cause. Light/dark + Hebrew RTL. Commit per sub-step (8a..8e) or one squashed commit:
```
git add wwwroot/js/calendar-quick-entry.js
git commit -m "feat(shifts): quick-entry per-shift eligibility — fetch-on-focus, cache, states, a11y, disambiguation, Enter buffering (3b phase 5)"
```

---

## Task 9: Admin "No category" badge on Blueprints (Phase 6)

**Files:**
- Modify: `Pages/Owner/Blueprints.cshtml:194-200`

- [ ] **Step 1: Add the badge next to the shift Key cell**

In the `<tr>` for each shift type, after the `IsOffline` badge (line 197-200), add (assignable = has a JobType, not a shared HOME/OFFLINE):
```cshtml
                                @if (shiftType.CategoryId == null && shiftType.JobTypeId != null
                                     && !shiftType.IsOffline && shiftType.Key != "HOME")
                                {
                                    <span class="badge badge-warning"
                                          title="@Localizer["Admin_ShiftType_NoCategory_Tooltip"]">
                                        <loc key="Admin_ShiftType_NoCategory_Badge" />
                                    </span>
                                }
```
Confirm `Model.ShiftTypes` items expose `CategoryId` + `JobTypeId` (they're `ShiftType` entities — they do). If the page projects a view-model instead, add the two fields to that projection.

- [ ] **Step 2: Build + browser-verify the badge** appears only for uncategorized assignable types, in both themes/RTL, with a readable contrast (`badge-warning` already paired in the design system). Commit:
```
git add Pages/Owner/Blueprints.cshtml
git commit -m "feat(shifts): 'No category' admin badge on Blueprints for uncategorized assignable types (3b phase 6)"
```

---

## Task 10: Docs, MEMORY, FinalProductPublish regen (Phase 7)

**Files:**
- Modify: `docs/superpowers/specs/2026-06-14-category-based-shift-eligibility-design.md` (mark IMPLEMENTED + record the noCategory+button resolution)
- Modify: `C:\Users\katzi\.claude\projects\C--Users-katzi-Downloads-ShiftManager\memory\shift_assign_grant_and_eligibility_2026-06-14.md` + `MEMORY.md` index line
- Regenerate: `FinalProductPublish/**` (deploy step — ASK THE USER for the version)

- [ ] **Step 1: Update the spec status** to IMPLEMENTED and append a short "Resolved at build time" note: the null-CategoryId fork = empty + "Show all shift workers" escape-hatch (`allowFallback`), flag default-off, two JS consumers (bottom-sheet + quick-entry), router = `ShiftCandidateService`.

- [ ] **Step 2: Update memory** — set 3b STATUS to IMPLEMENTED on `dev` with the final test count, list the new files (migration, `ShiftTypeCategoryBackfillSql`, `ShiftCandidateService`, flag `FF_CATEGORY_BASED_SHIFT_ELIGIBILITY`), the per-company default-off rollout step, and the DEPLOY TODO (FinalProductPublish regen + per-company enable AFTER verifying backfill). Update the `MEMORY.md` one-line index.

- [ ] **Step 3: Final full sequential suite — must be green**
```
dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false
```

- [ ] **Step 4: Regenerate FinalProductPublish (ASK USER FOR `-Version` FIRST — do not invent it)**
```
powershell -ExecutionPolicy Bypass -Command "& { $ConfirmPreference='None'; & ./scripts/Update-FinalProductPublish.ps1 -Version '<x.y.z>' }"
```

- [ ] **Step 5: Commit**
```
git add docs/superpowers/specs/2026-06-14-category-based-shift-eligibility-design.md FinalProductPublish
git commit -m "docs(shifts): 3b category eligibility implemented; regen FinalProductPublish <x.y.z>"
```

---

## Self-Review

**Spec coverage (§3 scope + §6R decisions):**
1. Data backfill (§3.1 / D2) → Task 1. ✅
2. Eligibility rule switch + per-company DoesShifts (§3.2 / D3, canonical expr) → Tasks 2-3 (leaves) + 5 (router). ✅
3. Unified per-shift endpoint, behavior-preserving first (§3.3 / D5) → Tasks 5-6; flag-off legacy path proven. ✅
4. Wire all UIs (§3.4) → bottom-sheet Task 7, quick-entry Task 8. (Desktop dropdown = bottom-sheet's native `<select>`; only 2 JS consumers exist.) ✅
5. Quick-entry behavior P0/P1 (§3.5 / Quick-entry reqs) → Task 8a-8e (fetch-on-focus, two-tier cache, no silent fallback, Enter buffering, state rows, aria-busy + aria-live). ✅
6. Disambiguation line (§3.6 / D8) → Task 8c (company + busy glyph, omit category, hasHardError excludes from auto-commit). ✅
7. Consistency: empty-query order = DisplayName (§3.7 / D4) → leaf methods + tech already `OrderBy(DisplayName)`; workforce path returns DTOs ordered by the same; confirm in Task 8c render. ✅
8. Rollout flag default-off (D1/§Ordering) → Task 4 + router Task 5. ✅
9. Zero/empty UX reason field (D2/7) → router reason + Task 8c rows. ✅
10. Admin badge (D5/10) → Task 9. ✅
11. Loc keys EN+HE incl. the added `QuickEntry_ShowAllWorkers` → Task 4. ✅
12. Multi-category user (owner note) → Task 2 `Workforce_CategoryFilter_Returns_Only_Category_Members_Across_Multiple_Categories`. ✅

**Type consistency:** router returns `EligibleCandidatesResult(Reason, Users:EligibleCandidateDto(Id,Name,CompanyName))`; endpoint serializes `{id,name,companyName}` + top-level `reason`; both JS consumers read `data.users[].companyName` + `data.reason`. Leaf signatures both end `..., bool categoryFilter = false`. Flag constant `FeatureFlagSeed.Flags.CategoryBasedShiftEligibility` referenced identically in router. Consistent.

**Placeholder scan:** test arrange blocks intentionally say "copy the existing harness" + concrete ids are filled at write-time from the seed — these are TDD steps where the seed is authored alongside the assertions, not hidden logic. All production code steps contain complete code.

**Known follow-ups (OUT of 3b, per spec):** bulk-categorize tool + save-time category validation (→ Category & Roster admin phase); retiring the legacy branch + collapsing the flag to global (post per-company rollout).
