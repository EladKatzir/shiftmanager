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

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for Task A3 — switchable fairness basis (BySize vs EqualShare).
///
///   BySize     — the default, existing behavior: Expected is capacity/target-weighted per row.
///   EqualShare — every row's Expected = Σ rows.Actual / rows.Count (equal split of actual total).
///
/// Seed: 3 companies in one molecule with UNEQUAL headcounts (2 / 4 / 6) and known chore
/// actuals (12 / 6 / 6 = 24 total).  Because chore Expected scales with headcount under BySize
/// but is flat under EqualShare, the two bases produce clearly different per-row Expected values
/// and thus different deviation signs.
/// </summary>
public class JusticeServiceFairnessBasisTests : IDisposable
{
    // ------------------------------------------------------------------
    // Hierarchy IDs
    // ------------------------------------------------------------------
    private const int AreaId = 1;
    private const int MoleculeId = 1;
    private const int CompanyAId = 1;   // headcount = 2, actuals = 12
    private const int CompanyBId = 2;   // headcount = 4, actuals = 6
    private const int CompanyCId = 3;   // headcount = 6, actuals = 6

    // User IDs: 2 in A, 4 in B, 6 in C  (IDs are arbitrary but unique)
    private static readonly int[] UsersA = { 101, 102 };
    private static readonly int[] UsersB = { 201, 202, 203, 204 };
    private static readonly int[] UsersC = { 301, 302, 303, 304, 305, 306 };

    // Total actual chores: 12 + 6 + 6 = 24.  Equal share per company = 24 / 3 = 8.
    private const decimal TotalActual = 24m;
    private const decimal EqualSharePerCompany = TotalActual / 3m;

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceFairnessBasisTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();

        // Tenant = Company A (arbitrary — IgnoreQueryFilters bypasses it in the service).
        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyAId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options, tenantMock.Object);
        _db.Database.EnsureCreated();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", CompanyAId.ToString())
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

    // ------------------------------------------------------------------
    // Seed helpers
    // ------------------------------------------------------------------

    private void SeedAll()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        _db.Companies.AddRange(
            new Company { Id = CompanyAId, MoleculeId = MoleculeId, Name = "CoA", DisplayName = "CoA" },
            new Company { Id = CompanyBId, MoleculeId = MoleculeId, Name = "CoB", DisplayName = "CoB" },
            new Company { Id = CompanyCId, MoleculeId = MoleculeId, Name = "CoC", DisplayName = "CoC" });

        // Users
        var allUsers = UsersA.Select(id => new AppUser { Id = id, Email = $"u{id}@coA.com", DisplayName = $"U{id}", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee })
            .Concat(UsersB.Select(id => new AppUser { Id = id, Email = $"u{id}@coB.com", DisplayName = $"U{id}", CompanyId = CompanyBId, IsActive = true, Role = UserRole.Employee }))
            .Concat(UsersC.Select(id => new AppUser { Id = id, Email = $"u{id}@coC.com", DisplayName = $"U{id}", CompanyId = CompanyCId, IsActive = true, Role = UserRole.Employee }));
        _db.Users.AddRange(allUsers);

        // Global chore target: 1 chore/month per user (drives BySize expected).
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();

        // Chore actuals: 12 for CoA, 6 for CoB, 6 for CoC.
        // Distribute across users in each company. Period: 2026-05-01 to 2026-05-31.
        var workDate = new DateOnly(2026, 5, 15);
        int choreId = 1;

        // CoA: 12 chores spread across 2 users (6 each)
        foreach (var uid in UsersA)
        {
            for (int i = 0; i < 6; i++)
            {
                _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyAId, UserId = uid, Date = workDate, CanceledAt = null });
            }
        }

        // CoB: 6 chores spread across 4 users (1-2 each)
        // 2 users get 2, 2 users get 1  → total 6
        for (int i = 0; i < 4; i++)
        {
            int count = i < 2 ? 2 : 1;
            for (int j = 0; j < count; j++)
            {
                _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyBId, UserId = UsersB[i], Date = workDate, CanceledAt = null });
            }
        }

        // CoC: 6 chores spread across 6 users (1 each)
        foreach (var uid in UsersC)
        {
            _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyCId, UserId = uid, Date = workDate, CanceledAt = null });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // ------------------------------------------------------------------
    // Query factory
    // ------------------------------------------------------------------

    /// <summary>Query period: full May 2026 (~30.4375 days = 1 month, multiplier ≈ 1).</summary>
    private static JusticeQuery MakeQuery(FairnessBasis basis) => new JusticeQuery(
        Scope: JusticeScope.Molecule,
        ScopeId: MoleculeId,
        PeriodStart: new DateOnly(2026, 5, 1),
        PeriodEnd: new DateOnly(2026, 5, 31),
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.CompaniesInMolecule,
        Basis: basis);

    // ==================================================================
    // BySize tests (default behavior — must remain unchanged by this PR)
    // ==================================================================

    /// <summary>
    /// BySize: Expected scales with headcount.
    /// CoA (2 users) expected ≈ 2 * 1 * (31/30.4375) ≈ 2.0185.
    /// CoB (4 users) expected ≈ 4 * (31/30.4375) ≈ 4.037.
    /// CoC (6 users) expected ≈ 6 * (31/30.4375) ≈ 6.056.
    /// The three values must all differ from each other.
    /// </summary>
    [Fact]
    public async Task BySize_CompaniesExpected_DiffersByHeadcount()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == CompanyAId);
        var rowB = vm.Rows.Single(r => r.Id == CompanyBId);
        var rowC = vm.Rows.Single(r => r.Id == CompanyCId);

        // Expected grows with headcount
        rowA.Expected.Should().BeLessThan(rowB.Expected,
            "CoA has fewer users (2) than CoB (4); BySize expected must scale with headcount");
        rowB.Expected.Should().BeLessThan(rowC.Expected,
            "CoB has fewer users (4) than CoC (6); BySize expected must scale with headcount");
    }

    /// <summary>
    /// BySize: CoA (12 actual, ~2 expected) is massively over — positive deviation.
    /// CoB (6 actual, ~4 expected) is moderately over — positive deviation.
    /// CoC (6 actual, ~6 expected) is roughly balanced — near-zero deviation.
    /// </summary>
    [Fact]
    public async Task BySize_Deviations_ReflectCapacityWeighting()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == CompanyAId);
        var rowB = vm.Rows.Single(r => r.Id == CompanyBId);
        var rowC = vm.Rows.Single(r => r.Id == CompanyCId);

        // CoA: actual(12) >> expected(~2) → strong positive deviation
        rowA.DeviationPercent.Should().BeGreaterThan(100m,
            "CoA has 12 actuals vs ~2 expected (by-size); deviation must exceed 100%");

        // CoB: actual(6) > expected(~4) → positive deviation (smaller than A's)
        rowB.DeviationPercent.Should().BeGreaterThan(0m,
            "CoB has 6 actuals vs ~4 expected (by-size); deviation must be positive");
        rowB.DeviationPercent.Should().BeLessThan(rowA.DeviationPercent!.Value,
            "CoB deviation must be less than CoA's");

        // CoC: actual(6) ≈ expected(~6) → near-zero deviation (within ±15%)
        rowC.DeviationPercent.Should().BeInRange(-15m, 15m,
            "CoC has 6 actuals vs ~6 expected (by-size); deviation must be near zero");
    }

    // ==================================================================
    // EqualShare tests
    // ==================================================================

    /// <summary>
    /// EqualShare: every company's Expected = totalActual / rowCount = 24 / 3 = 8,
    /// regardless of headcount.  All three Expected values must be equal (within rounding).
    /// </summary>
    [Fact]
    public async Task EqualShare_AllCompaniesHaveSameExpected()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == CompanyAId);
        var rowB = vm.Rows.Single(r => r.Id == CompanyBId);
        var rowC = vm.Rows.Single(r => r.Id == CompanyCId);

        rowA.Expected.Should().BeApproximately(EqualSharePerCompany, 0.01m,
            $"EqualShare: CoA Expected must be totalActual/3 = {EqualSharePerCompany}");
        rowB.Expected.Should().BeApproximately(EqualSharePerCompany, 0.01m,
            $"EqualShare: CoB Expected must be totalActual/3 = {EqualSharePerCompany}");
        rowC.Expected.Should().BeApproximately(EqualSharePerCompany, 0.01m,
            $"EqualShare: CoC Expected must be totalActual/3 = {EqualSharePerCompany}");
    }

    /// <summary>
    /// EqualShare: CoA actual(12) > mean(8) → positive deviation.
    /// CoB actual(6) &lt; mean(8) → negative deviation.
    /// CoC actual(6) &lt; mean(8) → negative deviation.
    /// </summary>
    [Fact]
    public async Task EqualShare_Deviations_RelativeToMean()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == CompanyAId);
        var rowB = vm.Rows.Single(r => r.Id == CompanyBId);
        var rowC = vm.Rows.Single(r => r.Id == CompanyCId);

        // CoA: 12 actual / 8 expected → +50 %
        rowA.DeviationPercent.Should().BeApproximately(50m, 1m,
            "CoA: actual(12) / expected(8) = 150% → deviation = +50%");
        rowA.Band.Should().Be(DeviationBand.Over,
            "CoA at +50% deviation is in the Over band (>= +25%)");

        // CoB and CoC: 6 actual / 8 expected → -25%
        rowB.DeviationPercent.Should().BeApproximately(-25m, 1m,
            "CoB: actual(6) / expected(8) = 75% → deviation = -25%");
        rowB.DeviationPercent.Should().BeLessThan(0m,
            "CoB is below mean; deviation must be negative");

        rowC.DeviationPercent.Should().BeApproximately(-25m, 1m,
            "CoC: actual(6) / expected(8) = 75% → deviation = -25%");
        rowC.DeviationPercent.Should().BeLessThan(0m,
            "CoC is below mean; deviation must be negative");
    }

    /// <summary>
    /// EqualShare vs BySize: CoA Expected under EqualShare must differ from CoA Expected
    /// under BySize, confirming the two bases produce different row values for an unequal
    /// headcount scenario.
    /// </summary>
    [Fact]
    public async Task EqualShare_ExpectedDiffersFromBySize_ForUnequalHeadcounts()
    {
        var vmBySize = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);
        var vmEqualShare = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare), CancellationToken.None);

        var bySizeA = vmBySize.Rows.Single(r => r.Id == CompanyAId).Expected;
        var equalShareA = vmEqualShare.Rows.Single(r => r.Id == CompanyAId).Expected;

        bySizeA.Should().NotBeApproximately(equalShareA, 0.5m,
            "CoA (2 users) BySize expected ≈ 2 while EqualShare expected = 8; they must differ significantly");
    }

    // ==================================================================
    // Default-basis backward-compatibility test
    // ==================================================================

    /// <summary>
    /// A JusticeQuery constructed WITHOUT specifying the Basis parameter (7-arg positional form,
    /// matching all pre-A3 call sites) must default to BySize and produce the same result as
    /// an explicit BySize query.
    /// </summary>
    [Fact]
    public async Task DefaultBasis_IsEquivalentToBySize()
    {
        // 7-arg form — identical to all existing call sites (pre-A3)
        var queryDefault = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: MoleculeId,
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.CompaniesInMolecule);

        var vmDefault = await _service.GetJusticeViewAsync(queryDefault, CancellationToken.None);
        var vmBySize = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var defaultA = vmDefault.Rows.Single(r => r.Id == CompanyAId);
        var bySizeA = vmBySize.Rows.Single(r => r.Id == CompanyAId);

        defaultA.Expected.Should().BeApproximately(bySizeA.Expected, 0.001m,
            "Default (no Basis param) must behave identically to explicit FairnessBasis.BySize");
        defaultA.DeviationPercent.Should().BeApproximately(bySizeA.DeviationPercent!.Value, 0.001m,
            "Default deviation must match BySize deviation");
    }
}
