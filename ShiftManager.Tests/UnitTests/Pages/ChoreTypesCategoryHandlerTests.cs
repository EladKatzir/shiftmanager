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
using ShiftManager.Pages.Admin.Organization.ChoreTypes;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Covers the ChoreTypes/Index category CRUD handlers: create persists in the accessible molecule;
/// create in an inaccessible molecule is rejected (IDOR); rename + delete round-trip.
/// Uses real SQLite so EF query filters behave as in production.
/// </summary>
public class ChoreTypesCategoryHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICompanyContext> _companyContext = new();

    public ChoreTypesCategoryHandlerTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    private Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));
        return loc;
    }

    private IndexModel BuildModel(int callerId)
    {
        var model = new IndexModel(
            BuildLocalizer().Object, _db, NullLogger<IndexModel>.Instance,
            new ChoreTypeService(_db), new ChoreCategoryService(_db), new ChoreEligibilityAdminService(_db),
            _grants.Object, _companyContext.Object, _audit.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<int> SeedMoleculeAsync(int moleculeId = 1)
    {
        if (!await _db.Projects.AnyAsync())
            _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        if (!await _db.Areas.AnyAsync())
            _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = moleculeId, AreaId = 1, Name = "M" + moleculeId, DisplayName = "M" + moleculeId });
        await _db.SaveChangesAsync();
        return moleculeId;
    }

    [Fact]
    public async Task OnPostCreateCategory_Accessible_Molecule_Persists()
    {
        await SeedMoleculeAsync(1);
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.CategoryName = "Physical";
        model.CategoryDisplayName = "Physical";
        model.CategoryNameEn = "Physical";
        model.CategoryNameHe = "פיזי";

        var result = await model.OnPostCreateCategoryAsync();

        result.Should().BeOfType<RedirectToPageResult>();
        var cat = await _db.ChoreCategories.SingleAsync();
        cat.Name.Should().Be("Physical");
        cat.MoleculeId.Should().Be(1);
        cat.NameEn.Should().Be("Physical");
        cat.NameHe.Should().Be("פיזי");
    }

    [Fact]
    public async Task OnPostCreateCategory_Inaccessible_Molecule_Rejected()
    {
        await SeedMoleculeAsync(1);
        const int caller = 777;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        // No accessible molecules.
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int>());

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.CategoryName = "Physical";
        model.CategoryDisplayName = "Physical";

        var result = await model.OnPostCreateCategoryAsync();

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData["ErrorMessage"].Should().Be("Error_MoleculeNotFound");
        (await _db.ChoreCategories.AnyAsync()).Should().BeFalse("no category should be created for an inaccessible molecule");
    }

    [Fact]
    public async Task OnPostUpdateAndDeleteCategory_RoundTrip()
    {
        await SeedMoleculeAsync(1);
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        var svc = new ChoreCategoryService(_db);
        var cat = await svc.CreateAsync(1, "Physical", "Physical");

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.CategoryEditId = cat!.Id;
        model.CategoryEditDisplayName = "Manual";
        model.CategoryEditNameHe = "ידני";

        (await model.OnPostUpdateCategoryAsync()).Should().BeOfType<RedirectToPageResult>();
        (await svc.GetCategoryAsync(cat.Id))!.DisplayName.Should().Be("Manual");

        (await model.OnPostDeleteCategoryAsync(cat.Id)).Should().BeOfType<RedirectToPageResult>();
        (await _db.ChoreCategories.AnyAsync()).Should().BeFalse();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
