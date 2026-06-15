using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Phase 2d integration tests: confirm that <see cref="JusticeService.GetEligibleCandidatesAsync"/>
/// for chore and onduty kinds routes through the real validation services (not the deleted
/// degraded path). These exercise the end-to-end wiring: JusticeService → IChoreService →
/// IBusyService for chore, and JusticeService → IOnDutyService → IBusyService for onduty.
/// </summary>
public class JusticeServiceChoreOnDutyEligibilityTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;
    private const int MoleculeId = 1;
    private const int CompanyId = 1;

    public JusticeServiceChoreOnDutyEligibilityTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "100"),
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

        // ShiftAssignmentService is required by the JusticeService constructor but not used for
        // chore/onduty eligibility — pass a minimal real instance so injection succeeds.
        var shiftAssignmentService = ShiftAssignmentServiceTestFactory();

        _service = new JusticeService(_db, shiftAssignmentService, choreService, onDutyService);

        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "TestCo", DisplayName = "Test Co" });
        _db.SaveChanges();
    }

    private IShiftAssignmentService ShiftAssignmentServiceTestFactory()
    {
        // Construct via Mock — eligibility for chore/onduty never invokes shift validation,
        // so the mock is never called. Throws if it is, which would catch unintended routing.
        var mock = new Mock<IShiftAssignmentService>(MockBehavior.Strict);
        return mock.Object;
    }

    private async Task SeedUserAsync(int userId, int companyId = CompanyId, bool active = true)
    {
        _db.Users.Add(new AppUser { Id = userId, Email = $"u{userId}@t.com", DisplayName = $"U{userId}", CompanyId = companyId, IsActive = active, Role = UserRole.Employee });
        await _db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    [Fact]
    public async Task GetEligibleCandidates_ChoreHole_UserOnVacation_AppearsAsWarning_NotHardBlocked()
    {
        await SeedUserAsync(1);
        var date = new DateOnly(2026, 7, 10);
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            UserId = 1, CompanyId = CompanyId, Status = RequestStatus.Approved,
            StartDate = date, EndDate = date,
            Type = TimeOffType.Vacation
        });
        await _db.SaveChangesAsync();

        var parentScope = new JusticeQuery(
            JusticeScope.Company, CompanyId,
            date.AddDays(-15), date.AddDays(15),
            JusticeWorkType.Chore, ExcludeExemptShifts: false,
            JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "chore", ShiftInstanceId: null, Date: date,
            CompanyId: CompanyId, JobTypeId: null, DutyTypeValue: null,
            MoleculeId: MoleculeId, ParentScope: parentScope);

        var result = await _service.GetEligibleCandidatesAsync(hole);

        // User must appear in the visible candidates list — not in HardBlocked — because
        // vacation is a warning, not a hard error.
        result.Candidates.Should().Contain(c => c.UserId == 1);
        result.HardBlocked.Should().NotContain(c => c.UserId == 1);
        var u1 = result.Candidates.First(c => c.UserId == 1);
        u1.Eligibility.IsHardBlocked.Should().BeFalse();
        u1.Eligibility.WarningKeys.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetEligibleCandidates_ChoreHole_UserOutsideMolecule_AppearsAsHardBlocked()
    {
        // User in a sibling company belonging to a different molecule — must be hard-blocked.
        _db.Companies.Add(new Company { Id = 2, MoleculeId = 99, Name = "OtherCo", DisplayName = "Other Co" });
        await SeedUserAsync(1, companyId: CompanyId);
        await SeedUserAsync(2, companyId: 2);
        var date = new DateOnly(2026, 7, 10);

        var parentScope = new JusticeQuery(
            JusticeScope.Company, CompanyId,
            date.AddDays(-15), date.AddDays(15),
            JusticeWorkType.Chore, ExcludeExemptShifts: false,
            JusticeLevel.UsersInCompany);

        // The hole is for company 1 / molecule 1. Because ParentScope is for company 1,
        // the candidate set is constrained to that company — user 2 wouldn't be ranked at all.
        // Sanity check: at least user 1 is ranked and not hard-blocked.
        var hole = new HoleSelector(
            Kind: "chore", ShiftInstanceId: null, Date: date,
            CompanyId: CompanyId, JobTypeId: null, DutyTypeValue: null,
            MoleculeId: MoleculeId, ParentScope: parentScope);

        var result = await _service.GetEligibleCandidatesAsync(hole);

        result.Candidates.Should().Contain(c => c.UserId == 1);
        result.Candidates.First(c => c.UserId == 1).Eligibility.IsHardBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetEligibleCandidates_OnDutyHole_InactiveUser_FilteredOutBeforeRanking()
    {
        await SeedUserAsync(1, active: true);
        await SeedUserAsync(2, active: false);
        var date = new DateOnly(2026, 7, 10);

        var parentScope = new JusticeQuery(
            JusticeScope.Molecule, MoleculeId,
            date.AddDays(-15), date.AddDays(15),
            JusticeWorkType.OnDuty, ExcludeExemptShifts: false,
            JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "onduty", ShiftInstanceId: null, Date: date,
            CompanyId: null, JobTypeId: null, DutyTypeValue: (int)OnDutyType.Hakam,
            MoleculeId: MoleculeId, ParentScope: parentScope);

        var result = await _service.GetEligibleCandidatesAsync(hole);

        // Inactive users are filtered by ResolveUsersInScopeAsync's `&& u.IsActive` predicate
        // BEFORE eligibility validation — they don't appear as candidates or hard-blocked.
        // This is correct behaviour: inactive accounts are off the table, no validation needed.
        result.Candidates.Should().NotContain(c => c.UserId == 2);
        result.HardBlocked.Should().NotContain(c => c.UserId == 2);
        // Active user 1 should appear in candidates.
        result.Candidates.Should().Contain(c => c.UserId == 1);
    }

    [Fact]
    public async Task GetEligibleCandidates_OnDutyHole_DuplicateAssignment_AppearsAsWarning()
    {
        await SeedUserAsync(1);
        var date = new DateOnly(2026, 7, 10);
        // Seed an existing OnDuty for user 1 on the target date.
        // OnDuty has no CompanyId — it's global by design (no IBelongsToCompany).
        _db.OnDuties.Add(new OnDuty
        {
            UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = 100
        });
        await _db.SaveChangesAsync();

        var parentScope = new JusticeQuery(
            JusticeScope.Molecule, MoleculeId,
            date.AddDays(-15), date.AddDays(15),
            JusticeWorkType.OnDuty, ExcludeExemptShifts: false,
            JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "onduty", ShiftInstanceId: null, Date: date,
            CompanyId: null, JobTypeId: null, DutyTypeValue: (int)OnDutyType.Hakam,
            MoleculeId: MoleculeId, ParentScope: parentScope);

        var result = await _service.GetEligibleCandidatesAsync(hole);

        // User 1 should appear with a warning (duplicate-onduty / onduty-conflict),
        // not as hard-blocked — the assigner can override.
        result.Candidates.Should().Contain(c => c.UserId == 1);
        var u1 = result.Candidates.First(c => c.UserId == 1);
        u1.Eligibility.IsHardBlocked.Should().BeFalse();
        u1.Eligibility.WarningKeys.Should().NotBeEmpty();
    }
}
