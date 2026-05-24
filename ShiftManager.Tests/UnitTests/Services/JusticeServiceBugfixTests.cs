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
            busyService);
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
        // Seed User1 in Company1
        _db.Users.Add(new AppUser
        {
            Id = User1Id, Email = "u1@c1.com", DisplayName = "U1",
            CompanyId = Company1Id, IsActive = true, Role = UserRole.Employee
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
