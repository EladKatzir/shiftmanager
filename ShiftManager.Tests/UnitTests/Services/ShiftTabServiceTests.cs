using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for <see cref="ShiftTabService"/> (calendar "tabs" / לשונית): tab CRUD + uniqueness,
/// shift-type assignment (molecule boundary + area-scope rejection), company assignment (cross-molecule
/// rejection C1 + reassignment-is-move M1), the tab→company roster set incl. the Main complement, FK behavior
/// on delete (shift types → Main, company links cascade, remembered pref reverts), per-user last-tab memory,
/// and reorder (SortOrder rewrite + cross-molecule id rejection).
/// </summary>
public sealed class ShiftTabServiceTests
{
    // Molecule 1 owns companies 1,2,3; molecule 2 owns company 4.
    private static async Task SeedHierarchyAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 },
            new Company { Id = 2, Name = "Co2", Slug = "co2", MoleculeId = 1 },
            new Company { Id = 3, Name = "Co3", Slug = "co3", MoleculeId = 1 },
            new Company { Id = 4, Name = "Co4", Slug = "co4", MoleculeId = 2 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Enforces_Name_Uniqueness_Within_Molecule_But_Allows_Reuse_Across_Molecules()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);

        (await svc.CreateAsync(1, "Radio", "Radio")).Should().NotBeNull();
        (await svc.CreateAsync(1, "Radio", "Radio")).Should().BeNull("name already used in molecule 1");
        (await svc.CreateAsync(2, "Radio", "Radio")).Should().NotBeNull("free in a different molecule");
    }

    [Fact]
    public async Task Rename_Rejects_A_Colliding_Name()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);

        await svc.CreateAsync(1, "Radio", "Radio");
        var b = await svc.CreateAsync(1, "Element", "Element");

        (await svc.RenameAsync(b!.Id, "Radio", "Radio", null)).Should().BeFalse("collides within the molecule");
        (await svc.RenameAsync(b.Id, "Element 2", "Element 2", "#ff0000")).Should().BeTrue();
    }

    [Fact]
    public async Task AssignShiftTypeToTab_Honors_Molecule_Boundary_And_Rejects_Area_Scoped()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING" },
            new ShiftType { Id = 200, Scope = ShiftScope.Area, AreaId = 1, Key = "AREA_DUTY" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);

        var tabM1 = await svc.CreateAsync(1, "Radio", "Radio");
        var tabM2 = await svc.CreateAsync(2, "Other", "Other");

        (await svc.AssignShiftTypeToTabAsync(100, tabM2!.Id)).Should().BeFalse("tab in molecule 2 can't own a molecule-1 shift");
        (await svc.AssignShiftTypeToTabAsync(100, tabM1!.Id)).Should().BeTrue();
        (await f.Db.ShiftTypes.FindAsync(100))!.TabId.Should().Be(tabM1.Id);

        (await svc.AssignShiftTypeToTabAsync(200, tabM1.Id)).Should().BeFalse("area-scoped shifts span molecules → never tabbed");

        (await svc.AssignShiftTypeToTabAsync(100, null)).Should().BeTrue("clearing is always allowed");
        (await f.Db.ShiftTypes.FindAsync(100))!.TabId.Should().BeNull();
    }

    [Fact]
    public async Task AssignCompanyToTab_Rejects_CrossMolecule_And_Reassignment_Is_A_Move()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var tab1 = await svc.CreateAsync(1, "Radio", "Radio");
        var tab2 = await svc.CreateAsync(1, "Element", "Element");
        var tabM2 = await svc.CreateAsync(2, "Other", "Other");

        // C1: a molecule-1 company cannot join a molecule-2 tab.
        (await svc.AssignCompanyToTabAsync(1, tabM2!.Id)).Should().BeFalse("cross-molecule stitching is blocked");

        (await svc.AssignCompanyToTabAsync(1, tab1!.Id)).Should().BeTrue();
        // M1: reassigning is a MOVE, not a second row (UNIQUE(CompanyId)).
        (await svc.AssignCompanyToTabAsync(1, tab2!.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        var rows = await f.Db.ShiftTabCompanies.Where(tc => tc.CompanyId == 1).ToListAsync();
        rows.Should().ContainSingle("a company is on at most one tab");
        rows[0].ShiftTabId.Should().Be(tab2.Id, "the company moved to the second tab");

        (await svc.AssignCompanyToTabAsync(1, null)).Should().BeTrue("clearing returns the company to Main");
        (await f.Db.ShiftTabCompanies.CountAsync(tc => tc.CompanyId == 1)).Should().Be(0);
    }

    [Fact]
    public async Task GetCompanyIdsForTab_Returns_Tab_Companies_And_Main_Complement()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var tab1 = await svc.CreateAsync(1, "Radio", "Radio");
        var tab2 = await svc.CreateAsync(1, "Element", "Element");
        await svc.AssignCompanyToTabAsync(1, tab1!.Id);
        await svc.AssignCompanyToTabAsync(2, tab2!.Id);

        (await svc.GetCompanyIdsForTabAsync(1, tab1.Id)).Should().BeEquivalentTo(new[] { 1 });
        (await svc.GetCompanyIdsForTabAsync(1, tab2.Id)).Should().BeEquivalentTo(new[] { 2 });
        // Main = molecule-1 companies with no assignment: company 3 only (1 and 2 are claimed).
        (await svc.GetCompanyIdsForTabAsync(1, null)).Should().BeEquivalentTo(new[] { 3 });
    }

    [Fact]
    public async Task Delete_RevertsShiftTypesToMain_CascadesCompanies_And_ClearsRememberedPref()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var tab = await svc.CreateAsync(1, "Radio", "Radio");
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING", TabId = tab!.Id });
        await f.Db.SaveChangesAsync();
        await svc.AssignCompanyToTabAsync(1, tab.Id);
        await svc.SetLastTabAsync(10, 1, tab.Id);

        (await svc.DeleteAsync(tab.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ShiftTypes.FindAsync(100))!.TabId.Should().BeNull("FK SetNull → shift returns to Main");
        (await f.Db.ShiftTabCompanies.CountAsync(tc => tc.ShiftTabId == tab.Id)).Should().Be(0, "company links cascade");
        (await svc.GetLastTabAsync(10, 1)).Should().BeNull("remembered pref reverts to Main (FK SetNull)");
    }

    [Fact]
    public async Task LastTab_Memory_Saves_Loads_And_Distinguishes_Explicit_Main()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var tab = await svc.CreateAsync(1, "Radio", "Radio");

        (await svc.GetLastTabAsync(10, 1)).Should().BeNull("no preference yet");

        await svc.SetLastTabAsync(10, 1, tab!.Id);
        (await svc.GetLastTabAsync(10, 1)).Should().Be(tab.Id);

        await svc.SetLastTabAsync(10, 1, null);   // explicit Main
        (await svc.GetLastTabAsync(10, 1)).Should().BeNull();

        // Isolated per (user, molecule).
        (await svc.GetLastTabAsync(11, 1)).Should().BeNull();
    }

    [Fact]
    public async Task Reorder_Rewrites_SortOrder_And_Rejects_CrossMolecule_Ids()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var a = await svc.CreateAsync(1, "A", "A");
        var b = await svc.CreateAsync(1, "B", "B");
        var c = await svc.CreateAsync(1, "C", "C");
        var m2 = await svc.CreateAsync(2, "M2Tab", "M2Tab");

        (await svc.ReorderTabsAsync(1, new[] { c!.Id, a!.Id, b!.Id })).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        var ordered = await svc.GetTabsForMoleculeAsync(1);
        ordered.Select(t => t.Id).Should().ContainInOrder(c.Id, a.Id, b.Id);

        (await svc.ReorderTabsAsync(1, new[] { c.Id, m2!.Id })).Should().BeFalse("a molecule-2 tab id is not reorderable in molecule 1");
        (await svc.ReorderTabsAsync(1, new[] { c.Id, 9999 })).Should().BeFalse("unknown id rejected");
    }
}
