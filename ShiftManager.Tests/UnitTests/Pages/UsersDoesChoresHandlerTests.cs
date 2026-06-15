using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Admin;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Covers OnPostDoesChoresAsync (toggle + future-chore soft-cancel) and OnPostUserChoreCategoriesAsync
/// (membership replace, molecule-constrained), both riding AuthorizeUserEditAsync. Real SQLite.
/// </summary>
public class UsersDoesChoresHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();

    public UsersDoesChoresHandlerTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _concurrency
            .Setup(c => c.SaveWithConcurrencyHandlingAsync(It.IsAny<Func<Task<int>>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<Func<Task<int>>, string, int>(async (save, _, __) => { await save(); return new ConcurrencySaveResult(Success: true); });
    }

    private Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));
        return loc;
    }

    private UsersModel BuildModel(int callerId)
    {
        var model = new UsersModel(
            BuildLocalizer().Object, _db, NullLogger<UsersModel>.Instance,
            Mock.Of<ICompanyContext>(), Mock.Of<IDirectorService>(), Mock.Of<ITraineeService>(),
            _audit.Object, Mock.Of<IMailService>(), Mock.Of<INotificationService>(),
            _grants.Object, Mock.Of<IRoleService>(), Mock.Of<IJobTypeService>(),
            _concurrency.Object, Mock.Of<ITenantResolver>(), Mock.Of<IHierarchyService>(),
            Mock.Of<IUserCompanyTransferService>(), Mock.Of<IShiftCategoryService>(),
            Mock.Of<ICompanyMembershipService>(), new ChoreCategoryService(_db));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<(int companyId, int moleculeId, int userId)> SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(mol); await _db.SaveChangesAsync();
        var company = new Company { MoleculeId = mol.Id, Name = "C", DisplayName = "C" };
        _db.Companies.Add(company); await _db.SaveChangesAsync();
        var user = new AppUser
        {
            CompanyId = company.Id, Email = "t@test.com", DisplayName = "Target", Role = UserRole.Employee,
            AccountType = AccountType.Standard, PasswordHash = Array.Empty<byte>(), PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user); await _db.SaveChangesAsync();
        return (company.Id, mol.Id, user.Id);
    }

    [Fact]
    public async Task OnPostDoesChores_True_Sets_Flag()
    {
        var (companyId, _, userId) = await SeedAsync();
        const int caller = 999;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(true);

        var model = BuildModel(caller);
        await model.OnPostDoesChoresAsync(userId, doesChores: true);

        (await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).DoesChores.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostDoesChores_False_WithDelete_SoftCancels_Future_Chores()
    {
        var (companyId, _, userId) = await SeedAsync();
        const int caller = 999;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(true);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.Chores.Add(new Chore { UserId = userId, CompanyId = (await _db.Users.FirstAsync(u => u.Id == userId)).CompanyId, Date = today.AddDays(3), Title = "future", WeightMinutes = 480 });
        _db.Chores.Add(new Chore { UserId = userId, CompanyId = (await _db.Users.FirstAsync(u => u.Id == userId)).CompanyId, Date = today.AddDays(-3), Title = "past", WeightMinutes = 480 });
        await _db.SaveChangesAsync();

        var model = BuildModel(caller);
        await model.OnPostDoesChoresAsync(userId, doesChores: false, deleteFutureChores: true);

        var future = await _db.Chores.IgnoreQueryFilters().FirstAsync(c => c.Title == "future");
        var past = await _db.Chores.IgnoreQueryFilters().FirstAsync(c => c.Title == "past");
        future.CanceledAt.Should().NotBeNull("future chore is soft-canceled");
        future.CanceledBy.Should().Be(caller);
        past.CanceledAt.Should().BeNull("past chores are never touched");
    }

    [Fact]
    public async Task OnPostDoesChores_Unauthorized_Is_Rejected_NoChange()
    {
        var (companyId, _, userId) = await SeedAsync();
        const int caller = 777;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(caller, "EditCompanyUsers", companyId)).ReturnsAsync(false);

        var model = BuildModel(caller);
        await model.OnPostDoesChoresAsync(userId, doesChores: true);

        model.TempData["ErrorMessage"].Should().Be("Error_NoPermissionForCompany");
        (await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).DoesChores.Should().BeFalse();
    }

    [Fact]
    public async Task OnPostUserChoreCategories_Replaces_Membership_Constrained_To_Molecule()
    {
        var (companyId, moleculeId, userId) = await SeedAsync();
        const int caller = 999;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(true);

        var catSvc = new ChoreCategoryService(_db);
        var inMol = await catSvc.CreateAsync(moleculeId, "Physical", "Physical");
        // A category in a different molecule must be ignored.
        var otherMol = new Molecule { AreaId = (await _db.Molecules.FirstAsync()).AreaId, Name = "Other", DisplayName = "Other" };
        _db.Molecules.Add(otherMol); await _db.SaveChangesAsync();
        var crossCat = await catSvc.CreateAsync(otherMol.Id, "Cross", "Cross");

        var model = BuildModel(caller);
        var result = await model.OnPostUserChoreCategoriesAsync(new UsersModel.UserChoreCategoriesRequest
        {
            UserId = userId, CategoryIds = new List<int> { inMol!.Id, crossCat!.Id }
        });

        result.Should().BeOfType<JsonResult>();
        var ids = await catSvc.GetUserCategoryIdsAsync(userId);
        ids.Should().BeEquivalentTo(new[] { inMol.Id }, "cross-molecule category must be filtered out");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
