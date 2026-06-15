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
/// Tests for analytics exclusion rules in JusticeService:
///
///   1. GroupUser (AccountType == GroupUser) is ALWAYS excluded from analytics rows,
///      regardless of WorkType. (Mil is visible — only GroupUser is excluded.)
///
///   2. DoesShifts=false users are excluded from SHIFT-based metrics only
///      (WorkType == Shift or All). They remain visible for Chore and OnDuty queries.
///
/// Seed (UsersInCompany, single company):
///   A: Standard,   DoesShifts=true   → always included
///   B: Mil,        DoesShifts=true   → always included (Mil stays)
///   C: GroupUser,  DoesShifts=true   → always excluded
///   D: Standard,   DoesShifts=false  → included for Chore; excluded for Shift
///
/// Period: fully-past (2026-03-01..2026-03-31) so endCap = PeriodEnd (actual counts work).
/// Users A and B each get 2 shift assignments and 1 chore in the period.
/// User D gets 1 chore in the period (no shifts — DoesShifts=false makes sense).
/// User C gets nothing (excluded anyway).
/// </summary>
public class JusticeServiceAccountTypeAndDoesShiftsTests : IDisposable
{
    // ------------------------------------------------------------------
    // Hierarchy IDs
    // ------------------------------------------------------------------
    private const int AreaId     = 1;
    private const int MoleculeId = 1;
    private const int CompanyId  = 1;

    // Shift type
    private const int ShiftTypeId     = 10;
    private const int ShiftInstanceAId = 1;
    private const int ShiftInstanceBId = 2;

    // User IDs
    private const int UserA = 101; // Standard, DoesShifts=true
    private const int UserB = 102; // Mil,      DoesShifts=true
    private const int UserC = 103; // GroupUser, DoesShifts=true
    private const int UserD = 104; // Standard,  DoesShifts=false

    // Period in the past so endCap = PeriodEnd (all actuals count)
    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd   = new(2026, 3, 31);
    private static readonly DateOnly WorkDate     = new(2026, 3, 15);

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceAccountTypeAndDoesShiftsTests()
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
            busyService, new EligibilityEvaluator());
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
        _db.Areas.Add(new Area     { Id = AreaId,     ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1",  DisplayName = "Mol1" });
        _db.Companies.Add(new Company  { Id = CompanyId,  MoleculeId = MoleculeId, Name = "Co1",  DisplayName = "Co1" });

        // Users
        _db.Users.AddRange(
            new AppUser
            {
                Id = UserA, Email = "a@test.com", DisplayName = "UserA",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                AccountType = AccountType.Standard, DoesShifts = true
            },
            new AppUser
            {
                Id = UserB, Email = "b@test.com", DisplayName = "UserB",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                AccountType = AccountType.Mil, DoesShifts = true
            },
            new AppUser
            {
                Id = UserC, Email = "c@test.com", DisplayName = "UserC",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                AccountType = AccountType.GroupUser, DoesShifts = true
            },
            new AppUser
            {
                Id = UserD, Email = "d@test.com", DisplayName = "UserD",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                AccountType = AccountType.Standard, DoesShifts = false
            });

        // Shift type + instances (for shift actuals)
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = ShiftTypeId, Key = ShiftType.KEY_MORNING,
            CompanyId = CompanyId, Scope = ShiftScope.Company
        });

        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = ShiftInstanceAId, CompanyId = CompanyId, ShiftTypeId = ShiftTypeId, WorkDate = WorkDate, StaffingRequired = 2 },
            new ShiftInstance { Id = ShiftInstanceBId, CompanyId = CompanyId, ShiftTypeId = ShiftTypeId, WorkDate = WorkDate.AddDays(1), StaffingRequired = 2 });

        _db.SaveChanges();

        // Shift assignments: A and B each get 2 shifts; C and D get none (C=excluded; D=DoesShifts=false)
        _db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 1, CompanyId = CompanyId, ShiftInstanceId = ShiftInstanceAId, UserId = UserA },
            new ShiftAssignment { Id = 2, CompanyId = CompanyId, ShiftInstanceId = ShiftInstanceBId, UserId = UserA },
            new ShiftAssignment { Id = 3, CompanyId = CompanyId, ShiftInstanceId = ShiftInstanceAId, UserId = UserB },
            new ShiftAssignment { Id = 4, CompanyId = CompanyId, ShiftInstanceId = ShiftInstanceBId, UserId = UserB });

        // Chores: A, B, D each get 1 chore; C gets none
        int choreId = 1;
        _db.Chores.AddRange(
            new Chore { Id = choreId++, CompanyId = CompanyId, UserId = UserA, Date = WorkDate, CanceledAt = null },
            new Chore { Id = choreId++, CompanyId = CompanyId, UserId = UserB, Date = WorkDate, CanceledAt = null },
            new Chore { Id = choreId++, CompanyId = CompanyId, UserId = UserD, Date = WorkDate, CanceledAt = null });

        // Shift capacity target (to make Expected non-zero for shift queries)
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,
            WorkType = JusticeWorkType.Shift,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 2m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        // Chore target (to make Expected non-zero for chore queries)
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 2,
            CompanyId = null,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // ------------------------------------------------------------------
    // Query factory
    // ------------------------------------------------------------------

    private static JusticeQuery MakeQuery(JusticeWorkType workType) => new JusticeQuery(
        Scope:               JusticeScope.Company,
        ScopeId:             CompanyId,
        PeriodStart:         PeriodStart,
        PeriodEnd:           PeriodEnd,
        WorkType:            workType,
        ExcludeExemptShifts: false,
        Level:               JusticeLevel.UsersInCompany);

    // ==================================================================
    // GroupUser exclusion tests
    // ==================================================================

    /// <summary>
    /// WorkType=Shift: A (Standard, DoesShifts=true) and B (Mil, DoesShifts=true)
    /// must appear. C (GroupUser) and D (DoesShifts=false) must be excluded.
    /// </summary>
    [Fact]
    public async Task WorkTypeShift_IncludesStandardAndMil_ExcludesGroupUserAndDoesShiftsFalse()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Shift), CancellationToken.None);

        var rowIds = vm.Rows.Select(r => r.Id).ToList();

        rowIds.Should().Contain(UserA, "Standard+DoesShifts=true (UserA) must appear in Shift rows");
        rowIds.Should().Contain(UserB, "Mil+DoesShifts=true (UserB/Mil) must appear — Mil is NOT excluded");
        rowIds.Should().NotContain(UserC, "GroupUser (UserC) must NEVER appear in analytics, any WorkType");
        rowIds.Should().NotContain(UserD, "DoesShifts=false (UserD) must be excluded for Shift work type");
    }

    /// <summary>
    /// WorkType=Chore: A (Standard), B (Mil), D (DoesShifts=false) must appear.
    /// C (GroupUser) must be excluded regardless.
    /// D must stay because DoesShifts=false only gates Shift metrics.
    /// </summary>
    [Fact]
    public async Task WorkTypeChore_IncludesStandardMilAndDoesShiftsFalse_ExcludesGroupUser()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Chore), CancellationToken.None);

        var rowIds = vm.Rows.Select(r => r.Id).ToList();

        rowIds.Should().Contain(UserA, "Standard (UserA) must appear in Chore rows");
        rowIds.Should().Contain(UserB, "Mil (UserB) must appear in Chore rows");
        rowIds.Should().NotContain(UserC, "GroupUser (UserC) must NEVER appear in analytics");
        rowIds.Should().Contain(UserD, "DoesShifts=false (UserD) must remain visible for Chore work type");
    }

    // ==================================================================
    // Denominator consistency test (shift case)
    // ==================================================================

    /// <summary>
    /// For WorkType=Shift, the included population is {A, B} (2 users).
    /// The shift capacity comes from the 2 ShiftInstances × StaffingRequired (2 + 2 = 4 slots total).
    /// Per-user Expected = 4 / 2 = 2.
    ///
    /// If UserC or UserD were still counted in the denominator (headcount=4) but excluded from
    /// the row list, per-user Expected would wrongly be 4/4 = 1.
    ///
    /// Assert that per-user Expected = 2 (capacity=4 / headcount=2), confirming denominator
    /// and numerator cover the same population.
    /// </summary>
    [Fact]
    public async Task WorkTypeShift_PerUserExpected_UsesIncludedPopulationOnly()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Shift), CancellationToken.None);

        // Only 2 rows (A + B)
        vm.Rows.Should().HaveCount(2, "only A and B are included for Shift");

        // Total shift capacity = 2 ShiftInstances each with StaffingRequired=2 → 4
        // headcount = 2 (A + B)  → per-user Expected = 4 / 2 = 2
        // NOTE: period is 31 days; SumShiftCapacityPerCompanyAsync sums StaffingRequired across
        // all ShiftInstances in [PeriodStart..PeriodEnd], which here is 2+2=4.
        // Expected = 4 / 2 = 2.0 exactly (no period multiplier on capacity-based expected).
        var rowA = vm.Rows.Single(r => r.Id == UserA);
        var rowB = vm.Rows.Single(r => r.Id == UserB);

        rowA.Expected.Should().BeApproximately(2m, 0.01m,
            "per-user Expected = total_capacity(4) / included_headcount(2) = 2; " +
            "GroupUser and DoesShifts=false must NOT inflate the denominator");
        rowB.Expected.Should().BeApproximately(2m, 0.01m,
            "all included users share the same Expected under BySize with homogeneous headcount");
    }

    // ==================================================================
    // Actual counts are correct for included users
    // ==================================================================

    /// <summary>
    /// WorkType=Shift: A and B each have 2 shift assignments → Actual = 2 for both.
    /// </summary>
    [Fact]
    public async Task WorkTypeShift_ActualCounts_CorrectForIncludedUsers()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Shift), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == UserA);
        var rowB = vm.Rows.Single(r => r.Id == UserB);

        rowA.Actual.Should().Be(2m, "UserA has 2 shift assignments in the period");
        rowB.Actual.Should().Be(2m, "UserB has 2 shift assignments in the period");
    }

    /// <summary>
    /// WorkType=Chore: A, B, D each have 1 chore → Actual = 1 for each.
    /// </summary>
    [Fact]
    public async Task WorkTypeChore_ActualCounts_CorrectForIncludedUsers()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Chore), CancellationToken.None);

        var rowA = vm.Rows.Single(r => r.Id == UserA);
        var rowB = vm.Rows.Single(r => r.Id == UserB);
        var rowD = vm.Rows.Single(r => r.Id == UserD);

        rowA.Actual.Should().Be(1m, "UserA has 1 chore in the period");
        rowB.Actual.Should().Be(1m, "UserB has 1 chore in the period");
        rowD.Actual.Should().Be(1m, "UserD has 1 chore in the period");
    }

    // ==================================================================
    // Row count assertions (completeness guards)
    // ==================================================================

    /// <summary>Shift view must have exactly 2 rows (A + B).</summary>
    [Fact]
    public async Task WorkTypeShift_RowCount_IsTwo()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Shift), CancellationToken.None);
        vm.Rows.Should().HaveCount(2, "only UserA (Standard/DoesShifts=true) and UserB (Mil/DoesShifts=true) are included");
    }

    /// <summary>Chore view must have exactly 3 rows (A + B + D).</summary>
    [Fact]
    public async Task WorkTypeChore_RowCount_IsThree()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(JusticeWorkType.Chore), CancellationToken.None);
        vm.Rows.Should().HaveCount(3, "UserA, UserB, and UserD are included; only GroupUser (C) is excluded");
    }
}
