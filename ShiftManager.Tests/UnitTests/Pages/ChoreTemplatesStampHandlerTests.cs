using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Admin.Organization.ChoreTemplates;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Covers the ChoreTemplates/Index stamp + CRUD handler wiring (IDOR + JSON shape). The deep stamp
/// behavior (rotate / skip-on-error / one-per-day / size cap) is covered by Phase 2's ChoreService tests.
/// </summary>
public class ChoreTemplatesStampHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICompanyContext> _companyContext = new();
    private readonly Mock<IChoreService> _choreService = new();

    public ChoreTemplatesStampHandlerTests()
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
            BuildLocalizer().Object, _db, new ChoreTemplateService(_db), _choreService.Object,
            new ChoreTypeService(_db), _grants.Object, _companyContext.Object, _audit.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<(int moleculeId, int templateId, int u1, int u2)> SeedAsync()
    {
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        _db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard, IsActive = true });
        _db.Users.Add(new AppUser { Id = 11, CompanyId = 1, Email = "b@x.mil", DisplayName = "B", AccountType = AccountType.Standard, IsActive = true });
        await _db.SaveChangesAsync();
        var t = await new ChoreTemplateService(_db).CreateAsync(1, "Kitchen", null, "Close kitchen", null, null, null, null, 999);
        return (1, t!.Id, 10, 11);
    }

    [Fact]
    public async Task OnPostStamp_Accessible_Returns_Created_Skipped_Counts()
    {
        var (moleculeId, templateId, u1, u2) = await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _choreService
            .Setup(s => s.StampTemplateAsync(templateId, today, today, It.IsAny<IReadOnlyList<DayOfWeek>>(), It.IsAny<IReadOnlyList<int>>(), false))
            .ReturnsAsync(new StampResult(
                new[] { new StampCreated(today, u1, 100), new StampCreated(today, u2, 101) },
                Array.Empty<StampSkipped>()));

        var model = BuildModel(caller);
        var result = await model.OnPostStampAsync(new IndexModel.StampRequest
        {
            TemplateId = templateId, From = today, To = today, Weekdays = new List<int>(), AssigneeIds = new List<int> { u1, u2 }, Rotate = false
        });

        var json = result.Should().BeOfType<JsonResult>().Subject;
        var val = json.Value!;
        val.GetType().GetProperty("success")!.GetValue(val).Should().Be(true);
        val.GetType().GetProperty("created")!.GetValue(val).Should().Be(2);
        val.GetType().GetProperty("skipped")!.GetValue(val).Should().Be(0);
    }

    [Fact]
    public async Task OnPostStamp_Inaccessible_Molecule_Rejected_403()
    {
        var (moleculeId, templateId, u1, u2) = await SeedAsync();
        const int caller = 777;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int>());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var model = BuildModel(caller);
        var result = await model.OnPostStampAsync(new IndexModel.StampRequest
        {
            TemplateId = templateId, From = today, To = today, AssigneeIds = new List<int> { u1 }, Rotate = false
        });

        var json = result.Should().BeOfType<JsonResult>().Subject;
        json.StatusCode.Should().Be(403);
        _choreService.Verify(s => s.StampTemplateAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<IReadOnlyList<DayOfWeek>>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task OnPostCreateAndDelete_Template_Persists_Via_Service()
    {
        var (moleculeId, templateId, u1, u2) = await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        var model = BuildModel(caller);
        model.MoleculeId = 1;
        model.TemplateName = "Sweep";
        model.TemplateDefaultTitle = "Sweep the floor";
        await model.OnPostCreateAsync();

        var created = await new ChoreTemplateService(_db).GetTemplatesForMoleculeAsync(1, includeInactive: true);
        created.Should().Contain(t => t.Name == "Sweep");

        var sweep = created.First(t => t.Name == "Sweep");
        await model.OnPostDeleteAsync(sweep.Id);
        (await new ChoreTemplateService(_db).GetByIdAsync(sweep.Id)).Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
