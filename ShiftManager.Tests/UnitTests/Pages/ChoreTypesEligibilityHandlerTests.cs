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
/// Covers the ChoreTypes/Index promoted-type editor wiring: OnPostUpdateAsync sets category +
/// default weight (h+m → minutes) + replace-semantics eligibility rules; cross-molecule category is rejected.
/// </summary>
public class ChoreTypesEligibilityHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICompanyContext> _companyContext = new();

    public ChoreTypesEligibilityHandlerTests()
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

    private async Task SeedAsync()
    {
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" });
        _db.Molecules.Add(new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task OnPostUpdate_Sets_Category_Weight_And_EligibilityRules()
    {
        await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        var typeSvc = new ChoreTypeService(_db);
        var ct = await typeSvc.CreateAsync(1, "kitchen", "Kitchen", null, caller);
        var catSvc = new ChoreCategoryService(_db);
        var cat = await catSvc.CreateAsync(1, "Physical", "Physical");

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.EditId = ct.Id;
        model.EditDisplayName = "Kitchen";
        model.EditCategoryId = cat!.Id;
        model.EditWeightHours = 2;
        model.EditWeightMinutes = 0;
        model.EditRequiredGender = 2;        // Female
        model.EditRequiresOfficer = true;

        var result = await model.OnPostUpdateAsync();
        result.Should().BeOfType<RedirectToPageResult>();

        var reread = await _db.ChoreTypes.IgnoreQueryFilters().FirstAsync(x => x.Id == ct.Id);
        reread.ChoreCategoryId.Should().Be(cat.Id);
        reread.DefaultWeightMinutes.Should().Be(120);

        var rules = await new ChoreEligibilityAdminService(_db).GetRulesForChoreTypeAsync(ct.Id);
        rules.Should().Contain(r => r.RuleKind == EligibilityRuleKind.RequiresGender && r.GenderValue == Gender.Female);
        rules.Should().Contain(r => r.RuleKind == EligibilityRuleKind.RequiresOfficerRank);
    }

    [Fact]
    public async Task OnPostUpdate_CrossMolecule_Category_Is_Rejected()
    {
        await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1, 2 });

        var typeSvc = new ChoreTypeService(_db);
        var ct = await typeSvc.CreateAsync(1, "kitchen", "Kitchen", null, caller);   // molecule 1
        var catSvc = new ChoreCategoryService(_db);
        var crossCat = await catSvc.CreateAsync(2, "Other", "Other");                 // molecule 2

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.EditId = ct.Id;
        model.EditDisplayName = "Kitchen";
        model.EditCategoryId = crossCat!.Id;   // cross-molecule — service rejects assignment

        await model.OnPostUpdateAsync();

        var reread = await _db.ChoreTypes.IgnoreQueryFilters().FirstAsync(x => x.Id == ct.Id);
        reread.ChoreCategoryId.Should().BeNull("cross-molecule category assignment must not persist");
    }

    [Fact]
    public async Task OnPostUpdate_ClearGender_Removes_Rule()
    {
        await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        var typeSvc = new ChoreTypeService(_db);
        var ct = await typeSvc.CreateAsync(1, "kitchen", "Kitchen", null, caller);
        await new ChoreEligibilityAdminService(_db).SetRulesForChoreTypeAsync(ct.Id, Gender.Male, true, caller);

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.EditId = ct.Id;
        model.EditDisplayName = "Kitchen";
        model.EditRequiredGender = 0;       // clears gender
        model.EditRequiresOfficer = false;  // clears officer

        await model.OnPostUpdateAsync();

        var rules = await new ChoreEligibilityAdminService(_db).GetRulesForChoreTypeAsync(ct.Id);
        rules.Should().BeEmpty();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
