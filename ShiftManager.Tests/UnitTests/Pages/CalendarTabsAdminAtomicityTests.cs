using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;
using TabsIndexModel = ShiftManager.Pages.Admin.Organization.Tabs.IndexModel;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Real-SQLite atomicity coverage for the calendar-tabs admin CRUD handlers
/// (<see cref="TabsIndexModel.OnPostCreateAsync"/> / <see cref="TabsIndexModel.OnPostUpdateAsync"/>).
///
/// Each handler applies name/priority/companies/shift types through several service calls that each
/// SaveChanges independently. Without a wrapping transaction, a LATE failure (e.g. a shift type
/// re-scoped out of the (molecule, jobtype) between form load and submit → SetShiftTypesForTabAsync
/// returns false) would persist the earlier rename+company writes yet reject the shift-type change,
/// leaving a visibly half-updated tab. These tests pin the transactional behaviour: any failure rolls
/// the WHOLE mutation back, and a valid change still commits.
///
/// Uses <see cref="SqliteDbContextFixture"/> (real SQLite) because the EF In-Memory provider does NOT
/// support transactions — the very mechanism under test.
/// </summary>
public sealed class CalendarTabsAdminAtomicityTests
{
    // Molecule 1 (jobtype 10) owns companies 1,2,3 + shift type 100. Molecule 2 owns shift type 200,
    // which is therefore OUT OF SCOPE for a molecule-1 tab and makes SetShiftTypesForTabAsync fail.
    private static async Task SeedAsync(AppDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        db.JobTypes.Add(new JobType { Id = 10, AreaId = 1, Name = "Alhut" });
        db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 },
            new Company { Id = 2, Name = "Co2", Slug = "co2", MoleculeId = 1 },
            new Company { Id = 3, Name = "Co3", Slug = "co3", MoleculeId = 1 });
        db.ShiftTypes.AddRange(
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" },
            new ShiftType { Id = 200, Scope = ShiftScope.Molecule, MoleculeId = 2, JobTypeId = 10, Key = "OUTOFSCOPE" });
        await db.SaveChangesAsync();
    }

    // A page model whose ManageCalendarTabs grant covers molecule 1, backed by the real fixture context.
    private static TabsIndexModel MakeModel(AppDbContext db)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim("CompanyId", "0"),
        }, "test"));
        var httpCtx = new DefaultHttpContext { User = principal };

        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var grant = new Mock<IGrantService>();
        grant.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(It.IsAny<int>(), "ManageCalendarTabs"))
             .ReturnsAsync(new List<int> { 1 });

        var companyCtx = new Mock<ICompanyContext>();
        companyCtx.SetupGet(c => c.CompanyId).Returns((int?)null); // no own-molecule augmentation

        return new TabsIndexModel(
            loc.Object, db, NullLogger<TabsIndexModel>.Instance,
            new ShiftTabService(db), Mock.Of<IJobTypeService>(),
            grant.Object, companyCtx.Object, Mock.Of<IAuditLogService>())
        {
            PageContext = new PageContext { HttpContext = httpCtx },
            TempData = new TempDataDictionary(httpCtx, Mock.Of<ITempDataProvider>()),
        };
    }

    [Fact]
    public async Task Update_OutOfScopeShiftType_RollsBackRenameAndCompanies()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var seed = new ShiftTabService(f.Db);
        var tab = await seed.CreateAsync(1, 10, "Geo", "גאו");
        (await seed.SetCompaniesForTabAsync(tab!.Id, new[] { 1 })).Should().BeTrue();
        (await seed.SetShiftTypesForTabAsync(tab.Id, new[] { 100 })).Should().BeTrue();
        f.Db.ChangeTracker.Clear(); // fresh request semantics

        var model = MakeModel(f.Db);
        model.EditTabId = tab.Id;
        model.NameEn = "GeoRenamed";
        model.NameHe = "גאו-שונה";
        model.PrioritizeCompanyUsers = false;                 // flip the default (true) — must also roll back
        model.SelectedCompanyIds = new List<int> { 2 };       // change 1 -> 2 — must roll back
        model.SelectedShiftTypeIds = new List<int> { 200 };   // OUT OF SCOPE -> whole update must fail

        await model.OnPostUpdateAsync();

        model.TempData.ContainsKey("ErrorMessage").Should().BeTrue("the out-of-scope shift type is rejected");
        model.TempData.ContainsKey("SuccessMessage").Should().BeFalse();

        f.Db.ChangeTracker.Clear();
        var reloaded = await f.Db.ShiftTabs.AsNoTracking().FirstAsync(t => t.Id == tab.Id);
        reloaded.NameEn.Should().Be("Geo", "the rename must roll back with the failed shift-type change");
        reloaded.PrioritizeCompanyUsers.Should().BeTrue("the priority flip must roll back too");
        var companies = await f.Db.ShiftTabCompanies.AsNoTracking()
            .Where(tc => tc.ShiftTabId == tab.Id).Select(tc => tc.CompanyId).ToListAsync();
        companies.Should().BeEquivalentTo(new[] { 1 }, "the company change must roll back");
        var shiftTypes = await f.Db.ShiftTabShiftTypes.AsNoTracking()
            .Where(x => x.ShiftTabId == tab.Id).Select(x => x.ShiftTypeId).ToListAsync();
        shiftTypes.Should().BeEquivalentTo(new[] { 100 }, "the shift-type set is unchanged");
    }

    [Fact]
    public async Task Update_ValidChange_Commits()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var seed = new ShiftTabService(f.Db);
        var tab = await seed.CreateAsync(1, 10, "Geo", "גאו");
        await seed.SetCompaniesForTabAsync(tab!.Id, new[] { 1 });
        await seed.SetShiftTypesForTabAsync(tab.Id, new[] { 100 });
        f.Db.ChangeTracker.Clear();

        var model = MakeModel(f.Db);
        model.EditTabId = tab.Id;
        model.NameEn = "GeoOK";
        model.NameHe = "גאו-תקין";
        model.PrioritizeCompanyUsers = true;
        model.SelectedCompanyIds = new List<int> { 2, 3 };
        model.SelectedShiftTypeIds = new List<int> { 100 };

        await model.OnPostUpdateAsync();

        model.TempData.ContainsKey("SuccessMessage").Should().BeTrue("a valid update must commit");
        model.TempData.ContainsKey("ErrorMessage").Should().BeFalse();

        f.Db.ChangeTracker.Clear();
        var reloaded = await f.Db.ShiftTabs.AsNoTracking().FirstAsync(t => t.Id == tab.Id);
        reloaded.NameEn.Should().Be("GeoOK");
        var companies = await f.Db.ShiftTabCompanies.AsNoTracking()
            .Where(tc => tc.ShiftTabId == tab.Id).Select(tc => tc.CompanyId).ToListAsync();
        companies.Should().BeEquivalentTo(new[] { 2, 3 }, "the committed transaction applies the company change");
    }

    [Fact]
    public async Task Create_OutOfScopeShiftType_LeavesNoHalfMadeTab()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        f.Db.ChangeTracker.Clear();

        var model = MakeModel(f.Db);
        model.MoleculeId = 1;
        model.JobTypeId = 10;
        model.NameEn = "NewTab";
        model.NameHe = "חדש";
        model.SelectedCompanyIds = new List<int> { 1 };
        model.SelectedShiftTypeIds = new List<int> { 200 };   // OUT OF SCOPE -> create must fail wholesale

        await model.OnPostCreateAsync();

        model.TempData.ContainsKey("ErrorMessage").Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        var tabs = await new ShiftTabService(f.Db).GetTabsForMoleculeAsync(1, 10, includeInactive: true);
        tabs.Should().BeEmpty("a failed create must leave no half-made tab (transaction rollback)");
        (await f.Db.ShiftTabCompanies.AsNoTracking().AnyAsync()).Should().BeFalse("no orphan company links");
    }
}
