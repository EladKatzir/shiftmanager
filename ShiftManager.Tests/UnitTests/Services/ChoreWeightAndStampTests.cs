using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Weight-resolution (pure, frozen) + template-stamp (rotate vs fan-out, skip-on-hard-error,
/// one-per-day index, size cap) coverage. Stamp runs a real ChoreService over real SQLite with
/// permissive grant/director Moq stubs so CreateChoreAsync proceeds; eligibility hard-errors are
/// exercised via an officer-only chore type.
/// </summary>
public sealed class ChoreWeightAndStampTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private ChoreService _sut = null!;

    private const int Molecule = 1;
    private const int FreeTextTemplate = 300;
    private const int OfficerTemplate = 301;
    private const int OfficerChoreType = 200;

    // ----- pure resolver tests (no DB) -----
    [Fact]
    public void Weight_From_Times_When_Both_Present_Same_Day()
        => ChoreService.ResolveWeightMinutes(new TimeOnly(10, 0), new TimeOnly(14, 0), choreTypeDefaultWeight: 999)
            .Should().Be(240, "10:00-14:00 = 240m; explicit times win over the type default");

    [Fact]
    public void Weight_Falls_To_Type_Default_When_No_Times()
        => ChoreService.ResolveWeightMinutes(null, null, choreTypeDefaultWeight: 120).Should().Be(120);

    [Fact]
    public void Weight_Falls_To_480_When_No_Times_And_No_Type_Default()
        => ChoreService.ResolveWeightMinutes(null, null, choreTypeDefaultWeight: null)
            .Should().Be(ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES).And.Be(480);

    [Fact]
    public void Weight_Falls_Through_When_End_Not_After_Start()
        => ChoreService.ResolveWeightMinutes(new TimeOnly(22, 0), new TimeOnly(2, 0), choreTypeDefaultWeight: 333)
            .Should().Be(333, "midnight-crossing is out of scope → fall through to the type default");

    // ----- stamp tests (real ChoreService over SQLite) -----
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = Molecule, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = Molecule });
        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "u1@x.mil", DisplayName = "U1", IsActive = true,
                          AccountType = AccountType.Standard, Rank = MilitaryRank.Turai },
            new AppUser { Id = 11, CompanyId = 1, Email = "u2@x.mil", DisplayName = "U2", IsActive = true,
                          AccountType = AccountType.Standard, Rank = MilitaryRank.Turai });
        _db.ChoreTypes.Add(new ChoreType { Id = OfficerChoreType, MoleculeId = Molecule, Name = "Guard", DisplayName = "Guard", CreatedByUserId = 10 });
        await _db.SaveChangesAsync();

        _db.EligibilityRules.Add(new EligibilityRule
        {
            ChoreTypeId = OfficerChoreType,
            RuleKind = EligibilityRuleKind.RequiresOfficerRank, CreatedBy = 10
        });
        _db.ChoreTemplates.AddRange(
            new ChoreTemplate { Id = FreeTextTemplate, MoleculeId = Molecule, Name = "Daily", DefaultTitle = "Sweep", IsActive = true, CreatedBy = 10 },
            new ChoreTemplate { Id = OfficerTemplate, MoleculeId = Molecule, Name = "Guard", DefaultTitle = "Guard", ChoreTypeId = OfficerChoreType, IsActive = true, CreatedBy = 10 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var busy = BusyServiceMockFactory.Real(_db);

        // Permissive ChoreService collaborators (this suite asserts business behavior, not authz).
        var grant = new Mock<IGrantService>();
        grant.Setup(g => g.HasGrantAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);
        grant.Setup(g => g.HasGrantForCompanyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
        grant.Setup(g => g.GetAccessibleCompanyIdsForGrantAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new List<int> { 1 });

        var companyCache = new Mock<ICompanyCacheService>();
        companyCache.Setup(c => c.GetCompanyAsync(It.IsAny<int>()))
            .ReturnsAsync((int companyId) => new Company { Id = companyId, Name = "Co", Slug = "co", MoleculeId = Molecule });

        var http = new Mock<IHttpContextAccessor>();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "10") }, "test");
        http.Setup(h => h.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });

        _sut = new ChoreService(
            _db,
            Mock.Of<ITenantResolver>(),
            http.Object,
            Mock.Of<IDirectorService>(),
            grant.Object,
            NullLogger<ChoreService>.Instance,
            companyCache.Object,
            busy,
            new EligibilityEvaluator());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Stamp_RotateFalse_Assigns_Every_Assignee_Every_Matching_Date()
    {
        // Mon-Wed (2026-07-06..08), no weekday filter → 3 days × 2 users = 6 chores.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 8),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10, 11 },
            rotate: false);

        result.CreatedCount.Should().Be(6);
        result.SkippedCount.Should().Be(0);
        result.Created.Select(c => c.UserId).Distinct().Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task Stamp_RotateTrue_Round_Robins_One_Assignee_Per_Date()
    {
        // 4 days, rotate over [10,11] → 10,11,10,11.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 9),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10, 11 },
            rotate: true);

        result.CreatedCount.Should().Be(4);
        result.Created.Select(c => c.UserId).Should().ContainInOrder(10, 11, 10, 11);
    }

    [Fact]
    public async Task Stamp_Respects_Weekday_Filter()
    {
        // 2026-07-06 is Monday. Filter to Monday only over a Mon-Wed range → 1 day.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 8),
            weekdays: new[] { DayOfWeek.Monday },
            assigneeIds: new[] { 10 },
            rotate: false);

        result.CreatedCount.Should().Be(1);
        result.Created[0].Date.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public async Task Stamp_Skips_Hard_Error_Days_And_Reports_Them()
    {
        // Officer-only template; both users are enlisted → every (date,user) hard-errors and is skipped.
        var result = await _sut.StampTemplateAsync(
            OfficerTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 7),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10 },
            rotate: false);

        result.CreatedCount.Should().Be(0);
        result.SkippedCount.Should().Be(2);
        result.Skipped.Should().OnlyContain(s => s.ReasonKey == "ELIG_OFFICER_RANK");
    }

    [Fact]
    public async Task Stamp_Honors_One_Active_Chore_Per_User_Per_Day()
    {
        // First stamp creates a chore for user 10 on day 1; second stamp on the same day must skip
        // (the second create raises a CHORE_CONFLICT warning → BUSY_OVERRIDE_REQUIRED → skipped).
        var day = new DateOnly(2026, 7, 6);
        var first = await _sut.StampTemplateAsync(FreeTextTemplate, day, day, Array.Empty<DayOfWeek>(), new[] { 10 }, rotate: false);
        first.CreatedCount.Should().Be(1);

        var second = await _sut.StampTemplateAsync(FreeTextTemplate, day, day, Array.Empty<DayOfWeek>(), new[] { 10 }, rotate: false);
        second.CreatedCount.Should().Be(0);
        second.SkippedCount.Should().Be(1);
        second.Skipped[0].ReasonKey.Should().Be("BUSY_OVERRIDE_REQUIRED",
            "a same-day second chore is an overrideable warning; the stamp passes no token, so it is skipped");
    }

    [Fact]
    public async Task Created_Chore_Has_Frozen_Default_Weight()
    {
        var day = new DateOnly(2026, 7, 6);
        await _sut.StampTemplateAsync(FreeTextTemplate, day, day, Array.Empty<DayOfWeek>(), new[] { 10 }, rotate: false);
        var chore = await _db.Chores.IgnoreQueryFilters().SingleAsync();
        chore.WeightMinutes.Should().Be(480, "free-text template → no times, no type default → 480 fallback frozen at create");
    }

    [Fact]
    public async Task Stamp_Rejects_Too_Long_A_Span()
    {
        // 93-day inclusive span > 92-day cap → STAMP_TOO_LARGE, no work done.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 1), to: new DateOnly(2026, 10, 1),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10 },
            rotate: false);

        result.CreatedCount.Should().Be(0);
        result.Skipped.Should().ContainSingle().Which.ReasonKey.Should().Be("STAMP_TOO_LARGE");
    }

    [Fact]
    public async Task Stamp_Rejects_Too_Many_Prospective_Chores()
    {
        // 90-day span (within the day cap) × 10 assignees fan-out = 900 > 500 cap → STAMP_TOO_LARGE.
        var assignees = Enumerable.Range(1, 10).ToArray(); // ids 1..10 (only 10/11 exist, but the cap fires first)
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 1), to: new DateOnly(2026, 9, 28), // 90 days
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: assignees,
            rotate: false);

        result.CreatedCount.Should().Be(0);
        result.Skipped.Should().ContainSingle().Which.ReasonKey.Should().Be("STAMP_TOO_LARGE");
    }
}
