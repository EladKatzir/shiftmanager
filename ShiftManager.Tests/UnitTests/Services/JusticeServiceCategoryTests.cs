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
/// Tests the optional ShiftCategory filter added in Task 14 of the
/// "Account Types &amp; Analytics" plan.
///
/// Seed layout (molecule with two categories, UsersInCompany view):
///
///   MoleculeId = 1
///   CompanyId  = 1
///   CategoryYekev  (Id=10): contains ShiftTypeY (Id=20)
///   CategoryHazon  (Id=11): contains ShiftTypeH (Id=21)
///
///   U1 (DoesShifts=true, member of Yekev):
///     • 2 shift assignments in Yekev (ShiftTypeY)
///     • 1 shift assignment in Hazon (ShiftTypeH) — must be excluded when filtering to Yekev
///   U2 (DoesShifts=true, member of Hazon only):
///     • 2 shift assignments in Hazon (ShiftTypeH)
///
/// Period: fully past (2026-03-01..2026-03-31) so endCap == PeriodEnd.
///
/// Assertions when ShiftCategoryId = Yekev:
///   1. Rows include U1 (Yekev member) and exclude U2 (Hazon member only).
///   2. U1.Actual == 2 (only the 2 Yekev assignments; the Hazon one is excluded).
///
/// Also tests that without a category filter (ShiftCategoryId=null) both users appear
/// and U1.Actual == 3 (all 3 assignments).
/// </summary>
public class JusticeServiceCategoryTests : IDisposable
{
    // ------------------------------------------------------------------
    // Hierarchy IDs
    // ------------------------------------------------------------------
    private const int AreaId     = 1;
    private const int MoleculeId = 1;
    private const int CompanyId  = 1;

    // Categories
    private const int CatYekev = 10;
    private const int CatHazon = 11;

    // Shift types (each belonging to one category)
    private const int ShiftTypeYekev = 20;
    private const int ShiftTypeHazon = 21;

    // Shift instances
    private const int SIYekev1 = 100; // WorkDate 2026-03-10 (Yekev)
    private const int SIYekev2 = 101; // WorkDate 2026-03-11 (Yekev)
    private const int SIHazon1 = 102; // WorkDate 2026-03-12 (Hazon)
    private const int SIHazon2 = 103; // WorkDate 2026-03-13 (Hazon)

    // Users
    private const int U1 = 201; // DoesShifts=true, member of Yekev
    private const int U2 = 202; // DoesShifts=true, member of Hazon

    // UserShiftCategory memberships
    private const int UscU1Yekev = 300;
    private const int UscU2Hazon = 301;

    // Period in the past so endCap = PeriodEnd (all actuals count)
    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd   = new(2026, 3, 31);

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceCategoryTests()
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

    // ------------------------------------------------------------------
    // Seed helpers
    // ------------------------------------------------------------------

    private void SeedAll()
    {
        _db.Areas.Add(new Area     { Id = AreaId,     ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1",  DisplayName = "Mol1" });
        _db.Companies.Add(new Company  { Id = CompanyId,  MoleculeId = MoleculeId, Name = "Co1",  DisplayName = "Co1" });

        // ShiftCategories
        _db.ShiftCategories.AddRange(
            new ShiftCategory { Id = CatYekev, MoleculeId = MoleculeId, Name = "Yekev", DisplayName = "Yekev", SortOrder = 1, IsActive = true },
            new ShiftCategory { Id = CatHazon, MoleculeId = MoleculeId, Name = "Hazon", DisplayName = "Hazon", SortOrder = 2, IsActive = true });

        // ShiftTypes — each belongs to one category
        _db.ShiftTypes.AddRange(
            new ShiftType { Id = ShiftTypeYekev, Key = "YEK_M", CompanyId = CompanyId, Scope = ShiftScope.Company, CategoryId = CatYekev },
            new ShiftType { Id = ShiftTypeHazon, Key = "HAZ_M", CompanyId = CompanyId, Scope = ShiftScope.Company, CategoryId = CatHazon });

        // ShiftInstances
        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = SIYekev1, CompanyId = CompanyId, ShiftTypeId = ShiftTypeYekev, WorkDate = new DateOnly(2026, 3, 10), StaffingRequired = 2 },
            new ShiftInstance { Id = SIYekev2, CompanyId = CompanyId, ShiftTypeId = ShiftTypeYekev, WorkDate = new DateOnly(2026, 3, 11), StaffingRequired = 2 },
            new ShiftInstance { Id = SIHazon1, CompanyId = CompanyId, ShiftTypeId = ShiftTypeHazon, WorkDate = new DateOnly(2026, 3, 12), StaffingRequired = 2 },
            new ShiftInstance { Id = SIHazon2, CompanyId = CompanyId, ShiftTypeId = ShiftTypeHazon, WorkDate = new DateOnly(2026, 3, 13), StaffingRequired = 2 });

        // Users
        _db.Users.AddRange(
            new AppUser
            {
                Id = U1, Email = "u1@test.com", DisplayName = "UserOne",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                AccountType = AccountType.Standard, DoesShifts = true
            },
            new AppUser
            {
                Id = U2, Email = "u2@test.com", DisplayName = "UserTwo",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                AccountType = AccountType.Standard, DoesShifts = true
            });

        // UserShiftCategory memberships
        _db.UserShiftCategories.AddRange(
            new UserShiftCategory { Id = UscU1Yekev, UserId = U1, ShiftCategoryId = CatYekev },
            new UserShiftCategory { Id = UscU2Hazon, UserId = U2, ShiftCategoryId = CatHazon });

        _db.SaveChanges();

        // Shift assignments:
        //   U1: 2 Yekev shifts (SIYekev1, SIYekev2) + 1 Hazon shift (SIHazon1)
        //   U2: 2 Hazon shifts (SIHazon1, SIHazon2)
        _db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 1, CompanyId = CompanyId, ShiftInstanceId = SIYekev1, UserId = U1 },
            new ShiftAssignment { Id = 2, CompanyId = CompanyId, ShiftInstanceId = SIYekev2, UserId = U1 },
            new ShiftAssignment { Id = 3, CompanyId = CompanyId, ShiftInstanceId = SIHazon1, UserId = U1 }, // must be excluded when filtering Yekev
            new ShiftAssignment { Id = 4, CompanyId = CompanyId, ShiftInstanceId = SIHazon1, UserId = U2 },
            new ShiftAssignment { Id = 5, CompanyId = CompanyId, ShiftInstanceId = SIHazon2, UserId = U2 });

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

    private static JusticeQuery MakeQuery(int? shiftCategoryId) => new JusticeQuery(
        Scope:               JusticeScope.Company,
        ScopeId:             CompanyId,
        PeriodStart:         PeriodStart,
        PeriodEnd:           PeriodEnd,
        WorkType:            JusticeWorkType.Shift,
        ExcludeExemptShifts: false,
        Level:               JusticeLevel.UsersInCompany,
        ShiftCategoryId:     shiftCategoryId);

    // ==================================================================
    // Category filter — user set
    // ==================================================================

    /// <summary>
    /// When ShiftCategoryId = Yekev, only U1 (Yekev member) must appear.
    /// U2 (Hazon-only member) must be excluded from the rows.
    /// </summary>
    [Fact]
    public async Task CategoryFilter_Yekev_IncludesU1_ExcludesU2()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(CatYekev), CancellationToken.None);

        var rowIds = vm.Rows.Select(r => r.Id).ToList();

        rowIds.Should().Contain(U1, "U1 is a Yekev member and must appear when filtering to Yekev");
        rowIds.Should().NotContain(U2, "U2 is a Hazon-only member and must be excluded when filtering to Yekev");
    }

    /// <summary>
    /// When ShiftCategoryId = Yekev, U1's Actual must be 2 (only the 2 Yekev shifts),
    /// NOT 3 (which would include U1's Hazon shift assignment).
    /// </summary>
    [Fact]
    public async Task CategoryFilter_Yekev_U1ActualCountsOnlyYekvShifts()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(CatYekev), CancellationToken.None);

        var rowU1 = vm.Rows.Single(r => r.Id == U1);

        rowU1.Actual.Should().Be(2m,
            "U1 has 2 Yekev shifts and 1 Hazon shift; the Hazon shift must not be counted when filtering to Yekev");
    }

    // ==================================================================
    // Category filter — Hazon
    // ==================================================================

    /// <summary>
    /// When ShiftCategoryId = Hazon, only U2 appears (U2 is the Hazon member).
    /// U1 is NOT a Hazon member so is excluded.
    /// </summary>
    [Fact]
    public async Task CategoryFilter_Hazon_IncludesU2_ExcludesU1()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(CatHazon), CancellationToken.None);

        var rowIds = vm.Rows.Select(r => r.Id).ToList();

        rowIds.Should().Contain(U2, "U2 is a Hazon member and must appear when filtering to Hazon");
        rowIds.Should().NotContain(U1, "U1 is not a Hazon member and must be excluded when filtering to Hazon");
    }

    // ==================================================================
    // No category filter (null) — baseline behavior unchanged
    // ==================================================================

    /// <summary>
    /// Without a category filter both U1 and U2 appear, confirming the null path is unchanged.
    /// </summary>
    [Fact]
    public async Task NoCategoryFilter_BothUsersAppear()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(shiftCategoryId: null), CancellationToken.None);

        var rowIds = vm.Rows.Select(r => r.Id).ToList();

        rowIds.Should().Contain(U1, "U1 must appear when no category filter is applied");
        rowIds.Should().Contain(U2, "U2 must appear when no category filter is applied");
    }

    /// <summary>
    /// Without a category filter U1.Actual = 3 (2 Yekev + 1 Hazon shifts all counted).
    /// </summary>
    [Fact]
    public async Task NoCategoryFilter_U1ActualCountsAllShifts()
    {
        var vm = await _service.GetJusticeViewAsync(MakeQuery(shiftCategoryId: null), CancellationToken.None);

        var rowU1 = vm.Rows.Single(r => r.Id == U1);

        rowU1.Actual.Should().Be(3m,
            "U1 has 2 Yekev + 1 Hazon shift assignment; all 3 count when no category filter is active");
    }
}
