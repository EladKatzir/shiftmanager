using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Scheduling.Eligibility;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Eligibility;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Task 5 (design doc #3): the Eligibility editor pinned all data to the caller's login
/// home-molecule claim with no selector, so a project-scoped Owner (or area-scoped AreaAdmin)
/// could never reach users/categories outside their own home molecule. It also had a latent
/// cross-molecule IDOR: OnPostToggleAsync/OnGetCategoryAsync/OnGetPersonAsync performed no
/// caller-scope check against the target's molecule at all.
///
/// This suite uses a REAL GrantService (not a mock) over a real SQLite fixture so the actual
/// scope-cascade logic (project scope -> every molecule in the project; molecule scope -> just
/// that one) is exercised end-to-end. Mocking GetAccessibleMoleculeIdsForGrantAsync/
/// HasGrantWithScopeAsync would only prove the page trusts its dependencies, not that the
/// authorization is actually enforced — the wrong thing to prove for a security fix.
/// </summary>
public class EligibilityScopeTests : IDisposable
{
    private const string GrantKey = "ManageShiftCategories";

    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IShiftCategoryService _shiftCats;
    private readonly IChoreCategoryService _choreCats;
    private readonly IEligibilityQueryService _elig;

    public EligibilityScopeTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _grantService = new GrantService(_db, new Mock<IHierarchyService>().Object, new Mock<IAuditLogService>().Object);
        _shiftCats = new ShiftCategoryService(_db);
        _choreCats = new ChoreCategoryService(_db);
        _elig = new EligibilityQueryService(_db);
    }

    private static Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        return loc;
    }

    private IndexModel BuildModel(int callerId, int? callerMoleculeId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(callerId);
        currentUser.SetupGet(c => c.MoleculeId).Returns(callerMoleculeId);

        var model = new IndexModel(
            _elig, _shiftCats, _choreCats, currentUser.Object, _grantService, _db, BuildLocalizer().Object);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<GrantType> SeedGrantTypeAsync()
    {
        var gt = new GrantType
        {
            Key = GrantKey,
            NameKey = "Grant_ManageShiftCategories",
            DescriptionKey = "Grant_ManageShiftCategories_Desc",
            Category = GrantCategory.Shift,
            DefaultScope = GrantScopeLevel.Molecule,
            IsSystem = true
        };
        _db.GrantTypes.Add(gt);
        await _db.SaveChangesAsync();
        return gt;
    }

    // ---- (a) Owner (project-scoped grant) sees every molecule in the project ----

    [Fact]
    public async Task Owner_Get_ListsAllProjectMolecules()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var m1 = new Molecule { AreaId = area.Id, Name = "M1", DisplayName = "M1", Type = MoleculeType.Workforce };
        var m2 = new Molecule { AreaId = area.Id, Name = "M2", DisplayName = "M2", Type = MoleculeType.Workforce };
        _db.Molecules.AddRange(m1, m2); await _db.SaveChangesAsync();

        var gt = await SeedGrantTypeAsync();
        const int ownerId = 501;
        _db.Grants.Add(new Grant { UserId = ownerId, GrantTypeId = gt.Id, ProjectId = project.Id, CanOwn = true });
        await _db.SaveChangesAsync();

        var model = BuildModel(ownerId, m1.Id);
        await model.OnGetAsync();

        model.AvailableMolecules.Count.Should().BeGreaterThanOrEqualTo(2);
        model.AvailableMolecules.Select(m => m.Id).Should().Contain(new[] { m1.Id, m2.Id });
    }

    // ---- (b) IDOR: a caller scoped to molecule A only must not touch molecule B ----

    private async Task<(Molecule MoleculeA, Molecule MoleculeB, AppUser UserB, ShiftCategory CategoryB, GrantType GrantType)> SeedTwoMoleculeSetupAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var moleculeA = new Molecule { AreaId = area.Id, Name = "MA", DisplayName = "MA", Type = MoleculeType.Workforce };
        var moleculeB = new Molecule { AreaId = area.Id, Name = "MB", DisplayName = "MB", Type = MoleculeType.Workforce };
        _db.Molecules.AddRange(moleculeA, moleculeB); await _db.SaveChangesAsync();

        var companyB = new Company { Name = "CB", DisplayName = "CB", MoleculeId = moleculeB.Id };
        _db.Companies.Add(companyB); await _db.SaveChangesAsync();

        var userB = new AppUser { CompanyId = companyB.Id, Email = "b@test.local", DisplayName = "User B", DoesShifts = true, IsActive = true };
        _db.Users.Add(userB); await _db.SaveChangesAsync();

        var categoryB = new ShiftCategory { MoleculeId = moleculeB.Id, Name = "CatB", DisplayName = "CatB", IsActive = true };
        _db.ShiftCategories.Add(categoryB); await _db.SaveChangesAsync();

        var gt = await SeedGrantTypeAsync();
        return (moleculeA, moleculeB, userB, categoryB, gt);
    }

    [Fact]
    public async Task Toggle_ForUserOutsideCallerScope_IsRejected()
    {
        var (moleculeA, _, userB, categoryB, gt) = await SeedTwoMoleculeSetupAsync();

        const int callerId = 601;
        _db.Grants.Add(new Grant { UserId = callerId, GrantTypeId = gt.Id, MoleculeId = moleculeA.Id, CanOwn = true });
        await _db.SaveChangesAsync();

        var model = BuildModel(callerId, moleculeA.Id);
        var result = await model.OnPostToggleAsync(new IndexModel.ToggleRequest
        {
            UserId = userB.Id,
            CategoryId = categoryB.Id,
            IsChore = false,
            Add = true
        });

        result.Should().BeOfType<JsonResult>();
        ((JsonResult)result).StatusCode.Should().Be(403);
        (await _db.UserShiftCategories.CountAsync()).Should()
            .Be(0, "the caller has no grant for molecule B; no membership should have been written");
    }

    // ---- Same IDOR class, other two handlers modified by this fix (beyond the 2 required tests) ----

    [Fact]
    public async Task GetCategory_ForCategoryOutsideCallerScope_IsRejected()
    {
        var (moleculeA, _, _, categoryB, gt) = await SeedTwoMoleculeSetupAsync();

        const int callerId = 602;
        _db.Grants.Add(new Grant { UserId = callerId, GrantTypeId = gt.Id, MoleculeId = moleculeA.Id, CanOwn = true });
        await _db.SaveChangesAsync();

        var model = BuildModel(callerId, moleculeA.Id);
        var result = await model.OnGetCategoryAsync(categoryB.Id, chore: false);

        result.Should().BeOfType<JsonResult>();
        ((JsonResult)result).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetPerson_ForUserOutsideCallerScope_IsRejected()
    {
        var (moleculeA, _, userB, _, gt) = await SeedTwoMoleculeSetupAsync();

        const int callerId = 603;
        _db.Grants.Add(new Grant { UserId = callerId, GrantTypeId = gt.Id, MoleculeId = moleculeA.Id, CanOwn = true });
        await _db.SaveChangesAsync();

        var model = BuildModel(callerId, moleculeA.Id);
        var result = await model.OnGetPersonAsync(userB.Id);

        result.Should().BeOfType<JsonResult>();
        ((JsonResult)result).StatusCode.Should().Be(403);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
