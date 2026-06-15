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
/// Regression tests for two JusticeService bugs:
///
///   A1 — JusticeTargets are loaded without IgnoreQueryFilters().
///        The EF query filter allows CompanyId == null OR CompanyId == currentTenant.
///        When the caller's tenant is Company 1, a Company-2-scoped override (CompanyId = 2)
///        is silently excluded, so cross-company admins see the wrong Expected value.
///
///   A2 — Exempt-shift exclusion checks only KEY_HOME and KEY_OFFLINE, missing KEY_HOME_AM
///        and KEY_HOME_PM.  A shift assignment with ShiftType.Key == KEY_HOME_PM must NOT be
///        counted as Actual when ExcludeExemptShifts == true.
///
/// Both tests follow the real-SQLite convention (SqliteConnection "DataSource=:memory:") to
/// exercise the SQL translator, matching JusticeServiceChoreOnDutyEligibilityTests.
/// </summary>
public class JusticeServiceBugfixTests : IDisposable
{
    // ------------------------------------------------------------------
    // Hierarchy IDs used by all tests
    // ------------------------------------------------------------------
    private const int AreaId = 1;
    private const int MoleculeId = 1;
    private const int Company1Id = 1; // ambient tenant (the caller's company)
    private const int Company2Id = 2; // cross-company (override must still appear)

    // Shift type IDs
    private const int ShiftTypeHomeId = 10;
    private const int ShiftTypeHomePmId = 11;

    // User IDs
    private const int User1Id = 101; // in Company1
    private const int User2Id = 102; // in Company1
    private const int User3Id = 201; // in Company2
    private const int User4Id = 202; // in Company2

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    /// <summary>
    /// The ambient tenant is Company1. The DbContext query-filter for JusticeTargets will
    /// therefore allow rows with CompanyId == null (global) or CompanyId == Company1Id.
    /// A Company2-scoped override (CompanyId == Company2Id) is invisible to the filter —
    /// that is exactly the bug we are testing.
    /// </summary>
    public JusticeServiceBugfixTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();

        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(Company1Id);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options, tenantMock.Object);
        _db.Database.EnsureCreated();

        // Wire up an HTTP context whose CompanyId claim matches the ambient tenant.
        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", Company1Id.ToString())
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

        SeedHierarchy();
    }

    // ------------------------------------------------------------------
    // Hierarchy seed (shared between both test groups)
    // ------------------------------------------------------------------

    private void SeedHierarchy()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        _db.Companies.Add(new Company { Id = Company1Id, MoleculeId = MoleculeId, Name = "Co1", DisplayName = "Co1" });
        _db.Companies.Add(new Company { Id = Company2Id, MoleculeId = MoleculeId, Name = "Co2", DisplayName = "Co2" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // ==================================================================
    // TASK A1 — IgnoreQueryFilters on JusticeTargets
    // ==================================================================

    /// <summary>
    /// Scenario:
    ///   - Global chore target: 1 chore/month per user.
    ///   - Company-2-scoped override: 10 chores/month total for Company2.
    ///   - Ambient tenant = Company1.
    ///
    /// With the bug: the Company2 target is invisible through the query filter → Expected for
    /// Company2 in the CompaniesInMolecule view = per-user default * headcount = 1 * 2 = 2.
    ///
    /// After the fix: IgnoreQueryFilters() bypasses the filter → Expected for Company2 =
    /// override * period-multiplier = 10 * (periodDays / 30.4375) ≈ much larger than 2.
    /// </summary>
    [Fact]
    public async Task GetJusticeView_CompaniesInMolecule_Company2Override_IsVisibleWhenTenantIsCompany1()
    {
        // Seed users: 2 in Company1, 2 in Company2
        _db.Users.AddRange(
            new AppUser { Id = User1Id, Email = "u1@c1.com", DisplayName = "U1", CompanyId = Company1Id, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = User2Id, Email = "u2@c1.com", DisplayName = "U2", CompanyId = Company1Id, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = User3Id, Email = "u3@c2.com", DisplayName = "U3", CompanyId = Company2Id, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = User4Id, Email = "u4@c2.com", DisplayName = "U4", CompanyId = Company2Id, IsActive = true, Role = UserRole.Employee });

        // Global chore target: 1 chore per user per month.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,                // Global row — visible even with the filter
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        // Company-2-scoped override: 10 chores/month total for Company2.
        // CompanyId = Company2Id → invisible through the Company1 tenant filter.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 2,
            CompanyId = Company2Id,          // This is the row the bug hides
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Company,
            ScopeId = Company2Id,
            ExpectedCount = 10m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();

        // Query: Molecule scope, CompaniesInMolecule level, Chore work-type.
        // Period: 30 days (approximately one month so the multiplier ≈ 1).
        var start = new DateOnly(2026, 5, 1);
        var end = new DateOnly(2026, 5, 30);

        var q = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: MoleculeId,
            PeriodStart: start,
            PeriodEnd: end,
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.CompaniesInMolecule);

        var vm = await _service.GetJusticeViewAsync(q, CancellationToken.None);

        var co2Row = vm.Rows.FirstOrDefault(r => r.Id == Company2Id);
        co2Row.Should().NotBeNull("Company2 must appear in CompaniesInMolecule view");

        // With the global-only fallback (bug path): Expected = 1 chore/user/month * 2 users = 2.
        // With override visible (fixed path): Expected = 10 * (29/30.4375) ≈ 9.52 >> 2.
        // We assert Expected > 5 to create a clear signal that the override was applied.
        co2Row!.Expected.Should().BeGreaterThan(5m,
            "Company2's Expected must reflect the 10-chores/month override, " +
            "not the global-default-derived value of ~2");
    }

    // ==================================================================
    // TASK A2 — Exempt-shift exclusion must cover HOME_AM and HOME_PM
    // ==================================================================

    /// <summary>
    /// Scenario:
    ///   - User1 has one regular shift assignment (ShiftType KEY_MORNING) in period.
    ///   - User1 also has one HOME_PM shift assignment in the same period.
    ///   - ExcludeExemptShifts = true.
    ///
    /// With the bug: only KEY_HOME and KEY_OFFLINE are excluded; KEY_HOME_PM passes through,
    /// so Actual for User1 = 2.
    ///
    /// After the fix: KEY_HOME_PM is also excluded, so Actual for User1 = 1.
    /// </summary>
    [Fact]
    public async Task GetJusticeView_UsersInCompany_ExcludeExemptShifts_HomePmShiftIsExcluded()
    {
        // Seed User1 in Company1.
        // DoesShifts=true is required because the new analytics filter excludes DoesShifts=false
        // users from Shift-type queries.  This user is assigned to shift instances, so the
        // flag must reflect reality.
        _db.Users.Add(new AppUser
        {
            Id = User1Id, Email = "u1@c1.com", DisplayName = "U1",
            CompanyId = Company1Id, IsActive = true, Role = UserRole.Employee,
            DoesShifts = true
        });

        // Seed ShiftTypes: a normal shift and a HOME_PM shift.
        // ShiftScope.Company (0) requires CompanyId IS NOT NULL; set Scope explicitly.
        var morningType = new ShiftType { Id = ShiftTypeHomeId, Key = ShiftType.KEY_MORNING, CompanyId = Company1Id, Scope = ShiftScope.Company };
        var homePmType = new ShiftType { Id = ShiftTypeHomePmId, Key = ShiftType.KEY_HOME_PM, CompanyId = Company1Id, Scope = ShiftScope.Company };
        _db.ShiftTypes.AddRange(morningType, homePmType);
        _db.SaveChanges();

        // Seed ShiftInstances — one per shift type, in the query period
        var workDate = new DateOnly(2026, 5, 10);
        var morningInstance = new ShiftInstance
        {
            Id = 1, CompanyId = Company1Id, ShiftTypeId = ShiftTypeHomeId,
            WorkDate = workDate, StaffingRequired = 1
        };
        var homePmInstance = new ShiftInstance
        {
            Id = 2, CompanyId = Company1Id, ShiftTypeId = ShiftTypeHomePmId,
            WorkDate = workDate, StaffingRequired = 1
        };
        _db.ShiftInstances.AddRange(morningInstance, homePmInstance);
        _db.SaveChanges();

        // Assign User1 to BOTH instances
        _db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 1, CompanyId = Company1Id, ShiftInstanceId = 1, UserId = User1Id },
            new ShiftAssignment { Id = 2, CompanyId = Company1Id, ShiftInstanceId = 2, UserId = User1Id });
        _db.SaveChanges();

        // Seed a global chore target (so the Expected resolves and we get a valid row)
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1, CompanyId = null, WorkType = JusticeWorkType.Shift,
            ScopeKind = JusticeScope.Global, ScopeId = null,
            ExpectedCount = 5m, PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        var start = new DateOnly(2026, 5, 1);
        var end = new DateOnly(2026, 5, 31);

        var q = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: Company1Id,
            PeriodStart: start,
            PeriodEnd: end,
            WorkType: JusticeWorkType.Shift,
            ExcludeExemptShifts: true,  // <-- the toggle under test
            Level: JusticeLevel.UsersInCompany);

        var vm = await _service.GetJusticeViewAsync(q, CancellationToken.None);

        var user1Row = vm.Rows.FirstOrDefault(r => r.Id == User1Id);
        user1Row.Should().NotBeNull("User1 must appear in UsersInCompany view");

        // With the bug: Actual = 2 (KEY_HOME_PM passes the old filter).
        // After the fix: Actual = 1 (only the MORNING shift counted).
        user1Row!.Actual.Should().Be(1m,
            "HOME_PM shift must be excluded when ExcludeExemptShifts is true; " +
            "only the MORNING shift should be counted");
    }
}

// ===========================================================================
// A8 — Characterization test for BuildChoreHolesAsync
//
// Seeds a molecule with 4 companies, each with 2 active users. One user per
// company has 0 chores (Under) and one has 2 chores (Balanced). The test
// calls GetInContextViewAsync(WorkType=Chore, Scope=Molecule) and pins the
// EXACT WhereToFocus hole list so the subsequent N+1 refactor cannot silently
// change observable behavior.
//
// The period is fully in the future (2027-07-01 → 2027-07-31) so that
// lookFrom == q.PeriodStart and the hole dates are deterministic.
// ===========================================================================

/// <summary>
/// A8: Characterization test — locks the exact WhereToFocus output of
/// BuildChoreHolesAsync across a 4-company molecule before the N+1 refactor.
/// Must pass against BOTH the old and new implementations.
/// </summary>
public class JusticeServiceA8ChoreHoleCharacterizationTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Hierarchy
    // -----------------------------------------------------------------------
    private const int AreaId     = 10;
    private const int MoleculeId = 10;
    private const int Co1 = 101, Co2 = 102, Co3 = 103, Co4 = 104;

    // Users: even ids = Under (0 chores), odd ids = Balanced (2 chores).
    // Insertion order matches company order: Co1→U1,U2 Co2→U3,U4 Co3→U5,U6 Co4→U7,U8
    private const int U1 = 1001, U2 = 1002; // Co1 — U1=Under, U2=Balanced
    private const int U3 = 1003, U4 = 1004; // Co2 — U3=Under, U4=Balanced
    private const int U5 = 1005, U6 = 1006; // Co3 — U5=Under, U6=Balanced
    private const int U7 = 1007, U8 = 1008; // Co4 — U7=Under, U8=Balanced

    // Period — fully future so lookFrom == PeriodStart (deterministic hole dates).
    private static readonly DateOnly PeriodStart = new(2027, 7, 1);
    private static readonly DateOnly PeriodEnd   = new(2027, 7, 31);

    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceA8ChoreHoleCharacterizationTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();

        // Tenant = Co1 (does not matter for the IgnoreQueryFilters paths in JusticeService).
        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(Co1);

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        _db = new AppDbContext(opts, tenantMock.Object);
        _db.Database.EnsureCreated();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", Co1.ToString())
        }, "TestAuth"));
        httpAccessor.Setup(x => x.HttpContext).Returns(ctx);

        var busyService = BusyServiceMockFactory.Real(_db);
        var choreService = new ChoreService(
            _db, Mock.Of<ITenantResolver>(), httpAccessor.Object,
            Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<ChoreService>>(), Mock.Of<ICompanyCacheService>(), busyService, new EligibilityEvaluator());
        var onDutyService = new OnDutyService(
            _db, httpAccessor.Object, Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(), Mock.Of<IFeatureFlagService>(), busyService);
        var shiftSvc = new Mock<IShiftAssignmentService>(MockBehavior.Strict).Object;

        _service = new JusticeService(_db, shiftSvc, choreService, onDutyService);

        SeedAll();
    }

    private void SeedAll()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "A8Area", DisplayName = "A8Area" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "A8Mol", DisplayName = "A8Mol" });

        // Companies — inserted in Co1→Co4 order so SQLite SELECT preserves that order.
        _db.Companies.AddRange(
            new Company { Id = Co1, MoleculeId = MoleculeId, Name = "Co1", DisplayName = "Co1" },
            new Company { Id = Co2, MoleculeId = MoleculeId, Name = "Co2", DisplayName = "Co2" },
            new Company { Id = Co3, MoleculeId = MoleculeId, Name = "Co3", DisplayName = "Co3" },
            new Company { Id = Co4, MoleculeId = MoleculeId, Name = "Co4", DisplayName = "Co4" });

        // Users — inserted in company order; even offset = Under (no chores), odd offset = Balanced.
        _db.Users.AddRange(
            new AppUser { Id = U1, Email = "u1@t.com", DisplayName = "U1", CompanyId = Co1, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U2, Email = "u2@t.com", DisplayName = "U2", CompanyId = Co1, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U3, Email = "u3@t.com", DisplayName = "U3", CompanyId = Co2, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U4, Email = "u4@t.com", DisplayName = "U4", CompanyId = Co2, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U5, Email = "u5@t.com", DisplayName = "U5", CompanyId = Co3, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U6, Email = "u6@t.com", DisplayName = "U6", CompanyId = Co3, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U7, Email = "u7@t.com", DisplayName = "U7", CompanyId = Co4, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = U8, Email = "u8@t.com", DisplayName = "U8", CompanyId = Co4, IsActive = true, Role = UserRole.Employee });

        // Global chore target: 1 chore per user per month.
        // With a 31-day period: expected_per_user = 31 / 30.4375 ≈ 1.018.
        // User with 0 chores → deviation ≈ -100% → Under.
        // User with 2 chores → deviation ≈ +96% → Over.
        // (Balanced = deviation in -10..+10%. We need at least one Under user per company.)
        // Use 1 chore for Balanced users: 1/1.018 ≈ -1.8% → Balanced.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,       // Global
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        // "Balanced" users each get 1 completed chore in the period.
        // "Under" users (U1,U3,U5,U7) get 0 chores.
        // Use a past date well within the period (2027-07-01 is the period start — chores
        // with Date==PeriodStart are counted by CountActualPerUserAsync as long as Date<=endCap;
        // endCap=min(PeriodEnd, today). Since today << 2027-07-01, endCap=today, so NO chores
        // in the future period contribute to Actual. This means ALL users get Actual=0.
        //
        // IMPORTANT: CountActualPerUserAsync uses endCap=min(PeriodEnd, today) to avoid counting
        // future work. A fully-future period means endCap=today < PeriodStart, so the date
        // range [PeriodStart..endCap] is empty — Actual=0 for all users → all are Under.
        //
        // To make ONLY "balanced" users balanced (deviation != -100%), we need to distinguish
        // them. But with endCap < PeriodStart all chores in period are excluded.
        //
        // SOLUTION: use a mixed period that straddles today:
        //   PeriodStart = 60 days ago, PeriodEnd = 30 days in the future.
        //   Seed "balanced" users with 1 chore 30 days ago (in the past half of the period).
        //   "Under" users have 0 chores.
        //   expected_per_user = (91 days) / 30.4375 ≈ 2.99.
        //   balanced user actual=1 → deviation=(1-2.99)/2.99*100 ≈ -66% → Under too!
        //
        // Better approach: use a past period entirely, keeping lookFrom=today > PeriodStart
        // which means lookFrom=today (since today > PeriodStart). The holes will start from
        // today. But today changes, so hole dates aren't golden.
        //
        // CLEANEST: override lookFrom by making PeriodStart = tomorrow so that
        //   lookFrom = PeriodStart (future start wins). Then hole dates are deterministic.
        //   But actuals in a future period = 0 for everyone → all Under → all 8 users tied.
        //   With STABLE sort the first user encountered keeps position.
        //   For a 4-company molecule with 2 users each, all 8 users are Under (-100%).
        //   After OrderByDescending (stable, all tied) the order is preserved from allRows:
        //   (U1,Co1),(U2,Co1),(U3,Co2),(U4,Co2),(U5,Co3),(U6,Co3),(U7,Co4),(U8,Co4).
        //   Holes: U1@Jul1, U1@Jul2, U1@Jul3, U1@Jul4, U1@Jul5 → 5 holes, DONE.
        //   This is the golden output with ALL users Under.
        //
        // This is the simplest deterministic scenario: fully-future period, no chores seeded,
        // all users Under, first 5 holes from U1 (first user in Co1, first company in DB order).
        // We do NOT seed any chores for anyone — Actual=0 for all.

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // -----------------------------------------------------------------------
    // The characterization test
    // -----------------------------------------------------------------------

    /// <summary>
    /// A8 characterization: pins the EXACT WhereToFocus hole list produced by
    /// BuildChoreHolesAsync for a 4-company molecule.
    ///
    /// Scenario:
    ///   - 4 companies, 2 active users each, 8 users total
    ///   - Fully-future period → endCap &lt; PeriodStart → Actual=0 for all users
    ///   - Global target: 1 chore/user/month → expected ≈ 1.018 → all users Under (-100%)
    ///   - lookFrom = PeriodStart (future, deterministic hole dates)
    ///   - No pre-assigned chores in the window → no (user,date) exclusions
    ///   - Stable sort by |deviation| (all tied) preserves allRows order:
    ///     Co1→U1,U2 then Co2→U3,U4 then Co3→U5,U6 then Co4→U7,U8
    ///   - WhereToFocusTopN=5: first 5 holes = U1@Jul1..Jul5
    ///
    /// This golden output must remain IDENTICAL after the N+1 refactor.
    /// </summary>
    [Fact]
    public async Task GetInContextView_MoleculeScope_ChoreWorkType_WhereToFocusHoles_MatchGoldenOutput()
    {
        // Use a fully-future period so lookFrom == PeriodStart (deterministic).
        var start = PeriodStart;   // 2027-07-01
        var end   = PeriodEnd;     // 2027-07-31

        var q = new JusticeQuery(
            Scope:               JusticeScope.Molecule,
            ScopeId:             MoleculeId,
            PeriodStart:         start,
            PeriodEnd:           end,
            WorkType:            JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level:               JusticeLevel.UsersInCompany);

        var vm = await _service.GetInContextViewAsync(q);

        var holes = vm.WhereToFocus;

        // ---- golden assertions ----
        // All 5 holes must be chore kind.
        holes.Should().HaveCount(5, "WhereToFocusTopN=5");
        holes.Should().OnlyContain(h => h.Kind == "chore", "all holes are chore type");

        // All 5 holes belong to U1 (first under-loaded user, first company in DB order, stable sort).
        holes.Should().OnlyContain(h => h.UserId == U1,
            "U1 is the first under-loaded user in insertion order; stable sort preserves ordering for ties");

        // All 5 holes belong to Co1 (U1's company).
        holes.Should().OnlyContain(h => h.CompanyId == Co1,
            "U1 belongs to Co1");

        // Dates: consecutive days starting from PeriodStart (lookFrom = PeriodStart since it is future).
        holes.Select(h => h.Date).Should().BeEquivalentTo(
            new[]
            {
                new DateOnly(2027, 7, 1),
                new DateOnly(2027, 7, 2),
                new DateOnly(2027, 7, 3),
                new DateOnly(2027, 7, 4),
                new DateOnly(2027, 7, 5)
            },
            opts => opts.WithStrictOrdering(),
            "holes are ordered by user first (most-under), then by date within the user");

        // Deficit is always 1 for chore holes.
        holes.Should().OnlyContain(h => h.Deficit == 1, "chore holes always have deficit=1");
    }
}
