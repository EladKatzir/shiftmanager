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
/// Tests for Task A4 — per-row actual/expected shares + dual-basis precompute.
///
/// The front-end needs to toggle between BySize and EqualShare WITHOUT a round-trip.
/// This requires the service to populate both fairness bases on every row regardless
/// of which basis is "active" in the query.
///
/// New fields on JusticeRow:
///   ActualShare         = actual / Σactual            (null when Σactual == 0)
///   ExpectedBySize      = expected under BySize basis
///   ExpectedEqual       = Σactual / N
///   ExpectedShareBySize = ExpectedBySize / Σ ExpectedBySize  (null when Σ == 0)
///   ExpectedShareEqual  = 1 / N
///   DeviationPercentEqual  (null when ExpectedEqual == 0)
///   BandEqual
///
/// Seed: 3 companies in one molecule with UNEQUAL chore actuals (12 / 6 / 6 = 24 total)
/// and UNEQUAL headcounts (2 / 4 / 6), giving a clearly non-trivial BySize baseline.
/// </summary>
public class JusticeServiceSharesAndDrillTests : IDisposable
{
    // ------------------------------------------------------------------
    // Hierarchy IDs
    // ------------------------------------------------------------------
    private const int AreaId    = 1;
    private const int MoleculeId = 1;
    private const int CompanyAId = 1;   // headcount = 2, actuals = 12
    private const int CompanyBId = 2;   // headcount = 4, actuals = 6
    private const int CompanyCId = 3;   // headcount = 6, actuals = 6

    private static readonly int[] UsersA = { 101, 102 };
    private static readonly int[] UsersB = { 201, 202, 203, 204 };
    private static readonly int[] UsersC = { 301, 302, 303, 304, 305, 306 };

    // Derived constants
    private const decimal ActualA = 12m;
    private const decimal ActualB = 6m;
    private const decimal ActualC = 6m;
    private const decimal TotalActual = ActualA + ActualB + ActualC; // 24
    private const int     RowCount   = 3;

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceSharesAndDrillTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();

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
    // Seed
    // ------------------------------------------------------------------
    private void SeedAll()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        _db.Companies.AddRange(
            new Company { Id = CompanyAId, MoleculeId = MoleculeId, Name = "CoA", DisplayName = "CoA" },
            new Company { Id = CompanyBId, MoleculeId = MoleculeId, Name = "CoB", DisplayName = "CoB" },
            new Company { Id = CompanyCId, MoleculeId = MoleculeId, Name = "CoC", DisplayName = "CoC" });

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

        var workDate = new DateOnly(2026, 5, 15);
        int choreId = 1;

        // CoA: 12 chores spread across 2 users (6 each)
        foreach (var uid in UsersA)
            for (int i = 0; i < 6; i++)
                _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyAId, UserId = uid, Date = workDate, CanceledAt = null });

        // CoB: 6 chores (2 users get 2, 2 users get 1 → total 6)
        for (int i = 0; i < 4; i++)
        {
            int count = i < 2 ? 2 : 1;
            for (int j = 0; j < count; j++)
                _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyBId, UserId = UsersB[i], Date = workDate, CanceledAt = null });
        }

        // CoC: 6 chores (1 per user)
        foreach (var uid in UsersC)
            _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyCId, UserId = uid, Date = workDate, CanceledAt = null });

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
    // ActualShare tests
    // ==================================================================

    /// <summary>
    /// ActualShare for all rows must sum to 1.0 (within 0.001).
    /// </summary>
    [Fact]
    public async Task ActualShare_SumsToOne_AcrossAllRows()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var totalShare = vm.Rows.Sum(r => r.ActualShare ?? 0m);
        totalShare.Should().BeApproximately(1m, 0.001m,
            "ActualShare values must sum to 1.0 across all rows");
    }

    /// <summary>
    /// ActualShare for each row = actual / Σactual.
    /// CoA: 12/24 = 0.5, CoB: 6/24 = 0.25, CoC: 6/24 = 0.25.
    /// </summary>
    [Fact]
    public async Task ActualShare_PerRow_EqualsActualOverTotalActual()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == CompanyAId);
        var rowB = vm.Rows.Single(r => r.Id == CompanyBId);
        var rowC = vm.Rows.Single(r => r.Id == CompanyCId);

        rowA.ActualShare.Should().HaveValue().And.BeApproximately(ActualA / TotalActual, 0.001m,
            "CoA ActualShare = 12/24 = 0.5");
        rowB.ActualShare.Should().HaveValue().And.BeApproximately(ActualB / TotalActual, 0.001m,
            "CoB ActualShare = 6/24 = 0.25");
        rowC.ActualShare.Should().HaveValue().And.BeApproximately(ActualC / TotalActual, 0.001m,
            "CoC ActualShare = 6/24 = 0.25");
    }

    // ==================================================================
    // ExpectedBySize tests
    // ==================================================================

    /// <summary>
    /// ExpectedBySize must be populated and equal to primary Expected when basis == BySize.
    /// </summary>
    [Fact]
    public async Task ExpectedBySize_PopulatedAndMatchesPrimaryExpected_WhenBasisIsBySize()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        foreach (var row in vm.Rows)
        {
            row.ExpectedBySize.Should().BeApproximately(row.Expected, 0.001m,
                $"Row {row.Id}: ExpectedBySize must equal primary Expected when Basis=BySize");
        }
    }

    /// <summary>
    /// ExpectedShareBySize values must sum to ~1.0 (within 0.001).
    /// </summary>
    [Fact]
    public async Task ExpectedShareBySize_SumsToOne_AcrossAllRows()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var totalShare = vm.Rows.Sum(r => r.ExpectedShareBySize ?? 0m);
        totalShare.Should().BeApproximately(1m, 0.001m,
            "ExpectedShareBySize values must sum to 1.0 across all rows");
    }

    /// <summary>
    /// ExpectedBySize must be basis-independent — it must have the SAME value regardless of
    /// which basis (BySize or EqualShare) the query uses.
    /// </summary>
    [Fact]
    public async Task ExpectedBySize_IsSameRegardlessOfActiveBasis()
    {
        var vmBySize     = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize),     CancellationToken.None);
        var vmEqualShare = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare), CancellationToken.None);

        foreach (var row in vmBySize.Rows)
        {
            var other = vmEqualShare.Rows.Single(r => r.Id == row.Id);
            row.ExpectedBySize.Should().BeApproximately(other.ExpectedBySize, 0.001m,
                $"Row {row.Id}: ExpectedBySize must be identical across both bases");
        }
    }

    // ==================================================================
    // ExpectedEqual tests
    // ==================================================================

    /// <summary>
    /// ExpectedEqual must equal Σactual / N for every row (= 24/3 = 8).
    /// This is basis-independent — it must be the same regardless of active basis.
    /// </summary>
    [Theory]
    [InlineData(FairnessBasis.BySize)]
    [InlineData(FairnessBasis.EqualShare)]
    public async Task ExpectedEqual_EqualsActualMean_ForEveryRow(FairnessBasis basis)
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(basis), CancellationToken.None);

        var expectedEqual = TotalActual / RowCount; // 8

        foreach (var row in vm.Rows)
        {
            row.ExpectedEqual.Should().BeApproximately(expectedEqual, 0.001m,
                $"Row {row.Id}: ExpectedEqual must be Σactual/N = {expectedEqual}");
        }
    }

    /// <summary>
    /// ExpectedShareEqual must be 1/N for every row (= 1/3 ≈ 0.333).
    /// </summary>
    [Fact]
    public async Task ExpectedShareEqual_IsOneOverN_ForEveryRow()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var expectedShare = 1m / RowCount;

        foreach (var row in vm.Rows)
        {
            row.ExpectedShareEqual.Should().HaveValue().And.BeApproximately(expectedShare, 0.001m,
                $"Row {row.Id}: ExpectedShareEqual must be 1/N = {expectedShare}");
        }
    }

    // ==================================================================
    // DeviationPercentEqual + BandEqual tests
    // ==================================================================

    /// <summary>
    /// DeviationPercentEqual must be computed against ExpectedEqual (= Σactual/N = 8).
    /// CoA: (12-8)/8*100 = +50%, Band = Over.
    /// CoB: (6-8)/8*100  = -25%, Band = Under.
    /// CoC: (6-8)/8*100  = -25%, Band = Under.
    /// </summary>
    [Fact]
    public async Task DeviationPercentEqual_CorrectPerRow_WhenBasisIsBySize()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == CompanyAId);
        var rowB = vm.Rows.Single(r => r.Id == CompanyBId);
        var rowC = vm.Rows.Single(r => r.Id == CompanyCId);

        rowA.DeviationPercentEqual.Should().HaveValue().And.BeApproximately(50m, 1m,
            "CoA: (12-8)/8*100 = +50%");
        rowA.BandEqual.Should().Be(DeviationBand.Over,
            "CoA at +50% deviation is in the Over band (>=+25%)");

        rowB.DeviationPercentEqual.Should().HaveValue().And.BeApproximately(-25m, 1m,
            "CoB: (6-8)/8*100 = -25%");
        rowB.BandEqual.Should().Be(DeviationBand.Under,
            "CoB at -25% is at the boundary — Under band (<=-25%)");

        rowC.DeviationPercentEqual.Should().HaveValue().And.BeApproximately(-25m, 1m,
            "CoC: (6-8)/8*100 = -25%");
        rowC.BandEqual.Should().Be(DeviationBand.Under,
            "CoC at -25% is at the boundary — Under band (<=-25%)");
    }

    // ==================================================================
    // Primary field routing tests
    // ==================================================================

    /// <summary>
    /// When Basis == BySize, primary Expected must equal ExpectedBySize for every row.
    /// This confirms that ComputeSharesAndBothBases does not overwrite primary fields
    /// when the active basis is BySize.
    /// </summary>
    [Fact]
    public async Task PrimaryExpected_EqualsBySize_WhenBasisIsBySize()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.BySize), CancellationToken.None);

        foreach (var row in vm.Rows)
        {
            row.Expected.Should().BeApproximately(row.ExpectedBySize, 0.001m,
                $"Row {row.Id}: primary Expected must equal ExpectedBySize when Basis=BySize");
        }
    }

    /// <summary>
    /// When Basis == EqualShare, primary Expected must equal ExpectedEqual for every row.
    /// </summary>
    [Fact]
    public async Task PrimaryExpected_EqualsEqualShare_WhenBasisIsEqualShare()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare), CancellationToken.None);

        foreach (var row in vm.Rows)
        {
            row.Expected.Should().BeApproximately(row.ExpectedEqual, 0.001m,
                $"Row {row.Id}: primary Expected must equal ExpectedEqual when Basis=EqualShare");
        }
    }

    /// <summary>
    /// When Basis == EqualShare, primary DeviationPercent must match DeviationPercentEqual.
    /// This confirms the active-basis routing in ComputeSharesAndBothBases.
    /// </summary>
    [Fact]
    public async Task PrimaryDeviation_MatchesEqualDeviation_WhenBasisIsEqualShare()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare), CancellationToken.None);

        foreach (var row in vm.Rows)
        {
            if (row.DeviationPercent.HasValue && row.DeviationPercentEqual.HasValue)
            {
                row.DeviationPercent.Value.Should().BeApproximately(row.DeviationPercentEqual.Value, 0.001m,
                    $"Row {row.Id}: primary DeviationPercent must match DeviationPercentEqual when Basis=EqualShare");
            }
        }
    }

    // ==================================================================
    // Basis-independence tests (both bases populated regardless of active basis)
    // ==================================================================

    /// <summary>
    /// Regardless of active basis, ActualShare must always be populated and sum to 1.
    /// </summary>
    [Theory]
    [InlineData(FairnessBasis.BySize)]
    [InlineData(FairnessBasis.EqualShare)]
    public async Task ActualShare_AlwaysPopulated_RegardlessOfBasis(FairnessBasis basis)
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(basis), CancellationToken.None);

        vm.Rows.Should().AllSatisfy(row =>
            row.ActualShare.Should().HaveValue($"ActualShare must be set for row {row.Id}"));

        vm.Rows.Sum(r => r.ActualShare ?? 0m)
            .Should().BeApproximately(1m, 0.001m, "ActualShare must sum to 1");
    }

    /// <summary>
    /// Regardless of active basis, ExpectedEqual must be populated on all rows.
    /// </summary>
    [Theory]
    [InlineData(FairnessBasis.BySize)]
    [InlineData(FairnessBasis.EqualShare)]
    public async Task BothBases_AlwaysPopulated_RegardlessOfActiveBasis(FairnessBasis basis)
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(basis), CancellationToken.None);

        vm.Rows.Should().AllSatisfy(row =>
        {
            row.ExpectedBySize.Should().BeGreaterThan(0m,
                $"Row {row.Id}: ExpectedBySize must always be populated");
            row.ExpectedEqual.Should().BeGreaterThan(0m,
                $"Row {row.Id}: ExpectedEqual must always be populated");
            row.ExpectedShareBySize.Should().HaveValue(
                $"Row {row.Id}: ExpectedShareBySize must always be populated");
            row.ExpectedShareEqual.Should().HaveValue(
                $"Row {row.Id}: ExpectedShareEqual must always be populated");
            row.DeviationPercentEqual.Should().HaveValue(
                $"Row {row.Id}: DeviationPercentEqual must always be populated");
        });
    }
}
