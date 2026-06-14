# Chores↔ShiftType Parity — Phase 5 (Fairness) Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (- [ ]) syntax.
**Goal:** Make chore fairness in the Justice engine **duration-weighted** (replace the chore `g.Count()` with `g.Sum(c => c.WeightMinutes)`), add an optional **`ChoreCategoryId`** narrowing filter to chore actuals/sparkline, scale chore targets by the fixed `DEFAULT_CHORE_WEIGHT_MINUTES = 480` so chore-equivalent targets compare against weighted minutes, and surface a chore-category selector + **hours**-formatted absolute load in both the calendar drawer and the standalone `/Admin/Analytics` page. Deviation / spread / banding / sparkline-shape math is deviation-driven and stays byte-for-byte unchanged. Shift / on-duty branches are untouched.
**Architecture:** Extend the existing single `JusticeService` engine (`Services/JusticeService.cs`). The only service-layer edits are confined to (a) the two chore branches — `CountActualPerUserAsync` (~584-598) and `GetSparklineSeriesAsync` (~848-867) — swapping count→Σ`WeightMinutes` and applying an optional category filter, and (b) the chore expected-target resolution (`ResolvePerUserChoreOrOnDuty` ~284-300 and `ResolveSingleWorkTypeExpected` ~922-944) multiplying the chore `ExpectedCount` by 480. A new nullable `JusticeQuery.ChoreCategoryId` (null = today's behavior) threads the filter through. UI: the drawer entry `OnGetJusticeAsync` (`Pages/Calendar/Chores.cshtml.cs` ~537) gains an optional `choreCategoryId` and category `<select>` (mirroring the existing ShiftCategory selector), and `/Admin/Analytics` gains a `choreCategoryId` query param + category selector shown only when `WorkType==Chore`, with absolute Actual/Expected rendered through the Phase-2 `DurationFormat.FormatHours(int)` helper.
**Tech Stack:** ASP.NET Core 8.0, EF Core (SQLite), xUnit + FluentAssertions.
**Depends on:** Phase 1 (Chore.WeightMinutes, ChoreType.ChoreCategoryId) + Phase 2 (Services/DurationFormat.cs). **Spec:** docs/superpowers/specs/2026-06-14-chores-shifttype-parity-design.md
---

## Dependency notes (read before starting)

- **Phase 1 must be merged first.** This plan reads `Chore.WeightMinutes` (int, frozen at create) and `ChoreType.ChoreCategoryId` (int?, FK to `ChoreCategory`). Both are added by the Phase 1 plan. As of this plan's authoring, `Models/Chore.cs` does **not** yet have `WeightMinutes` and `Models/ChoreType.cs` does **not** yet have `ChoreCategoryId` — if those properties are absent when you start, **STOP**: Phase 1 has not landed and every task here will fail to compile.
- **Phase 2 must be merged first.** Task 5/6 reference `ShiftManager.Services.DurationFormat.FormatHours(int minutes)` (static). This helper is authored in the Phase 2 plan at `Services/DurationFormat.cs`. **Do NOT redefine it here.** If it is missing when you reach Task 5, STOP and confirm Phase 2 landed. (As of authoring, `Services/DurationFormat.cs` does not exist yet.)
- **No grant changes.** Per spec §7.6/§9 this feature reuses `ViewJusticeTable` (#133) / `EditJusticeTargets` (#134). Do **not** touch `GrantTypeSeed.cs`, `RoleTemplateSeed.cs`, or `RoleTemplateAutoGrantTests.cs`.
- **The 480 constant.** `DEFAULT_CHORE_WEIGHT_MINUTES = 480` is the Phase 1 fallback weight. In this plan it lives as a `private const int` on `JusticeService` (the service must not depend on a chore-domain constant location that may not exist; a local const is the minimal, self-contained choice). If Phase 1 exposes a shared public constant (e.g. `ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES`), prefer referencing that instead of redeclaring — check first; either way the numeric value is 480.
- **Test project / run command.** Tests live in `ShiftManager.Tests/ShiftManager.Tests.csproj`. All runs MUST be sequential: append `-- xUnit.ParallelizeTestCollections=false` (parallel `:memory:` SQLite causes spurious contention failures). Tests use real SQLite (`DataSource=:memory:;Foreign Keys=False`), NOT `UseInMemoryDatabase` — the in-memory provider hides EF SQL-translation bugs (`Sum`, case-folding).

---

## File Structure

```
Services/
  JusticeViewModels.cs        # MODIFY  Task 1 — add ChoreCategoryId to JusticeQuery record
  JusticeService.cs           # MODIFY  Tasks 2,3,4 — weighted chore actual, sparkline minutes, target ×480
  DurationFormat.cs           # READ-ONLY (Phase 2) — FormatHours(int) consumed by UI tasks
Pages/Calendar/
  Chores.cshtml.cs            # MODIFY  Task 5 — OnGetJusticeAsync gains choreCategoryId; thread into JusticeQuery; expose category list to view
  Chores.cshtml               # MODIFY  Task 5 — chore-category <select> in the drawer toolbar
Pages/Admin/
  Analytics.cshtml.cs         # MODIFY  Task 6 — ChoreCategoryId bind param; PopulateChoreCategoryOptionsAsync; thread into JusticeQuery
  Analytics.cshtml            # MODIFY  Task 6 — category selector (WorkType==Chore only) + hours-formatted Actual/Expected
wwwroot/js/
  justice-panel.js            # MODIFY  Task 5 — category-select wiring + hours rendering of chore actual/expected
Resources/
  SharedResources.resx        # MODIFY  Tasks 5,6 — Justice_Filter_ChoreCategory, Justice_Filter_AllChoreCategories, Justice_TargetUnitNote
  SharedResources.he-IL.resx  # MODIFY  Tasks 5,6 — Hebrew mirrors
ShiftManager.Tests/UnitTests/Services/
  JusticeChoreWeightingTests.cs   # NEW   Task 2,3,4,7 — weighting, category narrowing, sparkline minutes, target scaling
```

Reference harness to clone for the new test file: `ShiftManager.Tests/UnitTests/Services/JusticeServiceCategoryTests.cs` (real-SQLite `:memory:` ctor, `BusyServiceMockFactory.Real`, `ChoreService`/`OnDutyService` construction, `SeedAll`, `MakeQuery` factory).

---

## Task 1 — `JusticeQuery.ChoreCategoryId` (int?)

Add the nullable property and confirm it threads (no logic yet — Task 2/3 consume it). Null = all categories = today's behavior.

**Files:**
- `Services/JusticeViewModels.cs` — the `JusticeQuery` positional record at lines 10-19.

- [ ] **Step 1 — Write the failing test.** Add a new test file. This first test asserts the record carries the property and defaults it to null. Create `ShiftManager.Tests/UnitTests/Services/JusticeChoreWeightingTests.cs` with ONLY this compile-gating test for now (the full seeded harness is added in Step 5 of Task 2):

```csharp
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class JusticeChoreWeightingQueryShapeTests
{
    [Fact]
    public void JusticeQuery_ChoreCategoryId_DefaultsToNull()
    {
        var q = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: 1,
            PeriodStart: new DateOnly(2026, 3, 1),
            PeriodEnd: new DateOnly(2026, 3, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        q.ChoreCategoryId.Should().BeNull("the default chore-category filter is null = all categories");
    }

    [Fact]
    public void JusticeQuery_ChoreCategoryId_RoundTripsViaWith()
    {
        var q = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: 1,
            PeriodStart: new DateOnly(2026, 3, 1),
            PeriodEnd: new DateOnly(2026, 3, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany)
        { };

        var narrowed = q with { ChoreCategoryId = 42 };

        narrowed.ChoreCategoryId.Should().Be(42);
        q.ChoreCategoryId.Should().BeNull("original record is immutable; `with` produced a copy");
    }
}
```

- [ ] **Step 2 — Run, expect failure (does not compile).**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingQueryShapeTests" -- xUnit.ParallelizeTestCollections=false`
  - Expected: build error `CS0117` / `'JusticeQuery' does not contain a definition for 'ChoreCategoryId'`.

- [ ] **Step 3 — Minimal implementation.** Edit `Services/JusticeViewModels.cs`. The current record (lines 10-19) is:

```csharp
public sealed record JusticeQuery(
    JusticeScope Scope,
    int? ScopeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    JusticeWorkType WorkType,
    bool ExcludeExemptShifts,
    JusticeLevel Level,
    FairnessBasis Basis = FairnessBasis.BySize,
    int? ShiftCategoryId = null);
```

  Append the new optional parameter AFTER `ShiftCategoryId` (keep it last so existing positional call sites — e.g. `new JusticeQuery(Scope, ScopeId, start, end, WorkType, ExcludeExemptShifts, Level, Basis)` in `Analytics.cshtml.cs:250` — stay valid):

```csharp
public sealed record JusticeQuery(
    JusticeScope Scope,
    int? ScopeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    JusticeWorkType WorkType,
    bool ExcludeExemptShifts,
    JusticeLevel Level,
    FairnessBasis Basis = FairnessBasis.BySize,
    int? ShiftCategoryId = null,
    /// <summary>
    /// Optional ChoreCategory filter for Chore work-type queries. When set, chore actuals and the
    /// chore sparkline series are restricted to chores whose ChoreType.ChoreCategoryId matches.
    /// Null = all chore categories = pre-Phase-5 behavior. Ignored for Shift/OnDuty work types.
    /// </summary>
    int? ChoreCategoryId = null);
```

- [ ] **Step 4 — Run, expect pass.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingQueryShapeTests" -- xUnit.ParallelizeTestCollections=false`
  - Expected: 2 passed.

- [ ] **Step 5 — Commit.** `feat(justice): add nullable JusticeQuery.ChoreCategoryId (null = all categories)`

---

## Task 2 — `CountActualPerUserAsync` chore branch: weighted sum + category filter

Replace the chore `g.Count()` with `g.Sum(c => c.WeightMinutes)`, and apply the optional `ChoreCategoryId` filter via the `Chore.ChoreType.ChoreCategoryId` navigation.

**Files:**
- `Services/JusticeService.cs` — chore branch of `CountActualPerUserAsync`, lines 584-598.

- [ ] **Step 1 — Write the failing test.** Replace the temporary `JusticeChoreWeightingQueryShapeTests.cs` content with the full seeded harness file below (it absorbs the two Task-1 query-shape tests AND adds the weighting + category-narrowing tests). This is the canonical `JusticeChoreWeightingTests` file referenced in spec §10.4; Tasks 3 and 4 append more `[Fact]`s to it.

  Seed layout (one molecule, one company, UsersInCompany view, fully-past period so `endCap == PeriodEnd`):
  - `ChoreCategoryPhysical (Id=10)` and `ChoreCategoryComputer (Id=11)`.
  - `ChoreTypePhysical (Id=20, ChoreCategoryId=10)`, `ChoreTypeComputer (Id=21, ChoreCategoryId=11)`.
  - `U1 (Id=201)`: 2 chores, both `WeightMinutes=480` (1 Physical, 1 Computer) → equal COUNT to U2 but…
  - `U2 (Id=202)`: 2 chores, both `WeightMinutes=240` (both Physical).
  - Equal chore count (2 each) but unequal total minutes (U1=960, U2=480) → weighted Actual must differ.

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Phase 5 (Duration-weighted Fairness). Verifies the chore branches of JusticeService:
///   • Task 2: chore Actual = Σ WeightMinutes (NOT count); optional ChoreCategoryId narrows it.
///   • Task 3: chore sparkline buckets sum WeightMinutes; category filter applies.
///   • Task 4: chore Expected target is scaled by DEFAULT_CHORE_WEIGHT_MINUTES (480) so the
///     chore-equivalent target compares against weighted minutes.
///
/// Mirrors the real-SQLite harness of JusticeServiceCategoryTests.
///
/// Seed (UsersInCompany, MoleculeId=1 / CompanyId=1, fully-past period so endCap == PeriodEnd):
///   ChoreCategoryPhysical (10) → ChoreTypePhysical (20)
///   ChoreCategoryComputer (11) → ChoreTypeComputer (21)
///   U1 (201): chore Physical 480min + chore Computer 480min  → count 2, minutes 960
///   U2 (202): chore Physical 240min + chore Physical 240min  → count 2, minutes 480
/// </summary>
public class JusticeChoreWeightingTests : IDisposable
{
    private const int AreaId = 1;
    private const int MoleculeId = 1;
    private const int CompanyId = 1;

    private const int CatPhysical = 10;
    private const int CatComputer = 11;
    private const int TypePhysical = 20;
    private const int TypeComputer = 21;

    private const int U1 = 201;
    private const int U2 = 202;

    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeChoreWeightingTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();

        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        _db = new AppDbContext(options, tenantMock.Object);
        _db.Database.EnsureCreated();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", CompanyId.ToString())
        }, "TestAuth"));
        httpAccessor.Setup(x => x.HttpContext).Returns(ctx);

        var busyService = BusyServiceMockFactory.Real(_db);
        var choreService = new ChoreService(
            _db, Mock.Of<ITenantResolver>(), httpAccessor.Object, Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(), Mock.Of<ILogger<ChoreService>>(), Mock.Of<ICompanyCacheService>(),
            busyService);
        var onDutyService = new OnDutyService(
            _db, httpAccessor.Object, Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(), Mock.Of<IFeatureFlagService>(), busyService);
        var shiftAssignmentService = new Mock<IShiftAssignmentService>(MockBehavior.Strict).Object;

        _service = new JusticeService(_db, shiftAssignmentService, choreService, onDutyService);

        SeedAll();
    }

    private void SeedAll()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "Co1", DisplayName = "Co1" });

        // Chore categories (Phase 1 entity).
        _db.ChoreCategories.AddRange(
            new ChoreCategory { Id = CatPhysical, MoleculeId = MoleculeId, Name = "Physical", DisplayName = "Physical", SortOrder = 1, IsActive = true },
            new ChoreCategory { Id = CatComputer, MoleculeId = MoleculeId, Name = "Computer", DisplayName = "Computer", SortOrder = 2, IsActive = true });

        // Chore types, each linked to a category (Phase 1 ChoreType.ChoreCategoryId).
        _db.ChoreTypes.AddRange(
            new ChoreType { Id = TypePhysical, MoleculeId = MoleculeId, Name = "Phys", DisplayName = "Phys", ChoreCategoryId = CatPhysical, IsActive = true },
            new ChoreType { Id = TypeComputer, MoleculeId = MoleculeId, Name = "Comp", DisplayName = "Comp", ChoreCategoryId = CatComputer, IsActive = true });

        _db.Users.AddRange(
            new AppUser { Id = U1, Email = "u1@test.com", DisplayName = "UserOne", CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee, AccountType = AccountType.Standard },
            new AppUser { Id = U2, Email = "u2@test.com", DisplayName = "UserTwo", CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee, AccountType = AccountType.Standard });

        _db.SaveChanges();

        // Chores: equal COUNT (2 each), unequal total WeightMinutes (U1=960, U2=480).
        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U1, ChoreTypeId = TypePhysical, Date = new DateOnly(2026, 3, 10), Title = "p", WeightMinutes = 480, CreatedAt = DateTime.UtcNow },
            new Chore { Id = 2, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U1, ChoreTypeId = TypeComputer, Date = new DateOnly(2026, 3, 11), Title = "c", WeightMinutes = 480, CreatedAt = DateTime.UtcNow },
            new Chore { Id = 3, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U2, ChoreTypeId = TypePhysical, Date = new DateOnly(2026, 3, 12), Title = "p", WeightMinutes = 240, CreatedAt = DateTime.UtcNow },
            new Chore { Id = 4, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U2, ChoreTypeId = TypePhysical, Date = new DateOnly(2026, 3, 13), Title = "p", WeightMinutes = 240, CreatedAt = DateTime.UtcNow });

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private static JusticeQuery MakeChoreQuery(int? choreCategoryId = null, DateOnly? start = null, DateOnly? end = null) => new JusticeQuery(
        Scope: JusticeScope.Company,
        ScopeId: CompanyId,
        PeriodStart: start ?? PeriodStart,
        PeriodEnd: end ?? PeriodEnd,
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.UsersInCompany,
        ChoreCategoryId: choreCategoryId);

    // ---- Task 1 query-shape (kept here so the file is the single source of truth) ----

    [Fact]
    public void JusticeQuery_ChoreCategoryId_DefaultsToNull()
    {
        MakeChoreQuery().ChoreCategoryId.Should().BeNull();
    }

    [Fact]
    public void JusticeQuery_ChoreCategoryId_RoundTripsViaWith()
    {
        var q = MakeChoreQuery();
        (q with { ChoreCategoryId = 42 }).ChoreCategoryId.Should().Be(42);
        q.ChoreCategoryId.Should().BeNull();
    }

    // ---- Task 2: weighted actual ----

    [Fact]
    public async Task ChoreActual_IsSumOfWeightMinutes_NotCount()
    {
        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(), CancellationToken.None);

        var u1 = vm.Rows.Single(r => r.Id == U1);
        var u2 = vm.Rows.Single(r => r.Id == U2);

        u1.Actual.Should().Be(960m, "U1 has two 480-minute chores = 960 weighted minutes (NOT a count of 2)");
        u2.Actual.Should().Be(480m, "U2 has two 240-minute chores = 480 weighted minutes (NOT a count of 2)");
        u1.Actual.Should().NotBe(u2.Actual, "equal chore counts but unequal durations must produce unequal weighted Actual");
    }

    [Fact]
    public async Task ChoreActual_CategoryFilter_NarrowsToMatchingType()
    {
        // Filter to Physical: U1 keeps only its 1 Physical chore (480), U2 keeps both Physical chores (480).
        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(choreCategoryId: CatPhysical), CancellationToken.None);

        var u1 = vm.Rows.Single(r => r.Id == U1);
        var u2 = vm.Rows.Single(r => r.Id == U2);

        u1.Actual.Should().Be(480m, "filtering to Physical drops U1's Computer chore, leaving one 480-minute Physical chore");
        u2.Actual.Should().Be(480m, "both of U2's chores are Physical (240+240)");
    }

    [Fact]
    public async Task ChoreActual_CategoryFilter_Computer_OnlyU1HasComputerLoad()
    {
        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(choreCategoryId: CatComputer), CancellationToken.None);

        var u1 = vm.Rows.Single(r => r.Id == U1);
        var u2 = vm.Rows.Single(r => r.Id == U2);

        u1.Actual.Should().Be(480m, "U1 has one Computer chore worth 480 minutes");
        u2.Actual.Should().Be(0m, "U2 has no Computer chores");
    }
}
```

- [ ] **Step 2 — Run, expect failure.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingTests" -- xUnit.ParallelizeTestCollections=false`
  - Expected: `ChoreActual_IsSumOfWeightMinutes_NotCount` FAILS (`u1.Actual` is `2`, expected `960`), and the two category tests FAIL (filter not applied — both users still counted). Query-shape tests pass.

- [ ] **Step 3 — Minimal implementation.** Edit the chore branch of `CountActualPerUserAsync`. The current code (lines 584-598) is:

```csharp
        if (q.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics views.
            var choreRows = await _db.Chores
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.CompanyId)
                            && c.CanceledAt == null
                            && c.Date >= q.PeriodStart
                            && c.Date <= endCap)
                .GroupBy(c => c.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var r in choreRows)
                byUser[r.UserId] = byUser.GetValueOrDefault(r.UserId, 0m) + r.Count;
        }
```

  Replace it with the weighted version + optional category filter. `Sum(c => c.WeightMinutes)` translates to SQL `SUM(WeightMinutes)` (an `int` sum); cast to `decimal` in-memory to match the `byUser` accumulator. The category filter narrows on `c.ChoreType!.ChoreCategoryId == catId` (free-text chores have null `ChoreTypeId` → excluded by a category filter, which is correct: a category-narrowed view shows only typed chores in that category):

```csharp
        if (q.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics views.
            // Phase 5: chore fairness is DURATION-WEIGHTED — Actual = Σ WeightMinutes, not a count.
            var choreQ = _db.Chores
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.CompanyId)
                            && c.CanceledAt == null
                            && c.Date >= q.PeriodStart
                            && c.Date <= endCap);
            // Phase 5: optional ChoreCategory narrowing. Restricts to chores whose type belongs to the
            // category. Free-text (null ChoreTypeId) chores are excluded by a category filter by design.
            if (q.ChoreCategoryId is int choreCatId)
            {
                choreQ = choreQ.Where(c => c.ChoreType != null && c.ChoreType.ChoreCategoryId == choreCatId);
            }
            var choreRows = await choreQ
                .GroupBy(c => c.UserId)
                .Select(g => new { UserId = g.Key, Minutes = g.Sum(c => c.WeightMinutes) })
                .ToListAsync(ct);
            foreach (var r in choreRows)
                byUser[r.UserId] = byUser.GetValueOrDefault(r.UserId, 0m) + r.Minutes;
        }
```

  Note: `byUser.GetValueOrDefault(r.UserId, 0m) + r.Minutes` — `r.Minutes` is `int`, promoted to `decimal` in the `+` with the `0m` seed; the accumulator stays `decimal`.

- [ ] **Step 4 — Run, expect pass.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingTests" -- xUnit.ParallelizeTestCollections=false`
  - Expected: all weighting + category tests pass.

- [ ] **Step 5 — Regression guard.** Run the full Justice suite to confirm shift/on-duty/category paths are unchanged:
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~Justice" -- xUnit.ParallelizeTestCollections=false`
  - Expected: all green. Pay attention to `JusticeServiceCategoryTests` (shift category filter) and `JusticeServiceChoreOnDutyEligibilityTests` — they must remain unaffected.

- [ ] **Step 6 — Commit.** `feat(justice): duration-weighted chore Actual + ChoreCategory filter in CountActualPerUserAsync`

---

## Task 3 — `GetSparklineSeriesAsync` chore branch: bucket by Σ WeightMinutes + category filter

The sparkline currently pulls `(UserId, Date)` pairs and increments each matched bucket by `1` (`arr[bi]++`). Switch to pulling `(UserId, Date, WeightMinutes)` and adding `WeightMinutes` to the bucket, and apply the same optional category filter.

**Files:**
- `Services/JusticeService.cs` — chore branch of `GetSparklineSeriesAsync`, lines 848-867.

- [ ] **Step 1 — Write the failing test.** Append to `JusticeChoreWeightingTests`. The sparkline buckets by calendar month; the seed's chores are all in 2026-03, so they land in a single bucket. Asserting the last bucket for U1 = 960 and U2 = 480 confirms minutes, not count (count would be 2). `GetSparklineSeriesAsync` is a public method returning `Dictionary<int, List<decimal>>`.

```csharp
    // ---- Task 3: sparkline minutes ----

    [Fact]
    public async Task ChoreSparkline_BucketsSumWeightMinutes_NotCount()
    {
        // 1 bucket ending in the PeriodEnd month (2026-03). All seeded chores fall in March.
        var series = await _service.GetSparklineSeriesAsync(MakeChoreQuery(), buckets: 1, CancellationToken.None);

        series.Should().ContainKey(U1);
        series.Should().ContainKey(U2);
        series[U1].Last().Should().Be(960m, "U1's single bucket sums two 480-minute chores (NOT a count of 2)");
        series[U2].Last().Should().Be(480m, "U2's single bucket sums two 240-minute chores (NOT a count of 2)");
    }

    [Fact]
    public async Task ChoreSparkline_CategoryFilter_NarrowsBucketMinutes()
    {
        var series = await _service.GetSparklineSeriesAsync(MakeChoreQuery(choreCategoryId: CatComputer), buckets: 1, CancellationToken.None);

        series[U1].Last().Should().Be(480m, "only U1's single Computer chore (480) counts under the Computer filter");
        series[U2].Last().Should().Be(0m, "U2 has no Computer chores");
    }
```

- [ ] **Step 2 — Run, expect failure.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingTests.ChoreSparkline" -- xUnit.ParallelizeTestCollections=false`
  - Expected: `ChoreSparkline_BucketsSumWeightMinutes_NotCount` FAILS (`series[U1].Last()` is `2`, expected `960`); category test FAILS (480 vs filtered).

- [ ] **Step 3 — Minimal implementation.** Edit the chore branch of `GetSparklineSeriesAsync`. The current code (lines 848-867) is:

```csharp
        if (baseQuery.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics; scope-gated upstream.
            var choreRows = await _db.Chores
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.CompanyId)
                            && c.CanceledAt == null
                            && c.Date >= firstBucketStart
                            && c.Date <= spanEnd)
                .Select(c => new { c.UserId, c.Date })
                .ToListAsync(ct);

            foreach (var r in choreRows)
            {
                if (!userToRowKey.TryGetValue(r.UserId, out var rk)) continue;
                if (!accum.TryGetValue(rk, out var arr)) continue;
                int bi = BucketIndex(r.Date);
                if (bi >= 0) arr[bi]++;
            }
        }
```

  Replace with the weighted, category-filtered version. The bucket array is `decimal[]`, so `arr[bi] += r.WeightMinutes` promotes the `int` to `decimal` cleanly:

```csharp
        if (baseQuery.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics; scope-gated upstream.
            // Phase 5: sparkline buckets accumulate Σ WeightMinutes (duration-weighted), not chore counts.
            var choreQ = _db.Chores
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.CompanyId)
                            && c.CanceledAt == null
                            && c.Date >= firstBucketStart
                            && c.Date <= spanEnd);
            // Phase 5: same optional ChoreCategory narrowing as CountActualPerUserAsync.
            if (baseQuery.ChoreCategoryId is int choreCatId)
            {
                choreQ = choreQ.Where(c => c.ChoreType != null && c.ChoreType.ChoreCategoryId == choreCatId);
            }
            var choreRows = await choreQ
                .Select(c => new { c.UserId, c.Date, c.WeightMinutes })
                .ToListAsync(ct);

            foreach (var r in choreRows)
            {
                if (!userToRowKey.TryGetValue(r.UserId, out var rk)) continue;
                if (!accum.TryGetValue(rk, out var arr)) continue;
                int bi = BucketIndex(r.Date);
                if (bi >= 0) arr[bi] += r.WeightMinutes;
            }
        }
```

- [ ] **Step 4 — Run, expect pass.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingTests" -- xUnit.ParallelizeTestCollections=false`
  - Expected: all green (Task 2 + Task 3 facts).

- [ ] **Step 5 — Commit.** `feat(justice): chore sparkline buckets sum WeightMinutes + honor ChoreCategory filter`

---

## Task 4 — Chore target scaling (×480) so expected matches weighted actual

Chore targets are stored in **chore-equivalents** (e.g. "3 chores per month"). Now that Actual is in weighted minutes, the chore Expected must be scaled by `DEFAULT_CHORE_WEIGHT_MINUTES = 480` so the units line up: a target of `3/period` → `3 × 480 = 1440` weighted minutes. The two resolution sites are `ResolvePerUserChoreOrOnDuty` (per-user, both override and global-default branches) and `ResolveSingleWorkTypeExpected` (company/molecule rollup). **OnDuty must NOT be scaled** — only the `JusticeWorkType.Chore` paths.

**Files:**
- `Services/JusticeService.cs` — add the const near lines 35-37; edit `ResolvePerUserChoreOrOnDuty` (284-300) and `ResolveSingleWorkTypeExpected` (922-944).

- [ ] **Step 1 — Write the failing test.** Append to `JusticeChoreWeightingTests`. Seed a global per-user chore target and assert the per-user Expected is `target × PeriodMultiplier × 480`. To keep the multiplier exact, use a `PerMonth` target and a period of exactly the average month length is awkward; instead assert the **ratio** Expected/(target×multiplier) == 480 by reading two rows, OR seed a `PerMonth` target and compute the expected multiplier in the test the same way the service does. Simplest robust assertion: seed a single global chore target of `ExpectedCount=1, PeriodKind=PerMonth`, then Expected = `1 × (days/30.4375) × 480`. Compute that in the test.

```csharp
    // ---- Task 4: chore target scaling (×480) ----

    [Fact]
    public async Task ChoreExpected_IsScaledBy480_SoUnitsMatchWeightedActual()
    {
        // Global per-user chore target: 1 chore-equivalent per month.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 500,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth
        });
        _db.SaveChanges();

        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(), CancellationToken.None);

        // PeriodMultiplier = inclusive-days / 30.4375. The seed period is 2026-03-01..2026-03-31 = 31 days.
        const decimal daysPerMonth = 30.4375m;
        var inclusiveDays = (decimal)(PeriodEnd.DayNumber - PeriodStart.DayNumber + 1); // 31
        var multiplier = inclusiveDays / daysPerMonth;
        var expectedMinutes = 1m * multiplier * 480m;

        var u1 = vm.Rows.Single(r => r.Id == U1);
        u1.Expected.Should().BeApproximately(expectedMinutes, 0.001m,
            "chore Expected = target(1) × periodMultiplier × DEFAULT_CHORE_WEIGHT_MINUTES(480), matching weighted-minute Actual units");
    }
```

  Note: confirm the `JusticeTarget` property names (`WorkType`, `ScopeKind`, `ScopeId`, `ExpectedCount`, `PeriodKind`) against `Models/JusticeTarget.cs` — they are used verbatim in `ResolvePerUserChoreOrOnDuty`. If a property differs, adjust the seed; do not change the production resolver to fit the test.

- [ ] **Step 2 — Run, expect failure.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingTests.ChoreExpected_IsScaledBy480" -- xUnit.ParallelizeTestCollections=false`
  - Expected: FAILS — Expected is `multiplier` (un-scaled, ≈1.018) not `multiplier × 480` (≈488.8).

- [ ] **Step 3 — Minimal implementation.**

  3a. Add the constant alongside the existing day-conversion consts (after line 37, `private const decimal DaysPerQuarter = 91.3125m;`):

```csharp
    // Phase 5: chore targets are stored in CHORE-EQUIVALENTS; chore Actual is in WEIGHTED MINUTES.
    // Scale a chore target by this fixed per-chore weight (8h) so the two compare in the same units.
    // Must match the Phase-1 Chore.WeightMinutes fallback (DEFAULT_CHORE_WEIGHT_MINUTES = 480).
    private const decimal ChoreWeightMinutesPerEquivalent = 480m;
```

  3b. Scale in `ResolvePerUserChoreOrOnDuty` (284-300). The method handles BOTH chore and on-duty (`wt` parameter), so scale **only when `wt == JusticeWorkType.Chore`**. Current body:

```csharp
    private decimal ResolvePerUserChoreOrOnDuty(JusticeQuery q, JusticeWorkType wt, int companyId, int headcount, List<JusticeTarget> targets)
    {
        // Company-scope override wins.
        var compOverride = targets.FirstOrDefault(t =>
            t.WorkType == wt && t.ScopeKind == JusticeScope.Company && t.ScopeId == companyId);
        if (compOverride != null && headcount > 0)
        {
            var totalForCompany = compOverride.ExpectedCount * PeriodMultiplier(q, compOverride.PeriodKind);
            return totalForCompany / headcount;
        }

        // Else: per-user global default applied directly (already a per-user number).
        var globalTarget = targets.FirstOrDefault(t =>
            t.WorkType == wt && t.ScopeKind == JusticeScope.Global);
        if (globalTarget == null) return 0m;
        return globalTarget.ExpectedCount * PeriodMultiplier(q, globalTarget.PeriodKind);
    }
```

  Replace with a version that multiplies the chore result by the weight scalar via a local helper:

```csharp
    private decimal ResolvePerUserChoreOrOnDuty(JusticeQuery q, JusticeWorkType wt, int companyId, int headcount, List<JusticeTarget> targets)
    {
        // Phase 5: chore targets (chore-equivalents) are scaled to weighted minutes; on-duty is not.
        decimal scale = wt == JusticeWorkType.Chore ? ChoreWeightMinutesPerEquivalent : 1m;

        // Company-scope override wins.
        var compOverride = targets.FirstOrDefault(t =>
            t.WorkType == wt && t.ScopeKind == JusticeScope.Company && t.ScopeId == companyId);
        if (compOverride != null && headcount > 0)
        {
            var totalForCompany = compOverride.ExpectedCount * PeriodMultiplier(q, compOverride.PeriodKind);
            return totalForCompany / headcount * scale;
        }

        // Else: per-user global default applied directly (already a per-user number).
        var globalTarget = targets.FirstOrDefault(t =>
            t.WorkType == wt && t.ScopeKind == JusticeScope.Global);
        if (globalTarget == null) return 0m;
        return globalTarget.ExpectedCount * PeriodMultiplier(q, globalTarget.PeriodKind) * scale;
    }
```

  3c. Scale in `ResolveSingleWorkTypeExpected` (922-944) — the company/molecule rollup path. This method already special-cases `JusticeWorkType.Shift` (returns capacity) at the top, so by the time we reach the target resolution `workType` is Chore or OnDuty. Scale only for Chore. Current body:

```csharp
    private decimal ResolveSingleWorkTypeExpected(JusticeWorkType workType, JusticeQuery q, JusticeScope scopeKind, int scopeId, int headcount, decimal shiftCapacity, List<JusticeTarget> targets)
    {
        if (workType == JusticeWorkType.Shift)
        {
            // Capacity-driven: ignore targets table.
            return shiftCapacity;
        }

        // Look for explicit override at this scope.
        var explicitTarget = targets.FirstOrDefault(t =>
            t.WorkType == workType && t.ScopeKind == scopeKind && t.ScopeId == scopeId);
        if (explicitTarget != null)
        {
            return explicitTarget.ExpectedCount * PeriodMultiplier(q, explicitTarget.PeriodKind);
        }

        // Fall through to global per-user default.
        var globalTarget = targets.FirstOrDefault(t =>
            t.WorkType == workType && t.ScopeKind == JusticeScope.Global);
        if (globalTarget == null) return 0m;

        return globalTarget.ExpectedCount * headcount * PeriodMultiplier(q, globalTarget.PeriodKind);
    }
```

  Replace the two target-return lines with weight-scaled versions:

```csharp
    private decimal ResolveSingleWorkTypeExpected(JusticeWorkType workType, JusticeQuery q, JusticeScope scopeKind, int scopeId, int headcount, decimal shiftCapacity, List<JusticeTarget> targets)
    {
        if (workType == JusticeWorkType.Shift)
        {
            // Capacity-driven: ignore targets table.
            return shiftCapacity;
        }

        // Phase 5: chore targets (chore-equivalents) scale to weighted minutes; on-duty is not scaled.
        decimal scale = workType == JusticeWorkType.Chore ? ChoreWeightMinutesPerEquivalent : 1m;

        // Look for explicit override at this scope.
        var explicitTarget = targets.FirstOrDefault(t =>
            t.WorkType == workType && t.ScopeKind == scopeKind && t.ScopeId == scopeId);
        if (explicitTarget != null)
        {
            return explicitTarget.ExpectedCount * PeriodMultiplier(q, explicitTarget.PeriodKind) * scale;
        }

        // Fall through to global per-user default.
        var globalTarget = targets.FirstOrDefault(t =>
            t.WorkType == workType && t.ScopeKind == JusticeScope.Global);
        if (globalTarget == null) return 0m;

        return globalTarget.ExpectedCount * headcount * PeriodMultiplier(q, globalTarget.PeriodKind) * scale;
    }
```

- [ ] **Step 4 — Run, expect pass.**
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~JusticeChoreWeightingTests" -- xUnit.ParallelizeTestCollections=false`
  - Expected: all green.

- [ ] **Step 5 — Regression guard.** The chore-target scaling touches shared resolution methods. Re-run the full Justice suite and watch the on-duty + "All" work-type expected tests (which exercise the same methods) stay green:
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~Justice" -- xUnit.ParallelizeTestCollections=false`
  - Expected: all green. If any pre-existing chore-expected test now fails, it likely asserted un-scaled chore expected — that test encoded the OLD (count-based) contract and must be updated to the weighted contract as part of THIS task (note it explicitly in the commit body); do NOT revert the scaling.

- [ ] **Step 6 — Commit.** `feat(justice): scale chore Expected targets by 480 to match weighted-minute Actual`

---

## Task 5 — Drawer: `choreCategoryId` param + category `<select>` + hours display

The Chores-calendar drawer (`OnGetJusticeAsync`) builds a `JusticeQuery` with `WorkType: Chore`. Add an optional `int? choreCategoryId` bound from the query string, pass it into the `JusticeQuery`, expose the molecule's active chore categories for a `<select>`, and have `justice-panel.js` render chore actual/expected as hours.

**Files:**
- `Pages/Calendar/Chores.cshtml.cs` — `OnGetJusticeAsync` (537-568) + `BuildJusticeJson` (570-595); add a bound `ChoreCategoryFilter` and a category-options loader.
- `Pages/Calendar/Chores.cshtml` — add the category `<select>` near the Justice trigger.
- `wwwroot/js/justice-panel.js` — `renderEquityRibbon`/`renderCallouts` already use raw numbers; add an hours formatter and apply it to chore rows/actuals. The drawer reads `data.query.workType`.
- `Resources/SharedResources.resx` + `.he-IL.resx` — `Justice_Filter_ChoreCategory`, `Justice_Filter_AllChoreCategories`.

- [ ] **Step 1 — Write the failing test.** This task's surface is a Razor PageModel handler. Add a focused handler test that calls `OnGetJusticeAsync` with a `choreCategoryId` and asserts the JSON it returns reflects the narrowed weighted actual. Create `ShiftManager.Tests/UnitTests/Pages/ChoresJusticeDrawerCategoryTests.cs`. (If the repo has no existing `UnitTests/Pages` PageModel test harness to mirror, prefer instead a service-level assertion already covered by Task 2 and convert this step into a thin handler test using the existing `ChoresModel` construction pattern found in any `Pages` test; search `ShiftManager.Tests` for `new ChoresModel(` first. If no PageModel test infrastructure exists, mark Step 1-4 of THIS task as covered by the Task-2 service tests + a manual browser check in Step 7, and say so explicitly in the commit body — do not fabricate a harness.)

  Minimal handler-shape test (only if `ChoresModel` is constructible in tests):

```csharp
// Asserts OnGetJusticeAsync accepts a choreCategoryId and threads it into the JusticeQuery.
// Construction mirrors the ctor in Pages/Calendar/Chores.cshtml.cs.
[Fact]
public async Task OnGetJustice_WithChoreCategoryId_ThreadsFilterIntoQuery()
{
    // Arrange a JusticeService spy/mock capturing the JusticeQuery, seed MoleculeId + grant.
    // Assert captured query.ChoreCategoryId == provided value and WorkType == Chore.
}
```

  If a PageModel harness is impractical, the binding is verified end-to-end by Step 7's browser check; the *query threading* is structurally guaranteed because the handler passes the bound property straight into the record (verified by code review in Step 3). State this choice in the commit body.

- [ ] **Step 2 — Run, expect failure** (only if you wrote a real test in Step 1). Otherwise skip to Step 3.
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoresJusticeDrawerCategoryTests" -- xUnit.ParallelizeTestCollections=false`

- [ ] **Step 3 — Minimal implementation (PageModel).**

  3a. Add a bound property near the other `[BindProperty(SupportsGet = true)]` declarations (after `ChoreTypeFilter`, line 71):

```csharp
    [BindProperty(SupportsGet = true)]
    public int? ChoreCategoryFilter { get; set; }
```

  3b. In `OnGetJusticeAsync` (537), after the grant check and before building the query, thread the filter and load the category list for the view. The current query construction (557-564) is:

```csharp
        var query = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: MoleculeId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,                  // chores have no exempt analogue
            Level: JusticeLevel.CompaniesInMolecule);
```

  Add `ChoreCategoryId: ChoreCategoryFilter` as the trailing argument:

```csharp
        var query = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: MoleculeId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,                  // chores have no exempt analogue
            Level: JusticeLevel.CompaniesInMolecule,
            ChoreCategoryId: ChoreCategoryFilter);
```

  3c. In `BuildJusticeJson` (570-595), surface the active workType (already present as `view.Query.WorkType.ToString()` = "Chore") so the JS knows to format as hours — it already serializes `query.workType`. No change strictly required there for the unit math, but ALSO emit the filter echo so the selector can render its current value. Add to the `query` anonymous object:

```csharp
                workType = view.Query.WorkType.ToString(),
                choreCategoryId = view.Query.ChoreCategoryId,
```

  3d. Populate the page's chore-category list for the `<select>`. In the main `OnGetAsync` flow (where `ChoreTypes`/`SelectedMolecule` are already loaded — search for `ChoreTypes = ` in this file), add a sibling list. Add a page property near `public List<ChoreType> ChoreTypes` (line 85):

```csharp
    public List<ChoreCategory> ChoreCategories { get; set; } = new();
```

  And load it wherever `ChoreTypes` is loaded for `SelectedMolecule` (molecule-scoped, not tenant-filtered — same posture as `ChoreTypes`):

```csharp
        if (SelectedMolecule != null)
        {
            ChoreCategories = await _db.ChoreCategories
                .Where(cc => cc.MoleculeId == SelectedMolecule.Id && cc.IsActive)
                .OrderBy(cc => cc.SortOrder)
                .ToListAsync();
        }
```

  (Place this adjacent to the existing `ChoreTypes` load; reuse the same `SelectedMolecule` guard already there.)

- [ ] **Step 4 — Minimal implementation (Razor).** In `Pages/Calendar/Chores.cshtml`, near the Justice trigger button (search `data-justice-trigger`), add a category `<select>` that reloads with `?choreCategoryFilter=`. Mirror the ShiftCategory selector pattern from `Analytics.cshtml` (130-155). Render only when `Model.ChoreCategories.Count > 0`:

```html
@if (Model.ChoreCategories.Count > 0)
{
    <div class="filter-field">
        <label class="filter-label" for="chore-cat-select"><loc key="Justice_Filter_ChoreCategory" /></label>
        <select id="chore-cat-select" class="form-select"
                onchange="(function(sel){ var url = new URL(window.location.href); if (sel.value) { url.searchParams.set('choreCategoryFilter', sel.value); } else { url.searchParams.delete('choreCategoryFilter'); } window.location.href = url.toString(); })(this)">
            <option value="" selected="@(Model.ChoreCategoryFilter == null)">@Localizer["Justice_Filter_AllChoreCategories"]</option>
            @foreach (var cat in Model.ChoreCategories)
            {
                <option value="@cat.Id" selected="@(Model.ChoreCategoryFilter == cat.Id)">@cat.DisplayName</option>
            }
        </select>
    </div>
}
```

  Note: the drawer fetches its data from `OnGetJusticeAsync` via `data-justice-endpoint`. So the endpoint URL on the trigger must carry `choreCategoryFilter` too. Find where `data-justice-endpoint` is built (search the cshtml) and append `&choreCategoryFilter=@Model.ChoreCategoryFilter` when set, so the drawer's fetch is narrowed consistently with the page selector.

- [ ] **Step 5 — Minimal implementation (JS hours display).** In `wwwroot/js/justice-panel.js`, add an hours formatter and apply it to chore actual/expected. The payload's `data.query.workType === 'Chore'` gates it. Add a helper near `formatDeviation` (686):

```javascript
    /** Phase 5: format weighted minutes as hours for chore work-type rows. e.g. 960 → "16h". */
    function formatHoursFromMinutes(minutes) {
        var m = Number(minutes) || 0;
        var h = m / 60;
        // 1 decimal, trim trailing ".0".
        var s = (Math.round(h * 10) / 10).toString();
        return s + 'h';
    }
    function isChoreWorkType(data) {
        return data && data.query && data.query.workType === 'Chore';
    }
```

  Then in `renderCallouts` (199) the mostOver/mostUnder render names + deviation% (no absolute value) → no change needed (deviation is unit-agnostic). The absolute load shows in the equity ribbon title and the focus list. Update `renderEquityRibbon` (210) so the hover title shows hours for chores. Current title line:

```javascript
            var name = (r.name || '') + ' ' + (r.actual || 0);
```

  becomes:

```javascript
            var actualLabel = isChoreWorkType(data) ? formatHoursFromMinutes(r.actual) : (r.actual || 0);
            var name = (r.name || '') + ' ' + actualLabel;
```

  (Pass `data` into `renderEquityRibbon` — it is already called as `renderEquityRibbon(data)` at line 162, so `data` is in scope.)

  The candidate-preview "Actual N → N+1" path (`renderPreview`, 468) is shift/onduty-oriented and shows counts; chore preview adds +1 unit which is a chore-count semantic, not minutes — leave the preview as-is (deferred, see Deferred Items) to avoid changing the what-if simulator's chore +1 semantics in this fairness-only phase.

- [ ] **Step 6 — Add resx keys.** Add to `Resources/SharedResources.resx` (English) and `Resources/SharedResources.he-IL.resx` (Hebrew). **Grep each key first** (`Justice_Filter_ChoreCategory`, `Justice_Filter_AllChoreCategories`) to avoid the duplicate-key localization-test break noted in project memory.
  - `Justice_Filter_ChoreCategory` → EN "Chore Category" / HE "קטגוריית מטלה"
  - `Justice_Filter_AllChoreCategories` → EN "All Categories" / HE "כל הקטגוריות"

- [ ] **Step 7 — Verify.** Build, run the app, open `/Calendar/Chores`, open the Justice drawer, pick a chore category, confirm: (a) the rows/ribbon narrow to that category, (b) the URL gains `choreCategoryFilter`, (c) absolute load hovers read in hours. Then run the full localization test suite to catch missing/duplicate keys:
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~Localization" -- xUnit.ParallelizeTestCollections=false`

- [ ] **Step 8 — Commit.** `feat(chores-drawer): chore-category filter + hours-formatted load in Justice drawer`

---

## Task 6 — Standalone `/Admin/Analytics`: chore-category selector + hours display

Add a `choreCategoryId` query param to `AnalyticsModel`, populate chore-category options (shown only when `WorkType==Chore`), thread it into the `JusticeQuery`, and render the absolute Actual/Expected columns as hours when `WorkType==Chore`.

**Files:**
- `Pages/Admin/Analytics.cshtml.cs` — add `ChoreCategoryId` bind (mirror `ShiftCategoryId`, line 69); add `ChoreCategoryOptions` + `PopulateChoreCategoryOptionsAsync`; thread into the query (181-190) and CSV query (250); add hidden field carry.
- `Pages/Admin/Analytics.cshtml` — add a chore-category `<select>` parallel to the ShiftCategory one (130-155), gated on `Model.WorkType == JusticeWorkType.Chore`; render Actual (788) and Expected (805) as hours when chore.
- `Resources/*.resx` — reuse the Task-5 keys; add `Justice_TargetUnitNote` (the "targets in chore-equivalents, load in weighted hours" clarifier from spec §7.6).

- [ ] **Step 1 — Write the failing test.** Add a PageModel test asserting `PopulateChoreCategoryOptionsAsync` fills options only for a chore molecule scope, and that the query carries `ChoreCategoryId`. If the repo lacks an `AnalyticsModel` test harness, mirror the structure of any existing `Pages/Admin` PageModel test; search `ShiftManager.Tests` for `new AnalyticsModel(`. If none exists, gate this on the service-level Task-2 coverage + Step 6 browser verification and state so in the commit (same policy as Task 5 Step 1). Prefer a real test when a harness exists:

```csharp
[Fact]
public async Task ChoreCategoryOptions_PopulatedForChoreWorkType_OnMoleculeScope()
{
    // Seed molecule with two active ChoreCategories; set WorkType=Chore, Level=UsersInMolecule.
    // Call OnGetAsync; assert Model.ChoreCategoryOptions has 2 entries ordered by SortOrder.
}
```

- [ ] **Step 2 — Run, expect failure** (if a real test was written).
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~AnalyticsChoreCategory" -- xUnit.ParallelizeTestCollections=false`

- [ ] **Step 3 — Minimal implementation (PageModel).**

  3a. Add the bind property after `ShiftCategoryId` (line 69):

```csharp
    /// <summary>
    /// Optional ChoreCategory filter. Meaningful only for WorkType=Chore. When set, chore actuals
    /// and the chore sparkline are restricted to chores whose type belongs to this category.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "choreCategoryId")] public int? ChoreCategoryId { get; set; }
```

  3b. Add the options surface near `CategoryOptions` (118):

```csharp
    /// <summary>
    /// Chore categories available for filtering when WorkType=Chore. Populated from the active
    /// molecule's ChoreCategories. Empty when no molecule can be determined or WorkType != Chore.
    /// </summary>
    public List<ScopeOption> ChoreCategoryOptions { get; private set; } = new();
```

  3c. Add the loader, mirroring `PopulateCategoryOptionsAsync` (360-388) but reading `ChoreCategories` and gating on `WorkType == JusticeWorkType.Chore`:

```csharp
    private async Task PopulateChoreCategoryOptionsAsync(CancellationToken ct)
    {
        if (ScopeId is null || WorkType != JusticeWorkType.Chore) return;

        int? moleculeId = null;
        if (Level == JusticeLevel.UsersInMolecule || Level == JusticeLevel.CompaniesInMolecule)
        {
            moleculeId = ScopeId;
        }
        else if (Level == JusticeLevel.UsersInCompany)
        {
            // SECURITY: IgnoreQueryFilters required — Justice viewer may span tenants.
            moleculeId = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id == ScopeId.Value)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync(ct);
        }

        if (moleculeId is null) return;

        // SECURITY: ChoreCategory is molecule-scoped (no IBelongsToCompany); no tenant filter to bypass.
        ChoreCategoryOptions = await _db.ChoreCategories
            .Where(cc => cc.MoleculeId == moleculeId.Value && cc.IsActive)
            .OrderBy(cc => cc.SortOrder)
            .Select(cc => new ScopeOption(cc.Id, cc.DisplayName))
            .ToListAsync(ct);
    }
```

  3d. Call it right after `await PopulateCategoryOptionsAsync(ct);` (179):

```csharp
        await PopulateCategoryOptionsAsync(ct);
        await PopulateChoreCategoryOptionsAsync(ct);
```

  3e. Thread into the main query (181-190): add `ChoreCategoryId: ChoreCategoryId` as the trailing argument:

```csharp
        var query = new JusticeQuery(
            Scope: Scope,
            ScopeId: ScopeId,
            PeriodStart: EffectivePeriodStart,
            PeriodEnd: EffectivePeriodEnd,
            WorkType: WorkType,
            ExcludeExemptShifts: ExcludeExemptShifts,
            Level: Level,
            Basis: Basis,
            ShiftCategoryId: ShiftCategoryId,
            ChoreCategoryId: ChoreCategoryId);
```

  3f. Thread into the CSV query (250). The current positional call is `new JusticeQuery(Scope, ScopeId, start, end, WorkType, ExcludeExemptShifts, Level, Basis)`. Extend to pass the filters (positional order: …, Basis, ShiftCategoryId, ChoreCategoryId):

```csharp
        var query = new JusticeQuery(Scope, ScopeId, start, end, WorkType, ExcludeExemptShifts, Level, Basis, ShiftCategoryId, ChoreCategoryId);
```

- [ ] **Step 4 — Minimal implementation (Razor selector + hours).**

  4a. In `Analytics.cshtml`, add a chore-category selector parallel to the ShiftCategory block (after the block ending ~155). Gate on chore work-type AND options present:

```html
@if (Model.WorkType == JusticeWorkType.Chore && Model.ChoreCategoryOptions.Count > 0)
{
    <div class="filter-field" style="flex:0 0 auto;">
        <label class="filter-label" for="chore-cat-select"><loc key="Justice_Filter_ChoreCategory" /></label>
        <select id="chore-cat-select" class="form-select" style="min-width:8rem;"
                onchange="(function(sel){ var url = new URL(window.location.href); if (sel.value) { url.searchParams.set('choreCategoryId', sel.value); } else { url.searchParams.delete('choreCategoryId'); } window.location.href = url.toString(); })(this)">
            <option value="" selected="@(Model.ChoreCategoryId == null)">@Localizer["Justice_Filter_AllChoreCategories"]</option>
            @foreach (var cat in Model.ChoreCategoryOptions)
            {
                <option value="@cat.Id" selected="@(Model.ChoreCategoryId == cat.Id)">@cat.Name</option>
            }
        </select>
    </div>
}
```

  4b. Carry the param through the period-submit hidden fields (parallel to the ShiftCategory hidden at 218-221):

```html
@if (Model.ChoreCategoryId.HasValue)
{
    <input type="hidden" name="choreCategoryId" value="@Model.ChoreCategoryId" />
}
```

  4c. Hours-format the absolute Actual + Expected columns when chore. Add a helper at the top `@functions`/`@{}` block of the cshtml (search for existing `FormatDeviation` local function — put it beside it):

```csharp
@functions {
    // Phase 5: chore Actual/Expected are weighted minutes — show as hours via the shared helper.
    string FormatChoreOrRaw(decimal value, JusticeWorkType wt)
        => wt == JusticeWorkType.Chore
            ? ShiftManager.Services.DurationFormat.FormatHours((int)System.Math.Round(value))
            : value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
}
```

  Then update the Actual cell (788) from:

```html
                <td class="num" dir="ltr">@row.Actual.ToString("F0")</td>
```
  to:
```html
                <td class="num" dir="ltr">@FormatChoreOrRaw(row.Actual, Model.WorkType)</td>
```

  And the Expected cell (805) from:

```html
                    @activeExp.ToString("F1", CultureInfo.InvariantCulture)
```
  to:
```html
                    @(Model.WorkType == JusticeWorkType.Chore
                        ? ShiftManager.Services.DurationFormat.FormatHours((int)System.Math.Round(activeExp))
                        : activeExp.ToString("F1", CultureInfo.InvariantCulture))
```

  (Leave the `data-exp-bysize` / `data-exp-equal` raw attributes as-is — they drive the JS basis toggle and must stay numeric; only the human-visible text becomes hours. The JS basis-toggle that swaps `textContent` from those attributes will show raw minutes after a toggle for chores — note this in Deferred Items, since wiring the JS toggle to re-format hours is a separate UI follow-up.)

  4d. Add the target-unit clarifier note near the table header. Add a single localized note line where the basis tag is explained (search `basisTagTitle`), shown only for chore work-type:

```html
@if (Model.WorkType == JusticeWorkType.Chore)
{
    <p class="text-subtle" style="margin:.25rem 0;"><loc key="Justice_TargetUnitNote" /></p>
}
```

- [ ] **Step 5 — Add resx key.** Add `Justice_TargetUnitNote` to both resx files (grep first):
  - EN: "Targets are set in chore-equivalents; load is shown in weighted hours."
  - HE: "יעדים נקבעים ביחידות מטלה; העומס מוצג בשעות משוקללות."

- [ ] **Step 6 — Verify.** Build, run, open `/Admin/Analytics?workType=Chore` at a molecule/user level, confirm: the chore-category selector appears only for `workType=Chore`, narrows actuals, the URL carries `choreCategoryId`, and Actual/Expected columns read in hours. Confirm the selector is HIDDEN for `workType=Shift`/`All`. Run localization tests:
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~Localization" -- xUnit.ParallelizeTestCollections=false`

- [ ] **Step 7 — Commit.** `feat(analytics): chore-category filter + hours-formatted chore load on /Admin/Analytics`

---

## Task 7 — Consolidate + full regression run

`JusticeChoreWeightingTests` (weighting, category narrowing, target scaling, sparkline minutes) is the spec §10.4 suite and is built incrementally across Tasks 2-4. This task is the final gate.

**Files:** none new — verification only.

- [ ] **Step 1 — Confirm coverage.** Re-read `JusticeChoreWeightingTests.cs` and confirm it asserts all four spec §10.4 properties:
  - equal counts / unequal duration → unequal weighted Actual (`ChoreActual_IsSumOfWeightMinutes_NotCount`),
  - sparkline sums minutes (`ChoreSparkline_BucketsSumWeightMinutes_NotCount`),
  - category filter narrows (`ChoreActual_CategoryFilter_*`, `ChoreSparkline_CategoryFilter_*`),
  - target scaling matches (`ChoreExpected_IsScaledBy480_*`).
  If any is missing, add it before proceeding.

- [ ] **Step 2 — Full sequential suite.** Run the ENTIRE test suite sequentially (the project's `:memory:` contention rule):
  - `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -- xUnit.ParallelizeTestCollections=false`
  - Expected: 100% green. The pre-change baseline was 1587/1587 (per project memory); after this phase the count rises by the new `JusticeChoreWeightingTests` facts (and any PageModel tests). No previously-green test may regress except any chore-expected test intentionally re-baselined to the weighted contract in Task 4 Step 5 (which must already be green by now).

- [ ] **Step 3 — Commit (if Step 1 added tests).** `test(justice): complete JusticeChoreWeightingTests coverage for Phase 5 fairness`

---

## Self-Review

Before declaring Phase 5 complete, verify each against the spec (§D5, §7.6) and the binding resolved decisions:

- [ ] **Weighted actual, both branches.** `CountActualPerUserAsync` and `GetSparklineSeriesAsync` chore branches use `Sum(c => c.WeightMinutes)` / `+= WeightMinutes` — NOT `Count()` / `++`. Shift and on-duty branches in both methods are byte-for-byte unchanged.
- [ ] **Category filter is opt-in.** `JusticeQuery.ChoreCategoryId` defaults to null; null path produces identical results to pre-Phase-5 (verified by the existing `JusticeServiceChoreOnDutyEligibilityTests` + any chore tests staying green). The filter uses `c.ChoreType != null && c.ChoreType.ChoreCategoryId == catId` so free-text chores drop out of a narrowed view (intended).
- [ ] **Target scaling is chore-only and applied at every chore target-return site.** `ResolvePerUserChoreOrOnDuty` (override + global branches) and `ResolveSingleWorkTypeExpected` (override + global branches) multiply by 480 **only when work-type is Chore**. On-duty expected is NOT scaled. Shift capacity is NOT scaled. Confirm no chore target-return path was missed (grep `ExpectedCount *` in JusticeService.cs and confirm each chore-reachable one carries `* scale` or `* ChoreWeightMinutesPerEquivalent`).
- [ ] **480 single source.** The scalar equals the Phase-1 `DEFAULT_CHORE_WEIGHT_MINUTES`. If Phase 1 exposed a shared public constant, this plan references it; otherwise the local `ChoreWeightMinutesPerEquivalent = 480m` is documented as mirroring it. The two must not drift.
- [ ] **Deviation/spread/banding untouched.** `ComputeDeviation`, `ComputeSpreadIndex`, `MapSpreadToSeverity`, `ComputeSharesAndBothBases` are unchanged. They are deviation-driven; because Actual and Expected are now both in weighted minutes, the deviation % is unit-consistent and bands remain meaningful.
- [ ] **No grant changes.** `GrantTypeSeed.cs` / `RoleTemplateSeed.cs` / `RoleTemplateAutoGrantTests.cs` are untouched. Reused `ViewJusticeTable` (#133) / `EditJusticeTargets` (#134) only.
- [ ] **UI: hours via shared helper.** Both the drawer (JS `formatHoursFromMinutes`) and `/Admin/Analytics` (`DurationFormat.FormatHours`) render absolute chore load in hours. `DurationFormat.FormatHours` is referenced, NOT redefined (Phase 2 owns it). NOTE: the drawer's JS hours formatter is a small local mirror of the C# helper because the JS layer can't call server code — this is intentional and the only duplication; both must agree numerically (minutes/60).
- [ ] **UI: chore-category selector visibility.** Drawer selector shows whenever the molecule has active chore categories (the drawer is chore-only by construction). Analytics selector shows ONLY when `WorkType == JusticeWorkType.Chore` AND options exist; hidden for Shift/All.
- [ ] **Localization.** All new keys (`Justice_Filter_ChoreCategory`, `Justice_Filter_AllChoreCategories`, `Justice_TargetUnitNote`) exist in BOTH `SharedResources.resx` and `SharedResources.he-IL.resx`, were grepped before adding (no duplicates), and the localization test suite is green.
- [ ] **RTL/contrast.** The new `<select>` reuses the existing `.form-select`/`.filter-field` classes (already RTL/dark-mode safe per the ShiftCategory selector it mirrors). No new colored backgrounds introduced.
- [ ] **Full suite sequential.** `dotnet test … -- xUnit.ParallelizeTestCollections=false` is 100% green.
- [ ] **Final validation question (per global policy):** "Did we do everything correctly?" — confirm the weighted-minute change did not silently alter any non-chore metric, and that a null-category chore query reproduces the exact numbers the pre-Phase-5 build produced for the same seed.

### Deferred Items
- **What-if preview chore +1 semantics (Task 5 Step 5).** The candidate-preview "Actual N → N+1" in `justice-panel.js` (`renderPreview`) adds a unit of 1 to the chore actual; with weighted minutes the "+1" no longer means "+1 chore" cleanly. Left unchanged this phase — the fairness columns and ribbon are correct, but the in-drawer what-if delta for chores still reads as +1 (count-flavored). Flagged for a follow-up that adds the candidate's prospective chore weight to the preview. **Confirm with the user whether to address now or later.**
- **Analytics JS basis-toggle re-formatting (Task 6 Step 4c).** The `data-exp-bysize`/`data-exp-equal` attributes stay raw (numeric) to drive the existing client-side basis toggle. After a basis toggle the Expected cell's JS-swapped text shows raw minutes (not hours) for chores until reload. Server-rendered initial value is correct in hours; only the post-toggle client swap is un-formatted. Flagged as a small JS follow-up (teach the toggle to call the hours formatter when `workType=Chore`). **Confirm with the user whether to address now or later.**
- **PageModel test harness (Tasks 5 & 6, Step 1).** If the repo has no existing `ChoresModel` / `AnalyticsModel` PageModel test infrastructure, the handler-threading tests are replaced by service-level coverage (Task 2) + browser verification. Whether to build a PageModel harness is deferred. **Confirm with the user whether to address now or later.**
