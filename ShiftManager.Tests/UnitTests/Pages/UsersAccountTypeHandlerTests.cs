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
/// Covers OnPostAccountTypeAsync: grant-authorized change persists; unauthorized caller is rejected and no change persists.
/// Uses real SQLite (not UseInMemoryDatabase) so EF query filters and string functions behave as in production.
/// </summary>
public class UsersAccountTypeHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();

    public UsersAccountTypeHandlerTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Localizer returns the key as its value so TempData assertions are stable.
        // Concurrency service returns success by default.
        _concurrency
            .Setup(c => c.SaveWithConcurrencyHandlingAsync(It.IsAny<Func<Task<int>>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<Func<Task<int>>, string, int>(async (save, _, __) =>
            {
                await save();
                return new ConcurrencySaveResult(Success: true);
            });
    }

    private Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));
        return loc;
    }

    private UsersModel BuildModel(int callerId, Mock<IStringLocalizer<SharedResources>> loc)
    {
        var model = new UsersModel(
            loc.Object, _db, NullLogger<UsersModel>.Instance,
            Mock.Of<ICompanyContext>(), Mock.Of<IDirectorService>(), Mock.Of<ITraineeService>(),
            _audit.Object, Mock.Of<IMailService>(), Mock.Of<INotificationService>(),
            _grants.Object, Mock.Of<IRoleService>(), Mock.Of<IJobTypeService>(),
            _concurrency.Object, Mock.Of<ITenantResolver>(), Mock.Of<IHierarchyService>(),
            Mock.Of<IUserCompanyTransferService>(), Mock.Of<IShiftCategoryService>(),
            Mock.Of<ICompanyMembershipService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
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
            CompanyId = company.Id,
            Email = "target@test.com",
            DisplayName = "Target",
            Role = UserRole.Employee,
            AccountType = AccountType.Standard,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user); await _db.SaveChangesAsync();
        return (company.Id, user.Id);
    }

    [Fact]
    public async Task OnPostAccountTypeAsync_AuthorizedAdmin_PersistsAccountTypeMil()
    {
        var (companyId, userId) = await SeedAsync();
        const int callerId = 999;

        _grants.Setup(g => g.HasGrantAsync(callerId, "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(callerId, "EditCompanyUsers", companyId)).ReturnsAsync(true);

        var loc = BuildLocalizer();
        var model = BuildModel(callerId, loc);

        var result = await model.OnPostAccountTypeAsync(userId, (int)AccountType.Mil);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData.ContainsKey("ErrorMessage").Should().BeFalse("no error should be set on success");

        var persisted = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        persisted.AccountType.Should().Be(AccountType.Mil);
    }

    [Fact]
    public async Task OnPostAccountTypeAsync_UnauthorizedCaller_IsRejectedAndNoChangePersists()
    {
        var (companyId, userId) = await SeedAsync();
        const int callerId = 777;

        _grants.Setup(g => g.HasGrantAsync(callerId, "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(callerId, "EditCompanyUsers", companyId)).ReturnsAsync(false);

        var loc = BuildLocalizer();
        var model = BuildModel(callerId, loc);

        var result = await model.OnPostAccountTypeAsync(userId, (int)AccountType.Mil);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().Be("Error_NoPermissionForCompany");

        var persisted = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        persisted.AccountType.Should().Be(AccountType.Standard, "no change should persist when caller is unauthorized");
    }

    [Fact]
    public async Task OnPostAccountTypeAsync_InvalidEnumValue_ReturnsErrorAndNoSave()
    {
        var (companyId, userId) = await SeedAsync();
        const int callerId = 999;

        _grants.Setup(g => g.HasGrantAsync(callerId, "AdminAccess")).ReturnsAsync(true);

        var loc = BuildLocalizer();
        var model = BuildModel(callerId, loc);

        // 99 is not a valid AccountType value
        var result = await model.OnPostAccountTypeAsync(userId, 99);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().NotBeNull();

        var persisted = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        persisted.AccountType.Should().Be(AccountType.Standard);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
