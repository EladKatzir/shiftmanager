using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Task #9.3 — /Calendar/Team: "team" = the users of a chosen (Company × JobType), rendered via
/// the SAME <see cref="IOverviewCalendarBuilder"/> as /Calendar/Overview, with a Company+JobType
/// picker (scope-limited to the caller's "ViewShifts" grant) and saved private
/// <see cref="DeskTeamView"/> tables.
///
/// Security focus (design doc #9.3/#9.4 — anti-IDOR):
///  (a) the row filter only ever returns Standard users of the resolved (Company × JobType);
///  (b) a crafted/out-of-scope SelectedCompanyId query param is hard-rejected (Forbid) — the
///      picker itself never offers an out-of-scope option, so reaching this path means direct
///      URL tampering;
///  (c) opening a saved DeskTeamView whose TargetCompanyId has fallen out of the caller's
///      CURRENT ViewShifts scope (grant revoked after the view was saved) must not render that
///      company's data — this is graceful (falls back to the caller's own company + a flag), not
///      a hard Forbid, because it is a normal lifecycle event, not an attack;
///  (d) OnPostAddViewAsync pre-checks the DB's unique (CompanyId,OwnerId,Name) constraint so a
///      duplicate name is a clean 400, never a raw DbUpdateException/500;
///  (e) add → list → delete round-trips through the real (mocked-dependency) handlers.
///
/// The grant service is MOCKED (not real GrantService + real hierarchy) so each test can pin an
/// exact accessible-company set without seeding a full Project/Area/Molecule/Grant chain —
/// TeamModel's OWN scope-check logic (not GrantService's cascade, which has its own test suite)
/// is what's under test here.
/// </summary>
public sealed class TeamPageTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        return loc;
    }

    /// <summary>
    /// Builds a TeamModel wired to the shared real SQLite `_db`, a mocked IGrantService pinned to
    /// <paramref name="accessibleCompanyIds"/> for "ViewShifts", a mocked IJobTypeService (configure
    /// per-test via the returned mock), a fixed "own company" tenant, and a REAL
    /// DeskTeamViewService (already proven correct by DeskTeamViewServiceTests) so the saved-view
    /// handlers exercise real CRUD/uniqueness behavior, not a mock's assumptions.
    /// </summary>
    private (TeamModel Model, Mock<IGrantService> GrantMock, Mock<IJobTypeService> JobTypeMock) BuildModel(
        int callerId, int ownCompanyId, List<int> accessibleCompanyIds)
    {
        var grantMock = new Mock<IGrantService>();
        grantMock.Setup(g => g.GetAccessibleCompanyIdsForGrantAsync(callerId, "ViewShifts"))
            .ReturnsAsync(accessibleCompanyIds);

        var jobTypeMock = new Mock<IJobTypeService>();
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<JobType>());

        var tenantResolverMock = new Mock<ITenantResolver>();
        tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(ownCompanyId);

        // Renders rows 1:1 from whatever user set TeamModel resolved — lets tests assert exactly
        // which users reached the renderer (the security-relevant surface) without needing to
        // seed shifts/vacations/chores (OverviewCalendarBuilder's own concern, already covered by
        // OverviewCalendarBuilderTests).
        var builderMock = new Mock<IOverviewCalendarBuilder>();
        builderMock
            .Setup(b => b.BuildAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<AppUser>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((int companyId, IReadOnlyList<AppUser> users, DateOnly start, DateOnly end, string viewMode, bool canEditNotes) =>
                new ExcelCalendarTableViewModel
                {
                    StartDate = start,
                    EndDate = end,
                    ViewMode = viewMode,
                    IsReadOnly = !canEditNotes,
                    CalendarType = "overview",
                    RowMode = "Shifts",
                    Rows = users.Select(u => new ExcelCalendarRow { Id = $"user-{u.Id}", Label = u.DisplayName }).ToList()
                });

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var deskService = new DeskTeamViewService(_db, tenantResolverMock.Object, httpContextAccessorMock.Object);

        var model = new TeamModel(
            _db,
            grantMock.Object,
            jobTypeMock.Object,
            tenantResolverMock.Object,
            builderMock.Object,
            deskService,
            BuildLocalizer().Object,
            NullLogger<TeamModel>.Instance);

        model.PageContext = new PageContext { HttpContext = httpContext };

        return (model, grantMock, jobTypeMock);
    }

    private async Task<Company> SeedCompanyAsync(int id, int moleculeId, string name)
    {
        var company = new Company { Id = id, Name = name, DisplayName = name, MoleculeId = moleculeId };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        return company;
    }

    private async Task<AppUser> SeedUserAsync(int id, int companyId, int? jobTypeId, string name,
        AccountType accountType = AccountType.Standard, bool isActive = true)
    {
        var user = new AppUser
        {
            Id = id,
            CompanyId = companyId,
            JobTypeId = jobTypeId,
            Email = $"user{id}@test.local",
            DisplayName = name,
            Role = UserRole.Employee,
            IsActive = isActive,
            AccountType = accountType
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>Directly inserts a DeskTeamView row — bypasses the service (already covered by
    /// DeskTeamViewServiceTests) so tests here can seed a saved view without depending on it.</summary>
    private async Task<DeskTeamView> SeedViewAsync(int ownerId, int companyId, int targetCompanyId, int jobTypeId, string name)
    {
        var view = new DeskTeamView
        {
            OwnerId = ownerId,
            CompanyId = companyId,
            TargetCompanyId = targetCompanyId,
            JobTypeId = jobTypeId,
            Name = name,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.DeskTeamViews.Add(view);
        await _db.SaveChangesAsync();
        return view;
    }

    // ---------------------------------------------------------------------------------------
    // (a) Row filter: only Standard users of the chosen (Company × JobType)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_ShowsOnlyStandardUsersOfChosenCompanyAndJobType()
    {
        const int CallerId = 601, CompanyId = 5, AlhutJobTypeId = 2, TechJobTypeId = 3, LeadJobTypeId = 99;
        await SeedCompanyAsync(CompanyId, moleculeId: 1, "Tzafona");
        // The lead viewing the page has their OWN job type (not Alhut) — proves the roster is the
        // (Company × JobType) row set, not "the caller's teammates by some other relation", and
        // that the caller isn't trivially included just by virtue of viewing the page.
        await SeedUserAsync(CallerId, CompanyId, LeadJobTypeId, "Caller");

        var alice = await SeedUserAsync(101, CompanyId, AlhutJobTypeId, "Alice"); // in scope
        var bob = await SeedUserAsync(102, CompanyId, AlhutJobTypeId, "Bob");     // in scope
        await SeedUserAsync(103, CompanyId, TechJobTypeId, "Carol");              // wrong job type — excluded
        await SeedUserAsync(104, CompanyId, AlhutJobTypeId, "MilDave", AccountType.Mil); // wrong account type — excluded

        var (model, _, jobTypeMock) = BuildModel(CallerId, CompanyId, accessibleCompanyIds: new List<int> { CompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(1))
            .ReturnsAsync(new List<JobType> { new() { Id = AlhutJobTypeId, Name = "Alhut", AreaId = 1 }, new() { Id = TechJobTypeId, Name = "Tech", AreaId = 1 } });

        model.SelectedCompanyId = CompanyId;
        model.SelectedJobTypeId = AlhutJobTypeId;

        var result = await model.OnGetAsync();

        result.Should().BeOfType<PageResult>();
        model.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { alice.Id, bob.Id });
        model.CalendarData.Rows.Select(r => r.Id).Should().BeEquivalentTo(new[] { $"user-{alice.Id}", $"user-{bob.Id}" });
    }

    /// <summary>
    /// Deactivated/locked users must never appear on Team, matching Overview's active-only
    /// default — a deactivated Standard user of the chosen (Company × JobType) is excluded from
    /// both the row set and the rendered calendar rows, while an active teammate still shows.
    /// </summary>
    [Fact]
    public async Task Get_ExcludesInactiveUsers()
    {
        const int CallerId = 610, CompanyId = 5, JobTypeId = 2;
        await SeedCompanyAsync(CompanyId, moleculeId: 1, "Tzafona");
        await SeedUserAsync(CallerId, CompanyId, JobTypeId, "Caller");

        var active = await SeedUserAsync(701, CompanyId, JobTypeId, "ActiveUser");
        var inactive = await SeedUserAsync(702, CompanyId, JobTypeId, "InactiveUser", isActive: false);

        var (model, _, jobTypeMock) = BuildModel(CallerId, CompanyId, accessibleCompanyIds: new List<int> { CompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(1))
            .ReturnsAsync(new List<JobType> { new() { Id = JobTypeId, Name = "Alhut", AreaId = 1 } });

        model.SelectedCompanyId = CompanyId;
        model.SelectedJobTypeId = JobTypeId;

        var result = await model.OnGetAsync();

        result.Should().BeOfType<PageResult>();
        model.Users.Select(u => u.Id).Should().Contain(active.Id);
        model.Users.Select(u => u.Id).Should().NotContain(inactive.Id,
            "a deactivated/locked user must never appear on Team, matching Overview's active-only default");
        model.CalendarData.Rows.Should().NotContain(r => r.Id == $"user-{inactive.Id}");
    }

    // ---------------------------------------------------------------------------------------
    // (b) Crafted out-of-scope SelectedCompanyId → hard rejection, nothing loaded
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_ForCompanyOutsideViewShiftsScope_IsRejected()
    {
        const int CallerId = 602, OwnCompanyId = 5, OtherCompanyId = 6, JobTypeId = 2;
        await SeedCompanyAsync(OwnCompanyId, moleculeId: 1, "Tzafona");
        await SeedCompanyAsync(OtherCompanyId, moleculeId: 2, "Golan");
        await SeedUserAsync(CallerId, OwnCompanyId, JobTypeId, "Caller");
        var secretUser = await SeedUserAsync(201, OtherCompanyId, JobTypeId, "OutOfScopeUser");

        // Caller's ViewShifts scope covers ONLY their own company — company 6 is not in it.
        var (model, _, _) = BuildModel(CallerId, OwnCompanyId, accessibleCompanyIds: new List<int> { OwnCompanyId });

        model.SelectedCompanyId = OtherCompanyId; // crafted query param
        model.SelectedJobTypeId = JobTypeId;

        var result = await model.OnGetAsync();

        result.Should().BeOfType<ForbidResult>("SelectedCompanyId is outside the caller's ViewShifts scope");
        model.Users.Should().BeEmpty("no user data must be loaded once the scope check has failed");
        model.Users.Should().NotContain(u => u.Id == secretUser.Id);
    }

    // ---------------------------------------------------------------------------------------
    // (c) Saved DeskTeamView whose TargetCompanyId has fallen out of scope → not rendered
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task RenderSavedView_AfterScopeRevoked_IsRejected()
    {
        const int CallerId = 603, OwnCompanyId = 5, RevokedCompanyId = 6, JobTypeId = 9;
        await SeedCompanyAsync(OwnCompanyId, moleculeId: 1, "Tzafona");
        await SeedCompanyAsync(RevokedCompanyId, moleculeId: 2, "Golan");
        await SeedUserAsync(CallerId, OwnCompanyId, JobTypeId, "Caller");
        var secretUser = await SeedUserAsync(301, RevokedCompanyId, JobTypeId, "NoLongerVisible");

        // The view was saved while company 6 was still accessible...
        var staleView = await SeedViewAsync(CallerId, OwnCompanyId, RevokedCompanyId, JobTypeId, "Alhut Elsewhere");

        // ...but by request time the grant has been revoked: scope is ONLY the caller's own company.
        var (model, _, jobTypeMock) = BuildModel(CallerId, OwnCompanyId, accessibleCompanyIds: new List<int> { OwnCompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(1))
            .ReturnsAsync(new List<JobType> { new() { Id = JobTypeId, Name = "Alhut", AreaId = 1 } });

        model.ViewId = staleView.Id;

        var result = await model.OnGetAsync();

        // Graceful, not a hard Forbid: opening a stale saved view is a normal lifecycle event
        // (an admin revoked the grant), not an attack — the rest of the page (own company) still works.
        result.Should().BeOfType<PageResult>();
        model.RequestedViewInaccessible.Should().BeTrue();
        model.SelectedCompanyId.Should().Be(OwnCompanyId, "the revoked company must never be selected, even transiently");
        model.Users.Should().NotContain(u => u.Id == secretUser.Id);
        model.CalendarData.Rows.Should().NotContain(r => r.Id == $"user-{secretUser.Id}");
    }

    // ---------------------------------------------------------------------------------------
    // Self-review addition: OnPostAddViewAsync's OWN scope re-check (brief step 6 — a POST
    // handler is a separate authorization context from the GET that rendered the form; the
    // picker's SelectedCompanyId must be re-validated here too, not just trusted from the page).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AddView_ForCompanyOutsideViewShiftsScope_IsRejected()
    {
        const int CallerId = 609, OwnCompanyId = 5, OtherCompanyId = 6, JobTypeId = 2;
        await SeedCompanyAsync(OwnCompanyId, moleculeId: 1, "Tzafona");
        await SeedCompanyAsync(OtherCompanyId, moleculeId: 2, "Golan");
        await SeedUserAsync(CallerId, OwnCompanyId, JobTypeId, "Caller");

        var (model, _, _) = BuildModel(CallerId, OwnCompanyId, accessibleCompanyIds: new List<int> { OwnCompanyId });
        // Simulates a stale form / tampered request: the picker claims a company outside scope.
        model.SelectedCompanyId = OtherCompanyId;
        model.SelectedJobTypeId = JobTypeId;

        var result = await model.OnPostAddViewAsync("Sneaky Table");

        result.Should().BeOfType<JsonResult>();
        ((JsonResult)result).StatusCode.Should().Be(403);
        (await _db.DeskTeamViews.CountAsync(v => v.Name == "Sneaky Table")).Should().Be(0,
            "no DeskTeamView must ever be created for a company outside the caller's ViewShifts scope");
    }

    // ---------------------------------------------------------------------------------------
    // (d) Duplicate saved-view name → clean validation error, not a raw exception
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AddView_WithDuplicateName_ReturnsCleanError()
    {
        const int CallerId = 604, CompanyId = 5, JobTypeId = 2;
        await SeedCompanyAsync(CompanyId, moleculeId: 1, "Tzafona");
        await SeedUserAsync(CallerId, CompanyId, JobTypeId, "Caller");
        await SeedViewAsync(CallerId, CompanyId, CompanyId, JobTypeId, "Alhut Tzafona");

        var (model, _, _) = BuildModel(CallerId, CompanyId, accessibleCompanyIds: new List<int> { CompanyId });
        model.SelectedCompanyId = CompanyId;
        model.SelectedJobTypeId = JobTypeId;

        var result = await model.OnPostAddViewAsync("Alhut Tzafona");

        result.Should().BeOfType<JsonResult>();
        var json = (JsonResult)result;
        json.StatusCode.Should().Be(400);
        (await _db.DeskTeamViews.CountAsync(v => v.Name == "Alhut Tzafona" && !v.IsDeleted)).Should().Be(1,
            "the duplicate must be rejected before ever reaching the DB — no second row created");
    }

    // ---------------------------------------------------------------------------------------
    // (e) add -> list -> delete round-trip
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AddThenListThenDelete_RoundTrips()
    {
        const int CallerId = 605, CompanyId = 5, JobTypeId = 2;
        await SeedCompanyAsync(CompanyId, moleculeId: 1, "Tzafona");
        await SeedUserAsync(CallerId, CompanyId, JobTypeId, "Caller");

        var (model, _, jobTypeMock) = BuildModel(CallerId, CompanyId, accessibleCompanyIds: new List<int> { CompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(1))
            .ReturnsAsync(new List<JobType> { new() { Id = JobTypeId, Name = "Alhut", AreaId = 1 } });
        model.SelectedCompanyId = CompanyId;
        model.SelectedJobTypeId = JobTypeId;

        var addResult = await model.OnPostAddViewAsync("New Table");
        addResult.Should().BeOfType<JsonResult>();
        var addStatusCode = ((JsonResult)addResult).StatusCode;
        (addStatusCode is null or 200).Should().BeTrue("a successful add must not report an error status code");

        await model.OnGetAsync();
        model.SavedViews.Should().ContainSingle(v => v.Name == "New Table");
        var createdId = model.SavedViews.Single(v => v.Name == "New Table").Id;

        var deleteResult = await model.OnPostDeleteViewAsync(createdId);
        deleteResult.Should().BeOfType<JsonResult>();

        await model.OnGetAsync();
        model.SavedViews.Should().NotContain(v => v.Id == createdId);
    }

    // ---------------------------------------------------------------------------------------
    // ALLOW-path coverage (guards against over-blocking — the actual feature must still work)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_NoQueryParams_DefaultsToOwnCompanyAndOwnJobType()
    {
        const int CallerId = 606, CompanyId = 5, JobTypeId = 2;
        await SeedCompanyAsync(CompanyId, moleculeId: 1, "Tzafona");
        await SeedUserAsync(CallerId, CompanyId, JobTypeId, "Caller");
        var teammate = await SeedUserAsync(401, CompanyId, JobTypeId, "Teammate");

        var (model, _, jobTypeMock) = BuildModel(CallerId, CompanyId, accessibleCompanyIds: new List<int> { CompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(1))
            .ReturnsAsync(new List<JobType> { new() { Id = JobTypeId, Name = "Alhut", AreaId = 1 } });

        var result = await model.OnGetAsync();

        result.Should().BeOfType<PageResult>();
        model.SelectedCompanyId.Should().Be(CompanyId);
        model.SelectedJobTypeId.Should().Be(JobTypeId);
        model.Users.Should().Contain(u => u.Id == teammate.Id);
    }

    [Fact]
    public async Task Get_SwitchingToAnotherAccessibleCompany_Succeeds()
    {
        // The Alhut-lead-in-Tzafona scenario from the design doc: an in-scope company switch must work.
        const int CallerId = 607, HomeCompanyId = 5, OtherCompanyId = 7, JobTypeId = 2;
        await SeedCompanyAsync(HomeCompanyId, moleculeId: 1, "Tzafona");
        await SeedCompanyAsync(OtherCompanyId, moleculeId: 3, "Golan");
        await SeedUserAsync(CallerId, HomeCompanyId, JobTypeId, "Caller");
        var otherTeammate = await SeedUserAsync(501, OtherCompanyId, JobTypeId, "GolanAlhutSoldier");

        var (model, _, jobTypeMock) = BuildModel(CallerId, HomeCompanyId, accessibleCompanyIds: new List<int> { HomeCompanyId, OtherCompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(3))
            .ReturnsAsync(new List<JobType> { new() { Id = JobTypeId, Name = "Alhut", AreaId = 1 } });

        model.SelectedCompanyId = OtherCompanyId;
        model.SelectedJobTypeId = JobTypeId;

        var result = await model.OnGetAsync();

        result.Should().NotBeOfType<ForbidResult>("company 7 IS within the caller's ViewShifts scope");
        model.Users.Should().Contain(u => u.Id == otherTeammate.Id);
    }

    [Fact]
    public async Task Get_RowOrderContextKey_IsTeamNamespaced()
    {
        // #9.3: Team ordering must be independent from Overview's "overview:{companyId}" namespace.
        const int CallerId = 608, CompanyId = 5, JobTypeId = 2;
        await SeedCompanyAsync(CompanyId, moleculeId: 1, "Tzafona");
        await SeedUserAsync(CallerId, CompanyId, JobTypeId, "Caller");

        var (model, _, jobTypeMock) = BuildModel(CallerId, CompanyId, accessibleCompanyIds: new List<int> { CompanyId });
        jobTypeMock.Setup(j => j.GetJobTypesForMoleculeAsync(1))
            .ReturnsAsync(new List<JobType> { new() { Id = JobTypeId, Name = "Alhut", AreaId = 1 } });
        model.SelectedCompanyId = CompanyId;
        model.SelectedJobTypeId = JobTypeId;

        await model.OnGetAsync();

        model.CalendarData.RowOrderContextKey.Should().Be($"team:{CompanyId}:{JobTypeId}");
    }
}
