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
/// Tests for Task A9 — UsersInMolecule level: all active users pooled across every
/// company in a molecule, with a per-row GroupLabel carrying the company name.
///
/// Seed:
///   Molecule 1 contains two companies:
///     Company A ("Alpha") — 2 users (U101, U102)
///     Company B ("Beta")  — 3 users (U201, U202, U203)
///
///   Chore actuals (period 2026-05-01..2026-05-31):
///     U101: 4 chores   U102: 2 chores   → CoA total = 6
///     U201: 5 chores   U202: 3 chores   U203: 1 chore  → CoB total = 9
///
///   Targets:
///     Global:  1 chore / month / user (drives per-user expected under BySize by default)
///     CoB override: 15 chores/month total for CoB (3 users → 5/user expected)
///       → proves per-company context is used, not a pooled value
/// </summary>
public class JusticeServiceUsersInMoleculeTests : IDisposable
{
    // ------------------------------------------------------------------
    // Hierarchy IDs
    // ------------------------------------------------------------------
    private const int AreaId     = 1;
    private const int MoleculeId = 1;
    private const int CompanyAId = 1;  // "Alpha", 2 users
    private const int CompanyBId = 2;  // "Beta",  3 users

    // User IDs
    private const int UserA1 = 101;
    private const int UserA2 = 102;
    private const int UserB1 = 201;
    private const int UserB2 = 202;
    private const int UserB3 = 203;

    private const string CompanyAName = "Alpha";
    private const string CompanyBName = "Beta";

    // Phase 5: chore fairness is duration-weighted. Each seeded chore carries the default per-chore
    // weight (480 min = 8h) so the weighted Actual equals (chore count) × 480 — preserving the
    // original count-based fairness ratios while expressing them in the new weighted-minute units.
    private const int ChoreWeight = ShiftManager.Services.ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES;

    // Chore counts per user (weighted Actual = count × ChoreWeight).
    private const int CountA1 = 4;
    private const int CountA2 = 2;
    private const int CountB1 = 5;
    private const int CountB2 = 3;
    private const int CountB3 = 1;

    // Weighted chore actuals per user (Phase 5).
    private const decimal ActualA1 = CountA1 * ChoreWeight;
    private const decimal ActualA2 = CountA2 * ChoreWeight;
    private const decimal ActualB1 = CountB1 * ChoreWeight;
    private const decimal ActualB2 = CountB2 * ChoreWeight;
    private const decimal ActualB3 = CountB3 * ChoreWeight;

    // Global target: 1 chore / month / user
    // PeriodMultiplier for May 2026 (31 days): 31 / 30.4375 ≈ 1.01848...
    // CoA per-user expected (global) ≈ 1 * multiplier
    // CoB per-user expected (override): 15/month total / 3 users = 5 * multiplier

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceUsersInMoleculeTests()
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
            busyService, new EligibilityEvaluator());
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
            new Company { Id = CompanyAId, MoleculeId = MoleculeId, Name = CompanyAName, DisplayName = CompanyAName },
            new Company { Id = CompanyBId, MoleculeId = MoleculeId, Name = CompanyBName, DisplayName = CompanyBName });

        _db.Users.AddRange(
            new AppUser { Id = UserA1, Email = "u101@alpha.com", DisplayName = "UserA1", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = UserA2, Email = "u102@alpha.com", DisplayName = "UserA2", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = UserB1, Email = "u201@beta.com",  DisplayName = "UserB1", CompanyId = CompanyBId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = UserB2, Email = "u202@beta.com",  DisplayName = "UserB2", CompanyId = CompanyBId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = UserB3, Email = "u203@beta.com",  DisplayName = "UserB3", CompanyId = CompanyBId, IsActive = true, Role = UserRole.Employee });

        // Global chore target: 1 chore/month/user
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

        // CoB company override: 15 chores/month total for the company
        // → per-user expected = 15 / 3 users * multiplier ≈ 5 * multiplier
        // This is intentionally much higher than the global default (1) so CoA and CoB
        // users have clearly distinct per-user expected values.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 2,
            CompanyId = CompanyBId,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Company,
            ScopeId = CompanyBId,
            ExpectedCount = 15m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();

        // Chore actuals — period: 2026-05-15 (within 2026-05-01..2026-05-31)
        var workDate = new DateOnly(2026, 5, 15);
        int choreId = 1;

        void AddChores(int userId, int companyId, int count)
        {
            for (int i = 0; i < count; i++)
                _db.Chores.Add(new Chore { Id = choreId++, CompanyId = companyId, UserId = userId, Date = workDate, CanceledAt = null, WeightMinutes = ChoreWeight });
        }

        AddChores(UserA1, CompanyAId, CountA1);
        AddChores(UserA2, CompanyAId, CountA2);
        AddChores(UserB1, CompanyBId, CountB1);
        AddChores(UserB2, CompanyBId, CountB2);
        AddChores(UserB3, CompanyBId, CountB3);

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
    private static JusticeQuery MakeQuery(FairnessBasis basis = FairnessBasis.BySize) => new JusticeQuery(
        Scope: JusticeScope.Molecule,
        ScopeId: MoleculeId,
        PeriodStart: new DateOnly(2026, 5, 1),
        PeriodEnd: new DateOnly(2026, 5, 31),
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.UsersInMolecule,
        Basis: basis);

    // ==================================================================
    // Tests
    // ==================================================================

    /// <summary>
    /// All 5 active users from both companies are returned as individual rows.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_ReturnsAllUsersAcrossBothCompanies()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        view.Rows.Should().HaveCount(5, "2 users from Alpha + 3 users from Beta = 5 total");
        view.Rows.Select(r => r.Id).Should().BeEquivalentTo(new[] { UserA1, UserA2, UserB1, UserB2, UserB3 });
    }

    /// <summary>
    /// Each row's GroupLabel must equal the name of the company the user belongs to.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_GroupLabel_MatchesCompanyName()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        var rowById = view.Rows.ToDictionary(r => r.Id);

        rowById[UserA1].GroupLabel.Should().Be(CompanyAName);
        rowById[UserA2].GroupLabel.Should().Be(CompanyAName);
        rowById[UserB1].GroupLabel.Should().Be(CompanyBName);
        rowById[UserB2].GroupLabel.Should().Be(CompanyBName);
        rowById[UserB3].GroupLabel.Should().Be(CompanyBName);
    }

    /// <summary>
    /// Per-user Actual must match the seeded chore counts.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_PerUserActuals_AreCorrect()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        var rowById = view.Rows.ToDictionary(r => r.Id);

        rowById[UserA1].Actual.Should().Be(ActualA1);
        rowById[UserA2].Actual.Should().Be(ActualA2);
        rowById[UserB1].Actual.Should().Be(ActualB1);
        rowById[UserB2].Actual.Should().Be(ActualB2);
        rowById[UserB3].Actual.Should().Be(ActualB3);
    }

    /// <summary>
    /// CoA users use the global default (1/month ≈ multiplier ≈ 1.018).
    /// CoB users use the company override (15/month / 3 users = 5/user ≈ 5.09).
    /// These must be clearly different, proving per-company context is respected.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_PerUserExpected_UsesOwnCompanyContext()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        var rowById = view.Rows.ToDictionary(r => r.Id);

        var coaExpected = rowById[UserA1].ExpectedBySize;
        var cobExpected = rowById[UserB1].ExpectedBySize;

        // Phase 5: chore Expected is scaled by ChoreWeight (480) so it compares against weighted-minute Actual.
        // CoA: global 1/month * ~1 month * 480 ≈ 1.018 * 480 ≈ 488.9
        coaExpected.Should().BeApproximately(1.018m * ChoreWeight, 0.01m * ChoreWeight, "CoA uses global 1/month target, scaled to weighted minutes");

        // CoB: override 15/month / 3 users * ~1 month * 480 ≈ 5.09 * 480 ≈ 2443.6  (much higher)
        cobExpected.Should().BeApproximately(5.09m * ChoreWeight, 0.05m * ChoreWeight, "CoB uses company override 15/month / 3 users, scaled to weighted minutes");

        // They must be materially different (proves per-company context, not a pooled value).
        cobExpected.Should().BeGreaterThan(coaExpected * 3m,
            "CoB per-user expected should be ~5x CoA per-user expected due to the company override");
    }

    /// <summary>
    /// All users within the same company must have the same per-user Expected
    /// (headcount and capacity are homogeneous within a single company).
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_AllUsersInSameCompany_HaveSameExpected()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        var rowById = view.Rows.ToDictionary(r => r.Id);

        // CoA: U101 and U102 must have identical expected (same company, same headcount context).
        rowById[UserA1].ExpectedBySize.Should().Be(rowById[UserA2].ExpectedBySize,
            "both Alpha users share the same company target context");

        // CoB: U201, U202, U203 must all have identical expected.
        rowById[UserB1].ExpectedBySize.Should().Be(rowById[UserB2].ExpectedBySize);
        rowById[UserB2].ExpectedBySize.Should().Be(rowById[UserB3].ExpectedBySize,
            "all Beta users share the same company override context");
    }

    /// <summary>
    /// ActualShare (from the dual-basis post-processing step) must be populated on all rows
    /// and the values must sum to 1 across the full pooled set.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_ActualShare_IsPopulatedAndSumsToOne()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        view.Rows.Should().OnlyContain(r => r.ActualShare.HasValue,
            "ComputeSharesAndBothBases must populate ActualShare for all rows when Σ Actual > 0");

        var sumOfShares = view.Rows.Sum(r => r.ActualShare!.Value);
        sumOfShares.Should().BeApproximately(1.0m, 0.001m, "ActualShare values must sum to 1.0");
    }

    /// <summary>
    /// Passing a wrong scope (e.g., Scope=Company instead of Scope=Molecule) must return
    /// an empty row set (precondition guard).
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_WrongScope_ReturnsEmptyList()
    {
        var query = new JusticeQuery(
            Scope: JusticeScope.Company,   // wrong — should be Molecule
            ScopeId: CompanyAId,
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInMolecule);

        var view = await _service.GetJusticeViewAsync(query);

        view.Rows.Should().BeEmpty("precondition guard must reject Scope != Molecule");
    }

    /// <summary>
    /// Passing ScopeId=null must return an empty row set.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_NullScopeId_ReturnsEmptyList()
    {
        var query = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: null,               // missing scope id
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInMolecule);

        var view = await _service.GetJusticeViewAsync(query);

        view.Rows.Should().BeEmpty("precondition guard must reject null ScopeId");
    }

    /// <summary>
    /// DeviationPercent must be populated for all rows that have a non-zero Expected.
    /// Because both companies have chore targets set (global or override), every user
    /// should have a non-null deviation.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_DeviationPercent_IsPopulatedForAllRows()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery());

        view.Rows.Should().OnlyContain(r => r.DeviationPercent.HasValue,
            "every user has a chore target defined, so expected > 0 for all rows");
    }

    /// <summary>
    /// GroupLabel must be null for UsersInCompany level rows (backward-compatibility guard —
    /// the additive init property must default to null when not set by the builder).
    /// </summary>
    [Fact]
    public async Task UsersInCompany_GroupLabel_IsNull()
    {
        var companyQuery = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: CompanyAId,
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var view = await _service.GetJusticeViewAsync(companyQuery);

        view.Rows.Should().OnlyContain(r => r.GroupLabel == null,
            "GroupLabel is only set by BuildUsersInMoleculeAsync; other builders leave it null");
    }

    /// <summary>
    /// EqualShare basis still works for the UsersInMolecule level: all rows get the same
    /// Expected (= Σ Actual / N), and ActualShare still sums to 1.
    /// </summary>
    [Fact]
    public async Task UsersInMolecule_EqualShareBasis_AllRowsGetSameExpected()
    {
        var view = await _service.GetJusticeViewAsync(MakeQuery(FairnessBasis.EqualShare));

        decimal totalActual = ActualA1 + ActualA2 + ActualB1 + ActualB2 + ActualB3; // 4+2+5+3+1 = 15
        decimal expectedEqual = totalActual / 5m; // 15 / 5 = 3.0

        view.Rows.Should().OnlyContain(r =>
            r.Expected == expectedEqual,
            "under EqualShare all rows share the same Expected = Σ Actual / N");

        view.Rows.Should().OnlyContain(r => r.GroupLabel != null,
            "GroupLabel must survive the ComputeSharesAndBothBases `row with {...}` rebuild");
    }
}
