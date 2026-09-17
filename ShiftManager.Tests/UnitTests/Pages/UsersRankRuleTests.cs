using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
/// The rank rule on /Admin/Users: you may act at your own rank or below, never above.
///
/// This became load-bearing when EditCompanyUsers went molecule-wide for Lead/Kabar — that grant also
/// covers reset-password, deactivate and delete, so without the rule a Lead could reset the password
/// of their MoleculeAdmin in another desk and log in as them. The company/grant checks are unchanged;
/// these tests pin that in-scope actions on peers and juniors still work.
/// </summary>
public sealed class UsersRankRuleTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();

    private const int LeadTemplate = 3, MoleculeAdminTemplate = 7;
    private int _companyId;

    public UsersRankRuleTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _concurrency
            .Setup(c => c.SaveWithConcurrencyHandlingAsync(It.IsAny<Func<Task<int>>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<Func<Task<int>>, string, int>(async (save, _, __) => { await save(); return new ConcurrencySaveResult(Success: true); });
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private UsersModel BuildModel(int callerId)
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));

        var model = new UsersModel(
            loc.Object, _db, NullLogger<UsersModel>.Instance,
            Mock.Of<ICompanyContext>(), Mock.Of<IDirectorService>(), Mock.Of<ITraineeService>(),
            Mock.Of<IAuditLogService>(), Mock.Of<IMailService>(), Mock.Of<INotificationService>(),
            _grants.Object, Mock.Of<IRoleService>(), Mock.Of<IJobTypeService>(),
            _concurrency.Object, Mock.Of<ITenantResolver>(), Mock.Of<IHierarchyService>(),
            Mock.Of<IUserCompanyTransferService>(), Mock.Of<IShiftCategoryService>(),
            Mock.Of<ICompanyMembershipService>(), new ChoreCategoryService(_db));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = http };
        model.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return model;
    }

    /// <summary>Seeds a Lead (caller), a MoleculeAdmin (senior) and another Lead (peer) in one company.
    /// The caller holds EditCompanyUsers for that company but not AdminAccess.</summary>
    private async Task<(int lead, int moleculeAdmin, int peer)> SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(mol); await _db.SaveChangesAsync();
        var company = new Company { MoleculeId = mol.Id, Name = "C", DisplayName = "C" };
        _db.Companies.Add(company); await _db.SaveChangesAsync();
        _companyId = company.Id;

        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = MoleculeAdminTemplate, Key = "MoleculeAdmin", SortOrder = 3, DerivedUserRole = UserRole.Manager },
            new RoleTemplate { Id = LeadTemplate, Key = "Lead", SortOrder = 20, DerivedUserRole = UserRole.Manager });
        await _db.SaveChangesAsync();

        AppUser U(string email, int template) => new()
        {
            CompanyId = company.Id, Email = email, DisplayName = email, Role = UserRole.Manager,
            AccountType = AccountType.Standard, RoleTemplateId = template,
            PasswordHash = Array.Empty<byte>(), PasswordSalt = Array.Empty<byte>()
        };
        var lead = U("lead@t", LeadTemplate);
        var mgr = U("mol@t", MoleculeAdminTemplate);
        var peer = U("peer@t", LeadTemplate);
        _db.Users.AddRange(lead, mgr, peer);
        await _db.SaveChangesAsync();

        _grants.Setup(g => g.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(false);
        _grants.Setup(g => g.HasGrantForCompanyAsync(lead.Id, "EditCompanyUsers", company.Id)).ReturnsAsync(true);
        return (lead.Id, mgr.Id, peer.Id);
    }

    private async Task<byte[]> PasswordHashOfAsync(int userId) =>
        (await _db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == userId)).PasswordHash;

    [Fact]
    public async Task Lead_CannotResetThePasswordOfTheirMoleculeAdmin()
    {
        var (lead, moleculeAdmin, _) = await SeedAsync();
        var before = await PasswordHashOfAsync(moleculeAdmin);

        var model = BuildModel(lead);
        await model.OnPostResetPasswordAsync(moleculeAdmin, "NewPass123!");

        (await PasswordHashOfAsync(moleculeAdmin)).Should().BeEquivalentTo(before,
            "a Lead resetting a MoleculeAdmin's password is a full escalation path");
        model.TempData["ErrorMessage"].Should().Be("Error_CannotActOnHigherRankedUser");
    }

    [Fact]
    public async Task Lead_CanStillResetThePasswordOfAPeerAtTheSameRank()
    {
        var (lead, _, peer) = await SeedAsync();
        var before = await PasswordHashOfAsync(peer);

        var model = BuildModel(lead);
        await model.OnPostResetPasswordAsync(peer, "NewPass123!");

        (await PasswordHashOfAsync(peer)).Should().NotBeEquivalentTo(before,
            "acting at your own rank stays allowed — the rule must not block ordinary work");
    }

    [Fact]
    public async Task Lead_CannotDeactivateTheirMoleculeAdmin()
    {
        var (lead, moleculeAdmin, _) = await SeedAsync();

        var model = BuildModel(lead);
        await model.OnPostToggleAsync(moleculeAdmin);

        (await _db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == moleculeAdmin))
            .IsActive.Should().BeTrue();
        model.TempData["ErrorMessage"].Should().Be("Error_CannotActOnHigherRankedUser");
    }

    [Fact]
    public async Task Lead_CanStillDeactivateAPeer()
    {
        var (lead, _, peer) = await SeedAsync();

        var model = BuildModel(lead);
        await model.OnPostToggleAsync(peer);

        (await _db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == peer))
            .IsActive.Should().BeFalse();
    }
}
