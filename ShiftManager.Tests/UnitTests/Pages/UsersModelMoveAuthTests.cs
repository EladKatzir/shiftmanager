using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
/// Covers the authorization gate (AuthorizeMoveAsync) exercised through the OnPostMoveUserAsync handler.
/// The gate is the security boundary for the move feature, so it is tested directly at the handler level.
/// </summary>
public class UsersModelMoveAuthTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IDirectorService> _directors = new();
    private readonly Mock<IUserCompanyTransferService> _transfer = new();
    private readonly Mock<IStringLocalizer<SharedResources>> _loc = new();

    public UsersModelMoveAuthTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Localizer returns the key as its value, so TempData carries the loc key we can assert on.
        _loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
    }

    private UsersModel BuildModel(int adminId)
    {
        var model = new UsersModel(
            _loc.Object, _db, NullLogger<UsersModel>.Instance,
            Mock.Of<ICompanyContext>(), _directors.Object, Mock.Of<ITraineeService>(),
            Mock.Of<IAuditLogService>(), Mock.Of<IMailService>(), Mock.Of<INotificationService>(),
            _grants.Object, Mock.Of<IRoleService>(), Mock.Of<IJobTypeService>(),
            Mock.Of<IConcurrencyService>(), Mock.Of<ITenantResolver>(), Mock.Of<IHierarchyService>(),
            _transfer.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, adminId.ToString()) }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<(int companyA, int companyB, int companyHq)> SeedCompaniesAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" }; _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" }; _db.Areas.Add(area); await _db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce }; _db.Molecules.Add(mol); await _db.SaveChangesAsync();
        var a = new Company { MoleculeId = mol.Id, Name = "A", DisplayName = "A" };
        var b = new Company { MoleculeId = mol.Id, Name = "B", DisplayName = "B" };
        var hq = new Company { MoleculeId = mol.Id, Name = "HQ", DisplayName = "HQ", IsHeadquarters = true };
        _db.Companies.AddRange(a, b, hq); await _db.SaveChangesAsync();
        return (a.Id, b.Id, hq.Id);
    }

    private async Task<int> SeedUserAsync(int companyId, UserRole role)
    {
        var u = new AppUser { CompanyId = companyId, Email = $"u{Guid.NewGuid():N}@x.com", DisplayName = "U",
            Role = role, PasswordHash = Array.Empty<byte>(), PasswordSalt = Array.Empty<byte>() };
        _db.Users.Add(u); await _db.SaveChangesAsync();
        return u.Id;
    }

    [Fact]
    public async Task OnPostMoveUser_SelfMove_BlockedBeforeAnyDbLookup()
    {
        var model = BuildModel(adminId: 100);
        var result = await model.OnPostMoveUserAsync(userId: 100, destCompanyId: 5);
        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().Be("Error_MoveSelf");
    }

    [Fact]
    public async Task OnPostMoveUser_DestinationIsHeadquarters_Blocked()
    {
        var (companyA, _, companyHq) = await SeedCompaniesAsync();
        var targetId = await SeedUserAsync(companyA, UserRole.Employee);

        var model = BuildModel(adminId: 999);
        var result = await model.OnPostMoveUserAsync(targetId, companyHq);
        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().Be("Error_MoveDestHq");
    }

    [Fact]
    public async Task OnPostMoveUser_MoleculeAdminCannotMoveOwner_RoleGateBlocks()
    {
        var (companyA, companyB, _) = await SeedCompaniesAsync();
        var ownerId = await SeedUserAsync(companyA, UserRole.Owner);

        // Admin holds EditCompanyUsers on both companies but is NOT a super-admin and cannot assign Owner.
        _grants.Setup(g => g.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(It.IsAny<int>(), "EditCompanyUsers", It.IsAny<int>())).ReturnsAsync(true);
        _directors.Setup(d => d.CanAssignRoleAsync(UserRole.Owner)).ReturnsAsync(false);

        var model = BuildModel(adminId: 999);
        var result = await model.OnPostMoveUserAsync(ownerId, companyB);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().Be("Error_NoPermissionMoveRole");
        _transfer.Verify(t => t.MoveUserToCompanyAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task OnPostMoveUser_MissingSourcePermission_Blocked()
    {
        var (companyA, companyB, _) = await SeedCompaniesAsync();
        var targetId = await SeedUserAsync(companyA, UserRole.Employee);

        _grants.Setup(g => g.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(It.IsAny<int>(), "EditCompanyUsers", companyA)).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(It.IsAny<int>(), "EditCompanyUsers", companyB)).ReturnsAsync(true);

        var model = BuildModel(adminId: 999);
        var result = await model.OnPostMoveUserAsync(targetId, companyB);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().Be("Error_NoPermissionSourceCompany");
    }

    [Fact]
    public async Task OnPostMoveUser_Authorized_CallsServiceAndReportsSuccess()
    {
        var (companyA, companyB, _) = await SeedCompaniesAsync();
        var targetId = await SeedUserAsync(companyA, UserRole.Employee);

        _grants.Setup(g => g.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(It.IsAny<int>(), "EditCompanyUsers", It.IsAny<int>())).ReturnsAsync(true);
        _directors.Setup(d => d.CanAssignRoleAsync(UserRole.Employee)).ReturnsAsync(true);
        _transfer.Setup(t => t.MoveUserToCompanyAsync(targetId, companyB, 999)).ReturnsAsync(new MoveResult(true));

        var model = BuildModel(adminId: 999);
        var result = await model.OnPostMoveUserAsync(targetId, companyB);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["SuccessMessage"].Should().Be("Users_MoveSuccess");
        _transfer.Verify(t => t.MoveUserToCompanyAsync(targetId, companyB, 999), Times.Once);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
