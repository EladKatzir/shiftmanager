using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// 3b router tests: <see cref="ShiftCandidateService"/> reads the company-level flag, dispatches to the
/// workforce/tech leaves, applies the null-category fork (sharedFallback vs noCategory + the
/// allowFallback escape hatch), and projects {id,name,companyName} + a reason. Covers the full reason
/// table from the plan.
/// </summary>
public sealed class ShiftCandidateServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IFeatureFlagService> _flags;
    private readonly ShiftCandidateService _router;

    private Molecule _mol = null!, _techMol = null!;
    private Company _c1 = null!, _t1 = null!;
    private ShiftCategory _catA = null!, _catT = null!;
    private ShiftType _stCat = null!, _stHome = null!, _stNullJob = null!, _stAssignableNull = null!, _stTechCat = null!;
    private AppUser _a = null!, _b = null!, _tu = null!;

    public ShiftCandidateServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Workforce leaf
        var localizer = Mock.Of<IStringLocalizer<SharedResources>>();
        var hierarchy = new Mock<IHierarchySettingsService>();
        hierarchy.Setup(x => x.GetEffectiveSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new EffectiveSettings(RestHours: 11, WeeklyCap: 48, RestHoursSource: "Area", WeeklyCapSource: "Area"));
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiKeyHmacSecret"]).Returns("test-hmac-secret-for-unit-tests");
        var workforce = new ShiftAssignmentService(
            _db, localizer, Mock.Of<ILogger<ShiftAssignmentService>>(), hierarchy.Object,
            Mock.Of<IAuditLogService>(), config.Object, Mock.Of<IAppConfigCacheService>(),
            BusyServiceMockFactory.Real(_db, config.Object, restHours: 11, weeklyCap: 48));

        // Tech leaf
        var calLocalization = new Mock<ICompanyLocalizationService>();
        calLocalization.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);
        var tech = new ShiftCalendarService(_db, Mock.Of<ILogger<ShiftCalendarService>>(),
            new Mock<ICompanyCacheService>().Object, calLocalization.Object);

        _flags = new Mock<IFeatureFlagService>();
        _router = new ShiftCandidateService(_db, workforce, tech, _flags.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private void Flag(bool enabled) =>
        _flags.Setup(f => f.IsEnabledAsync(FeatureFlagSeed.Flags.CategoryBasedShiftEligibility, It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(enabled);

    private async Task SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var job = new JobType { AreaId = area.Id, Name = "Alhut", DisplayName = "Alhut", SortOrder = 1 };
        _db.JobTypes.Add(job); await _db.SaveChangesAsync();

        _mol = new Molecule { AreaId = area.Id, Name = "M", Type = MoleculeType.Workforce };
        _techMol = new Molecule { AreaId = area.Id, Name = "TM", Type = MoleculeType.Tech };
        _db.Molecules.AddRange(_mol, _techMol); await _db.SaveChangesAsync();
        _c1 = new Company { MoleculeId = _mol.Id, Name = "C1", DisplayName = "C1" };
        _t1 = new Company { MoleculeId = _techMol.Id, Name = "T1", DisplayName = "T1" };
        _db.Companies.AddRange(_c1, _t1); await _db.SaveChangesAsync();

        _catA = new ShiftCategory { MoleculeId = _mol.Id, Name = "CatA", DisplayName = "CatA" };
        _catT = new ShiftCategory { MoleculeId = _techMol.Id, Name = "CatT", DisplayName = "CatT" };
        _db.ShiftCategories.AddRange(_catA, _catT); await _db.SaveChangesAsync();

        _stCat = new ShiftType { Scope = ShiftScope.Molecule, MoleculeId = _mol.Id, JobTypeId = job.Id, CategoryId = _catA.Id, Key = ShiftType.KEY_MORNING, Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0) };
        _stHome = new ShiftType { Scope = ShiftScope.Molecule, MoleculeId = _mol.Id, JobTypeId = job.Id, CategoryId = null, Key = "HOME", Start = new TimeOnly(0, 0), End = new TimeOnly(0, 0) };
        _stNullJob = new ShiftType { Scope = ShiftScope.Molecule, MoleculeId = _mol.Id, JobTypeId = null, CategoryId = null, Key = "MIDDLE", Start = new TimeOnly(12, 0), End = new TimeOnly(20, 0) };
        _stAssignableNull = new ShiftType { Scope = ShiftScope.Molecule, MoleculeId = _mol.Id, JobTypeId = job.Id, CategoryId = null, Key = "NOON", Start = new TimeOnly(10, 0), End = new TimeOnly(18, 0) };
        _stTechCat = new ShiftType { Scope = ShiftScope.Molecule, MoleculeId = _techMol.Id, TechShiftType = ShiftType.TECH_HANAVA, CategoryId = _catT.Id, RequiresOfficerRank = false, Key = "TECH_CAT", Start = new TimeOnly(8, 0), End = new TimeOnly(20, 0) };
        _db.ShiftTypes.AddRange(_stCat, _stHome, _stNullJob, _stAssignableNull, _stTechCat); await _db.SaveChangesAsync();

        _a = NewUser(_c1.Id, job.Id, "alpha", doesShifts: true);
        _b = NewUser(_c1.Id, job.Id, "bravo", doesShifts: true);
        _tu = NewUser(_t1.Id, job.Id, "techie", doesShifts: true);
        _db.Users.AddRange(_a, _b, _tu); await _db.SaveChangesAsync();

        // A in CatA; B not in CatA. TU in CatT.
        _db.UserShiftCategories.AddRange(
            new UserShiftCategory { UserId = _a.Id, ShiftCategoryId = _catA.Id },
            new UserShiftCategory { UserId = _tu.Id, ShiftCategoryId = _catT.Id });
        await _db.SaveChangesAsync();
    }

    private static AppUser NewUser(int companyId, int jobTypeId, string tag, bool doesShifts)
        => new AppUser
        {
            CompanyId = companyId, JobTypeId = jobTypeId, Email = tag + "@t.mil", DisplayName = tag,
            IsActive = true, DoesShifts = doesShifts, AccountType = AccountType.Standard,
            PasswordHash = Array.Empty<byte>(), PasswordSalt = Array.Empty<byte>()
        };

    [Fact]
    public async Task FlagOff_Returns_Legacy_Set_With_Reason_Category()
    {
        await SeedAsync();
        Flag(false);

        var result = await _router.GetEligibleCandidatesAsync(_mol.Id, _stCat.Id, currentCompanyId: _c1.Id);

        result.Reason.Should().Be("category");
        // Legacy workforce: jobType=Alhut, no category gate -> both A and B.
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { _a.Id, _b.Id });
    }

    [Fact]
    public async Task FlagOn_CategorySet_Returns_Members_Reason_Category()
    {
        await SeedAsync();
        Flag(true);

        var result = await _router.GetEligibleCandidatesAsync(_mol.Id, _stCat.Id, currentCompanyId: _c1.Id);

        result.Reason.Should().Be("category");
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { _a.Id });
        result.Users.Should().OnlyContain(u => u.CompanyName == "C1");
    }

    [Fact]
    public async Task FlagOn_NullCategory_HomeType_Returns_All_Reason_SharedFallback()
    {
        await SeedAsync();
        Flag(true);

        var result = await _router.GetEligibleCandidatesAsync(_mol.Id, _stHome.Id, currentCompanyId: _c1.Id);

        result.Reason.Should().Be("sharedFallback");
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { _a.Id, _b.Id });
    }

    [Fact]
    public async Task FlagOn_NullCategory_NullJobType_Returns_All_Reason_SharedFallback()
    {
        await SeedAsync();
        Flag(true);

        var result = await _router.GetEligibleCandidatesAsync(_mol.Id, _stNullJob.Id, currentCompanyId: _c1.Id);

        result.Reason.Should().Be("sharedFallback");
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { _a.Id, _b.Id });
    }

    [Fact]
    public async Task FlagOn_NullCategory_Assignable_NoFallback_Returns_Empty_Reason_NoCategory()
    {
        await SeedAsync();
        Flag(true);

        var result = await _router.GetEligibleCandidatesAsync(_mol.Id, _stAssignableNull.Id, currentCompanyId: _c1.Id);

        result.Reason.Should().Be("noCategory");
        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task FlagOn_NullCategory_Assignable_WithFallback_Returns_All_Reason_SharedFallback()
    {
        await SeedAsync();
        Flag(true);

        var result = await _router.GetEligibleCandidatesAsync(_mol.Id, _stAssignableNull.Id, currentCompanyId: _c1.Id, allowFallback: true);

        result.Reason.Should().Be("sharedFallback");
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { _a.Id, _b.Id });
    }

    [Fact]
    public async Task TechMolecule_Dispatches_To_Tech_Leaf_With_CompanyNames()
    {
        await SeedAsync();
        Flag(true);

        var result = await _router.GetEligibleCandidatesAsync(_techMol.Id, _stTechCat.Id, currentCompanyId: _t1.Id);

        result.Reason.Should().Be("category");
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(new[] { _tu.Id });
        result.Users.Should().OnlyContain(u => u.CompanyName == "T1");
    }
}
