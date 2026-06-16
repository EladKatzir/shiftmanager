using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// 3b leaf-method tests: category-aware eligibility behind the <c>categoryFilter</c> param. Verifies the
/// canonical rule (DoesShifts participant + single-category membership), MULTI-category users, the
/// null-category fallback, GroupUser exclusion, per-company DoesShifts (membership overrides mirror),
/// and that categoryFilter:false preserves the legacy jobType behavior. Tech cases (added in phase 2
/// task 3) keep officer-rank.
/// </summary>
public sealed class CategoryEligibilityLeafTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly ShiftAssignmentService _workforce;
    private readonly ShiftCalendarService _tech;

    // captured after seeding (workforce)
    private Molecule _molecule = null!;
    private Company _c1 = null!, _c2 = null!;
    private ShiftGrouping _grouping = null!;
    private JobType _jobAlhut = null!, _jobText = null!;
    private ShiftCategory _catA = null!, _catB = null!;
    private ShiftType _stCat = null!;   // CategoryId = catA, JobTypeId = Alhut
    private ShiftType _stNull = null!;  // CategoryId = null (shared, null jobType)
    private AppUser _a = null!, _b = null!, _c = null!, _d = null!, _e = null!, _f = null!, _g = null!;

    // captured after seeding (tech)
    private Molecule _techMol = null!;
    private ShiftType _stTechCat = null!;   // CategoryId = catT, RequiresOfficerRank=true
    private ShiftType _stTechNull = null!;   // CategoryId = null, RequiresOfficerRank=false
    private AppUser _officerMember = null!, _nonOfficerMember = null!, _officerNonMember = null!, _techGroupUser = null!;

    public CategoryEligibilityLeafTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var localizer = Mock.Of<IStringLocalizer<SharedResources>>();
        var logger = Mock.Of<ILogger<ShiftAssignmentService>>();
        var hierarchy = new Mock<IHierarchySettingsService>();
        hierarchy.Setup(x => x.GetEffectiveSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new EffectiveSettings(
                RestHours: 11, WeeklyCap: 48, RestHoursSource: "Area", WeeklyCapSource: "Area"));
        var audit = Mock.Of<IAuditLogService>();
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiKeyHmacSecret"]).Returns("test-hmac-secret-for-unit-tests");
        var configCache = Mock.Of<IAppConfigCacheService>();
        _workforce = new ShiftAssignmentService(
            _db, localizer, logger, hierarchy.Object, audit, config.Object, configCache,
            BusyServiceMockFactory.Real(_db, config.Object, restHours: 11, weeklyCap: 48));

        var calLogger = Mock.Of<ILogger<ShiftCalendarService>>();
        var companyCache = new Mock<ICompanyCacheService>();
        var calLocalization = new Mock<ICompanyLocalizationService>();
        calLocalization.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);
        _tech = new ShiftCalendarService(_db, calLogger, companyCache.Object, calLocalization.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private async Task SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();

        _jobAlhut = new JobType { AreaId = area.Id, Name = "Alhut", DisplayName = "Alhut", SortOrder = 1 };
        _jobText = new JobType { AreaId = area.Id, Name = "Text", DisplayName = "Text", SortOrder = 2 };
        _db.JobTypes.AddRange(_jobAlhut, _jobText); await _db.SaveChangesAsync();

        _molecule = new Molecule { AreaId = area.Id, Name = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(_molecule); await _db.SaveChangesAsync();

        _c1 = new Company { MoleculeId = _molecule.Id, Name = "C1", DisplayName = "C1" };
        _c2 = new Company { MoleculeId = _molecule.Id, Name = "C2", DisplayName = "C2" };
        _db.Companies.AddRange(_c1, _c2); await _db.SaveChangesAsync();

        _grouping = new ShiftGrouping { MoleculeId = _molecule.Id, Name = "G", DisplayName = "G" };
        _db.ShiftGroupings.Add(_grouping); await _db.SaveChangesAsync();
        _db.Set<ShiftGroupingCompany>().AddRange(
            new ShiftGroupingCompany { ShiftGroupingId = _grouping.Id, CompanyId = _c1.Id },
            new ShiftGroupingCompany { ShiftGroupingId = _grouping.Id, CompanyId = _c2.Id });
        await _db.SaveChangesAsync();

        _catA = new ShiftCategory { MoleculeId = _molecule.Id, Name = "CatA", DisplayName = "CatA" };
        _catB = new ShiftCategory { MoleculeId = _molecule.Id, Name = "CatB", DisplayName = "CatB" };
        _db.ShiftCategories.AddRange(_catA, _catB); await _db.SaveChangesAsync();

        _stCat = new ShiftType
        {
            Scope = ShiftScope.Molecule, MoleculeId = _molecule.Id, JobTypeId = _jobAlhut.Id,
            ShiftGroupingId = _grouping.Id, CategoryId = _catA.Id, Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0)
        };
        _stNull = new ShiftType
        {
            Scope = ShiftScope.Molecule, MoleculeId = _molecule.Id, JobTypeId = null,
            ShiftGroupingId = _grouping.Id, CategoryId = null, Key = "MIDDLE",
            Start = new TimeOnly(16, 0), End = new TimeOnly(23, 59)
        };
        _db.ShiftTypes.AddRange(_stCat, _stNull); await _db.SaveChangesAsync();

        _a = NewUser(_c1.Id, _jobAlhut.Id, "a", doesShifts: true);
        _b = NewUser(_c1.Id, _jobText.Id, "b", doesShifts: true);
        _c = NewUser(_c1.Id, _jobAlhut.Id, "c", doesShifts: true);
        _d = NewUser(_c1.Id, _jobAlhut.Id, "d", doesShifts: false); // membership DoesShifts=true added below
        _e = NewUser(_c1.Id, _jobAlhut.Id, "e", doesShifts: true);  // no membership row (mirror fallback)
        _f = NewUser(_c1.Id, _jobAlhut.Id, "f", doesShifts: true);  // membership DoesShifts=false overrides mirror
        _g = NewUser(_c1.Id, _jobAlhut.Id, "g", doesShifts: true, accountType: AccountType.GroupUser);
        _db.Users.AddRange(_a, _b, _c, _d, _e, _f, _g); await _db.SaveChangesAsync();

        // Category memberships: A in CatA+CatB (multi-category); B in CatB; C in CatA; G in CatA (GroupUser).
        _db.UserShiftCategories.AddRange(
            new UserShiftCategory { UserId = _a.Id, ShiftCategoryId = _catA.Id },
            new UserShiftCategory { UserId = _a.Id, ShiftCategoryId = _catB.Id },
            new UserShiftCategory { UserId = _b.Id, ShiftCategoryId = _catB.Id },
            new UserShiftCategory { UserId = _c.Id, ShiftCategoryId = _catA.Id },
            new UserShiftCategory { UserId = _g.Id, ShiftCategoryId = _catA.Id });
        // Per-company DoesShifts rows: D true (overrides mirror false), F false (overrides mirror true).
        _db.CompanyMemberships.AddRange(
            new CompanyMembership { UserId = _d.Id, CompanyId = _c1.Id, DoesShifts = true, IsPrimary = true },
            new CompanyMembership { UserId = _f.Id, CompanyId = _c1.Id, DoesShifts = false, IsPrimary = true });
        await _db.SaveChangesAsync();
    }

    private static AppUser NewUser(int companyId, int jobTypeId, string tag, bool doesShifts,
        AccountType accountType = AccountType.Standard, MilitaryRank rank = default)
        => new AppUser
        {
            CompanyId = companyId,
            JobTypeId = jobTypeId,
            Email = tag + "@t.mil",
            DisplayName = tag.ToUpperInvariant(),
            IsActive = true,
            DoesShifts = doesShifts,
            AccountType = accountType,
            Rank = rank,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };

    private async Task SeedTechAsync()
    {
        var project = new Project { Name = "TP", DisplayName = "TP" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "TA", DisplayName = "TA" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var job = new JobType { AreaId = area.Id, Name = "Tech", DisplayName = "Tech", SortOrder = 1 };
        _db.JobTypes.Add(job); await _db.SaveChangesAsync();

        _techMol = new Molecule { AreaId = area.Id, Name = "TM", Type = MoleculeType.Tech };
        _db.Molecules.Add(_techMol); await _db.SaveChangesAsync();
        var t1 = new Company { MoleculeId = _techMol.Id, Name = "T1", DisplayName = "T1" };
        _db.Companies.Add(t1); await _db.SaveChangesAsync();

        var catT = new ShiftCategory { MoleculeId = _techMol.Id, Name = "CatT", DisplayName = "CatT" };
        _db.ShiftCategories.Add(catT); await _db.SaveChangesAsync();

        _stTechCat = new ShiftType
        {
            Scope = ShiftScope.Molecule, MoleculeId = _techMol.Id, TechShiftType = ShiftType.TECH_HANAVA,
            CategoryId = catT.Id, RequiresOfficerRank = true, Key = "TECH_CAT",
            Start = new TimeOnly(8, 0), End = new TimeOnly(20, 0)
        };
        _stTechNull = new ShiftType
        {
            Scope = ShiftScope.Molecule, MoleculeId = _techMol.Id, TechShiftType = ShiftType.TECH_DELTA,
            CategoryId = null, RequiresOfficerRank = false, Key = "TECH_NULL",
            Start = new TimeOnly(20, 0), End = new TimeOnly(8, 0)
        };
        _db.ShiftTypes.AddRange(_stTechCat, _stTechNull); await _db.SaveChangesAsync();

        _officerMember = NewUser(t1.Id, job.Id, "officerMember", doesShifts: true, rank: MilitaryRank.SegenMishne);
        _nonOfficerMember = NewUser(t1.Id, job.Id, "nonOfficerMember", doesShifts: true); // rank default (< 9)
        _officerNonMember = NewUser(t1.Id, job.Id, "officerNonMember", doesShifts: true, rank: MilitaryRank.SegenMishne);
        _techGroupUser = NewUser(t1.Id, job.Id, "techGroupUser", doesShifts: true,
            accountType: AccountType.GroupUser, rank: MilitaryRank.SegenMishne);
        _db.Users.AddRange(_officerMember, _nonOfficerMember, _officerNonMember, _techGroupUser);
        await _db.SaveChangesAsync();

        // catT members: officerMember, nonOfficerMember, techGroupUser. officerNonMember is NOT in catT.
        _db.UserShiftCategories.AddRange(
            new UserShiftCategory { UserId = _officerMember.Id, ShiftCategoryId = catT.Id },
            new UserShiftCategory { UserId = _nonOfficerMember.Id, ShiftCategoryId = catT.Id },
            new UserShiftCategory { UserId = _techGroupUser.Id, ShiftCategoryId = catT.Id });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tech_CategoryFilter_Keeps_OfficerRank_And_Adds_Category_Gate()
    {
        await SeedTechAsync();

        var result = await _tech.GetEligibleUsersForShiftTypeAsync(_techMol.Id, _stTechCat.Id, categoryFilter: true);

        // RequiresOfficerRank + catT membership: officerMember in. nonOfficerMember out (rank),
        // officerNonMember out (not in catT), techGroupUser out (GroupUser).
        result.Select(u => u.Id).Should().BeEquivalentTo(new[] { _officerMember.Id });
    }

    [Fact]
    public async Task Tech_CategoryFilter_NullCategory_Falls_Back_To_All_Participants_Excl_GroupUser()
    {
        await SeedTechAsync();

        var result = await _tech.GetEligibleUsersForShiftTypeAsync(_techMol.Id, _stTechNull.Id, categoryFilter: true);

        // No officer-rank requirement + null category -> all DoesShifts participants, GroupUser excluded.
        result.Select(u => u.Id).Should()
            .BeEquivalentTo(new[] { _officerMember.Id, _nonOfficerMember.Id, _officerNonMember.Id });
    }

    [Fact]
    public async Task Tech_LegacyBranch_Unchanged_When_CategoryFilter_False()
    {
        await SeedTechAsync();

        var result = await _tech.GetEligibleUsersForShiftTypeAsync(_techMol.Id, _stTechCat.Id); // default false

        // Legacy tech: EligibleCompanyIds (none -> all) + officer rank, NO category/DoesShifts/GroupUser gate.
        // Officers only -> officerMember, officerNonMember, techGroupUser. nonOfficerMember excluded by rank.
        result.Select(u => u.Id).Should()
            .BeEquivalentTo(new[] { _officerMember.Id, _officerNonMember.Id, _techGroupUser.Id });
    }

    [Fact]
    public async Task Workforce_CategoryFilter_Returns_Only_Category_Members_Across_Multiple_Categories()
    {
        await SeedAsync();

        var result = await _workforce.GetEligibleUsersForShiftTypeAsync(_stCat.Id, categoryFilter: true);

        // CatA members who are participants and not GroupUser: A (CatA+CatB), C (CatA). B in CatB only,
        // D not in CatA, G is GroupUser -> all excluded. Multi-category A is still eligible for the CatA shift.
        result.Select(r => r.UserId).Should().BeEquivalentTo(new[] { _a.Id, _c.Id });
    }

    [Fact]
    public async Task Workforce_CategoryFilter_NullCategory_Returns_All_Participants()
    {
        await SeedAsync();

        var result = await _workforce.GetEligibleUsersForShiftTypeAsync(_stNull.Id, categoryFilter: true);

        // Null category -> every DoesShifts participant in the grouping (excl. GroupUser):
        // A,B,C (mirror true), D (membership true), E (mirror true, no row). F excluded (membership false), G GroupUser.
        result.Select(r => r.UserId).Should().BeEquivalentTo(new[] { _a.Id, _b.Id, _c.Id, _d.Id, _e.Id });
    }

    [Fact]
    public async Task Workforce_CategoryFilter_Uses_PerCompany_DoesShifts_With_Mirror_Fallback()
    {
        await SeedAsync();

        var ids = (await _workforce.GetEligibleUsersForShiftTypeAsync(_stNull.Id, categoryFilter: true))
            .Select(r => r.UserId).ToList();

        ids.Should().Contain(_d.Id, "membership DoesShifts=true overrides mirror AppUser.DoesShifts=false");
        ids.Should().Contain(_e.Id, "no membership row -> falls back to mirror AppUser.DoesShifts=true");
        ids.Should().NotContain(_f.Id, "membership DoesShifts=false overrides mirror AppUser.DoesShifts=true");
    }

    [Fact]
    public async Task Workforce_LegacyBranch_Unchanged_When_CategoryFilter_False()
    {
        await SeedAsync();

        var result = await _workforce.GetEligibleUsersForShiftTypeAsync(_stCat.Id); // categoryFilter:false default

        // Legacy: jobType=Alhut (from shift type) + active + grouping companies; NO DoesShifts/category/GroupUser
        // gate. All Alhut users -> A,C,D,E,F,G. B is Text -> excluded. Proves legacy != category result.
        result.Select(r => r.UserId).Should().BeEquivalentTo(new[] { _a.Id, _c.Id, _d.Id, _e.Id, _f.Id, _g.Id });
    }
}
