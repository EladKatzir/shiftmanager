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
/// Covers OnPostGenderAsync: an authorized user-editor persists Gender + writes an audit row; an
/// unauthorized caller is rejected and no change persists; an out-of-range value is rejected.
/// Rides AuthorizeUserEditAsync — NO dedicated grant. Real SQLite.
/// </summary>
public class UsersGenderHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();

    public UsersGenderHandlerTests()
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

    private async Task<(int companyId, int userId)> SeedAsync()
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
        return (company.Id, user.Id);
    }

    [Fact]
    public async Task OnPostGender_Authorized_Persists_And_Audits()
    {
        var (companyId, userId) = await SeedAsync();
        const int caller = 999;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(caller, "EditCompanyUsers", companyId)).ReturnsAsync(true);

        string? auditAction = null;
        _audit.Setup(a => a.LogUserActionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<int, string, string, int?, string, string?>((_, action, _, _, _, _) => auditAction = action)
            .Returns(Task.CompletedTask);

        var model = BuildModel(caller);
        await model.OnPostGenderAsync(userId, (int)Gender.Female);

        (await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).Gender.Should().Be(Gender.Female);
        auditAction.Should().Be("GenderChanged");
    }

    [Fact]
    public async Task OnPostGender_Unauthorized_Is_Rejected_NoChange()
    {
        var (companyId, userId) = await SeedAsync();
        const int caller = 777;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(caller, "EditCompanyUsers", companyId)).ReturnsAsync(false);

        var model = BuildModel(caller);
        await model.OnPostGenderAsync(userId, (int)Gender.Male);

        model.TempData["ErrorMessage"].Should().Be("Error_NoPermissionForCompany");
        (await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).Gender.Should().Be(Gender.Unspecified);
    }

    [Fact]
    public async Task OnPostGender_OutOfRange_Is_Rejected()
    {
        var (companyId, userId) = await SeedAsync();
        const int caller = 999;
        _grants.Setup(g => g.HasGrantAsync(caller, "AdminAccess")).ReturnsAsync(true);

        var model = BuildModel(caller);
        await model.OnPostGenderAsync(userId, 99);

        model.TempData["ErrorMessage"].Should().Be("Error_InvalidValue");
        (await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId)).Gender.Should().Be(Gender.Unspecified);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
