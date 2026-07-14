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

        // Real HierarchyService (not mocked): HasGrantWithScopeAsync's project-scope branch resolves
        // the CALLER's own hierarchy (Path.Project.Id) to validate a project-scoped grant against an
        // explicit moleculeId — an unconfigured/empty hierarchy mock would make that branch permanently
        // unable to authorize a project-scoped caller (discovered via a real test failure, not assumed).
        // A real authenticated caller always has a resolvable hierarchy in production; tests that need
        // that branch to succeed seed a real AppUser+Company for the caller (see below).
        _grantService = new GrantService(_db, new HierarchyService(_db), new Mock<IAuditLogService>().Object);
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

    // ---- ALLOW-path proof: the fix must not over-block legitimate same-scope callers, and must
    // actually deliver the feature half (an Owner editing a molecule other than their own home one) ----

    private async Task<(Molecule Molecule, AppUser User, ShiftCategory Category, GrantType GrantType)> SeedSingleMoleculeSetupAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var molecule = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule); await _db.SaveChangesAsync();

        var company = new Company { Name = "C", DisplayName = "C", MoleculeId = molecule.Id };
        _db.Companies.Add(company); await _db.SaveChangesAsync();

        var user = new AppUser { CompanyId = company.Id, Email = "u@test.local", DisplayName = "User", DoesShifts = true, IsActive = true };
        _db.Users.Add(user); await _db.SaveChangesAsync();

        var category = new ShiftCategory { MoleculeId = molecule.Id, Name = "Cat", DisplayName = "Cat", IsActive = true };
        _db.ShiftCategories.Add(category); await _db.SaveChangesAsync();

        var gt = await SeedGrantTypeAsync();
        return (molecule, user, category, gt);
    }

    [Fact]
    public async Task Toggle_SameMoleculeCaller_Succeeds()
    {
        // Guards against an over-strict regression: a caller scoped to the SAME molecule as both
        // the target user and the target category (the common Manager/Assigner case, unchanged by
        // this fix) must still be able to toggle membership.
        var (molecule, user, category, gt) = await SeedSingleMoleculeSetupAsync();

        const int callerId = 604;
        _db.Grants.Add(new Grant { UserId = callerId, GrantTypeId = gt.Id, MoleculeId = molecule.Id, CanOwn = true });
        await _db.SaveChangesAsync();

        var model = BuildModel(callerId, molecule.Id);
        var result = await model.OnPostToggleAsync(new IndexModel.ToggleRequest
        {
            UserId = user.Id,
            CategoryId = category.Id,
            IsChore = false,
            Add = true
        });

        result.Should().BeOfType<JsonResult>();
        ((JsonResult)result).StatusCode.Should().NotBe(403);
        (await _db.UserShiftCategories.CountAsync(m => m.UserId == user.Id && m.ShiftCategoryId == category.Id))
            .Should().Be(1, "a caller scoped to the same molecule as both targets must still be able to toggle membership");
    }

    [Fact]
    public async Task Toggle_ForOwnerAcrossMolecules_Succeeds()
    {
        // This is the FEATURE half of the fix (design doc: "Owner cannot edit eligibility for all
        // users"): a project-scoped Owner whose login home molecule differs from the target must be
        // able to toggle membership in that other, non-home molecule.
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var home = new Molecule { AreaId = area.Id, Name = "Home", DisplayName = "Home", Type = MoleculeType.Workforce };
        var target = new Molecule { AreaId = area.Id, Name = "Target", DisplayName = "Target", Type = MoleculeType.Workforce };
        _db.Molecules.AddRange(home, target); await _db.SaveChangesAsync();

        var company = new Company { Name = "C", DisplayName = "C", MoleculeId = target.Id };
        _db.Companies.Add(company); await _db.SaveChangesAsync();
        var user = new AppUser { CompanyId = company.Id, Email = "target@test.local", DisplayName = "Target User", DoesShifts = true, IsActive = true };
        _db.Users.Add(user); await _db.SaveChangesAsync();
        var category = new ShiftCategory { MoleculeId = target.Id, Name = "TargetCat", DisplayName = "TargetCat", IsActive = true };
        _db.ShiftCategories.Add(category); await _db.SaveChangesAsync();

        var gt = await SeedGrantTypeAsync();
        const int ownerId = 605;
        _db.Grants.Add(new Grant { UserId = ownerId, GrantTypeId = gt.Id, ProjectId = project.Id, CanOwn = true });

        // The Owner needs a resolvable hierarchy (real AppUser -> Company -> Molecule -> Area ->
        // Project chain) for HasGrantWithScopeAsync's project-scope branch to validate their grant
        // against an explicit moleculeId — a real authenticated caller always has one in production.
        // Placed in "home" (a different molecule than the mutation target) to prove this is a genuine
        // cross-molecule, same-project success, not a same-molecule coincidence.
        var ownerCompany = new Company { Name = "OwnerCo", DisplayName = "OwnerCo", MoleculeId = home.Id };
        _db.Companies.Add(ownerCompany); await _db.SaveChangesAsync();
        _db.Users.Add(new AppUser { Id = ownerId, CompanyId = ownerCompany.Id, Email = "owner@test.local", DisplayName = "Owner", IsActive = true });
        await _db.SaveChangesAsync();

        var model = BuildModel(ownerId, home.Id);
        var result = await model.OnPostToggleAsync(new IndexModel.ToggleRequest
        {
            UserId = user.Id,
            CategoryId = category.Id,
            IsChore = false,
            Add = true
        });

        result.Should().BeOfType<JsonResult>();
        ((JsonResult)result).StatusCode.Should().NotBe(403);
        (await _db.UserShiftCategories.CountAsync(m => m.UserId == user.Id && m.ShiftCategoryId == category.Id))
            .Should().Be(1, "a project-scoped Owner must be able to edit eligibility outside their own home molecule");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
