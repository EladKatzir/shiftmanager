using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the reshaped <see cref="ShiftTabService"/> (calendar "tabs" / לשונית):
/// (molecule, jobtype)-scoped CRUD + dual NameEn/NameHe uniqueness, many-to-many company + shift-type
/// membership (replace-set + cross-molecule/scope rejection), usage counts, per-(user,molecule,jobtype)
/// last-tab memory, and delete cascade (company + shift-type join rows drop, remembered pref → SetNull).
/// </summary>
public sealed class ShiftTabServiceTests
{
    // Molecule 1 (jobtypes 10,11) owns companies 1,2,3; molecule 2 owns company 4.
    private static async Task SeedHierarchyAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.JobTypes.AddRange(
            new JobType { Id = 10, AreaId = 1, Name = "Alhut" },
            new JobType { Id = 11, AreaId = 1, Name = "Text" });
        f.Db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 },
            new Company { Id = 2, Name = "Co2", Slug = "co2", MoleculeId = 1 },
            new Company { Id = 3, Name = "Co3", Slug = "co3", MoleculeId = 1 },
            new Company { Id = 4, Name = "Co4", Slug = "co4", MoleculeId = 2 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Is_Unique_Per_MoleculeJobtype_Over_Both_Names_But_Reusable_Across_Scope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);

        (await svc.CreateAsync(1, 10, "Geo", "גאו")).Should().NotBeNull();
        (await svc.CreateAsync(1, 10, "Geo", "אחר")).Should().BeNull("NameEn already used in (mol1, Alhut)");
        (await svc.CreateAsync(1, 10, "Other", "גאו")).Should().BeNull("NameHe already used in (mol1, Alhut)");
        (await svc.CreateAsync(1, 11, "Geo", "גאו")).Should().NotBeNull("free in a different jobtype");
        (await svc.CreateAsync(2, 10, "Geo", "גאו")).Should().NotBeNull("free in a different molecule");
        (await svc.CreateAsync(1, null, "Tech", "טכני")).Should().NotBeNull("null jobtype (Tech) is allowed");
    }

    [Fact]
    public async Task GetTabsForMolecule_Is_Scoped_To_Jobtype_And_Ordered()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        await svc.CreateAsync(1, 10, "Bravo", "ב");
        await svc.CreateAsync(1, 10, "Alpha", "א");
        await svc.CreateAsync(1, 11, "TextTab", "טקסט");

        var alhut = await svc.GetTabsForMoleculeAsync(1, 10);
        alhut.Select(t => t.NameEn).Should().ContainInOrder("Bravo", "Alpha"); // SortOrder (insertion) then NameEn
        alhut.Should().OnlyContain(t => t.JobTypeId == 10);
        (await svc.GetTabsForMoleculeAsync(1, 11)).Should().ContainSingle(t => t.NameEn == "TextTab");
        (await svc.GetTabsForMoleculeAsync(1, null)).Should().BeEmpty("no null-jobtype tabs in mol1");
    }

    [Fact]
    public async Task Rename_Updates_Names_Color_Priority_And_Rejects_Collision()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        await svc.CreateAsync(1, 10, "Geo", "גאו");
        var b = await svc.CreateAsync(1, 10, "Tacti", "טקטי");

        (await svc.RenameAsync(b!.Id, "Geo", "טקטי", null, true)).Should().BeFalse("NameEn collides in scope");
        (await svc.RenameAsync(b.Id, "Tacti", "גאו", null, true)).Should().BeFalse("NameHe collides in scope");
        (await svc.RenameAsync(b.Id, "Tacti2", "טקטי2", "#ff0000", false)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        var reloaded = await svc.GetTabAsync(b.Id);
        reloaded!.NameEn.Should().Be("Tacti2");
        reloaded.NameHe.Should().Be("טקטי2");
        reloaded.Color.Should().Be("#ff0000");
        reloaded.PrioritizeCompanyUsers.Should().BeFalse();
        reloaded.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task NewTab_Defaults_PrioritizeCompanyUsers_On_And_Stamps_Audit()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var tab = await svc.CreateAsync(1, 10, "Geo", "גאו", createdByUserId: 42);
        tab!.PrioritizeCompanyUsers.Should().BeTrue("UD3 default ON");
        tab.CreatedByUserId.Should().Be(42);
    }

    [Fact]
    public async Task SetCompanies_Replaces_Membership_Allows_ManyTabs_And_Rejects_CrossMolecule()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        var tacti = await svc.CreateAsync(1, 10, "Tacti", "טקטי");

        (await svc.SetCompaniesForTabAsync(geo!.Id, new[] { 1, 2 })).Should().BeTrue();
        (await svc.SetCompaniesForTabAsync(tacti!.Id, new[] { 1 })).Should().BeTrue("a company may be on many tabs now");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 1, 2 });
        (await svc.GetCompanyIdsForTabAsync(tacti.Id)).Should().BeEquivalentTo(new[] { 1 });

        (await svc.SetCompaniesForTabAsync(geo.Id, new[] { 2, 3 })).Should().BeTrue("replace-set");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 2, 3 });

        (await svc.SetCompaniesForTabAsync(geo.Id, new[] { 4 })).Should().BeFalse("company 4 is in molecule 2 (IDOR)");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 2, 3 }, "rejected — membership unchanged");

        (await svc.SetCompaniesForTabAsync(geo.Id, System.Array.Empty<int>())).Should().BeTrue("empty = no restriction");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetShiftTypes_Replaces_Membership_And_Rejects_OutOfScope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" },
            new ShiftType { Id = 101, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = null, Key = "OFFLINE" },
            new ShiftType { Id = 200, Scope = ShiftScope.Molecule, MoleculeId = 2, JobTypeId = 10, Key = "MORNING" },
            new ShiftType { Id = 300, Scope = ShiftScope.Area, AreaId = 1, Key = "AREA_DUTY" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");

        (await svc.SetShiftTypesForTabAsync(geo!.Id, new[] { 100, 101 })).Should().BeTrue("in molecule; jobtype match or null");
        (await svc.GetShiftTypeIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 100, 101 });

        (await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 200 })).Should().BeFalse("shift type in molecule 2");
        (await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 300 })).Should().BeFalse("area-scoped shift is never molecule-tabbed");
        (await svc.GetShiftTypeIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 100, 101 }, "rejected — unchanged");
    }

    [Fact]
    public async Task GetUsage_Counts_ShiftType_And_Company_Join_Rows()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        await svc.SetCompaniesForTabAsync(geo!.Id, new[] { 1, 2 });
        await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 100 });

        var (shiftTypeCount, companyCount) = await svc.GetUsageAsync(geo.Id);
        shiftTypeCount.Should().Be(1);
        companyCount.Should().Be(2);
    }

    [Fact]
    public async Task Delete_Cascades_Company_And_ShiftType_Joins_And_Clears_RememberedPref()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        await svc.SetCompaniesForTabAsync(geo!.Id, new[] { 1 });
        await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 100 });
        await svc.SetLastTabAsync(userId: 10, moleculeId: 1, jobTypeId: 10, tabId: geo.Id);

        (await svc.DeleteAsync(geo.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ShiftTabCompanies.CountAsync(tc => tc.ShiftTabId == geo.Id)).Should().Be(0, "company links cascade");
        (await f.Db.ShiftTabShiftTypes.CountAsync(x => x.ShiftTabId == geo.Id)).Should().Be(0, "shift-type links cascade");
        (await f.Db.ShiftTypes.FindAsync(100)).Should().NotBeNull("the shift type itself is untouched");
        (await svc.GetLastTabAsync(10, 1, 10)).Should().BeNull("remembered pref reverts (FK SetNull)");
    }

    [Fact]
    public async Task LastTab_Memory_Is_Isolated_Per_UserMoleculeJobtype_Including_NullJobtype()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        var tech = await svc.CreateAsync(1, null, "Tech", "טכני");

        (await svc.GetLastTabAsync(10, 1, 10)).Should().BeNull("no preference yet");

        await svc.SetLastTabAsync(10, 1, 10, geo!.Id);
        (await svc.GetLastTabAsync(10, 1, 10)).Should().Be(geo.Id);
        (await svc.GetLastTabAsync(10, 1, 11)).Should().BeNull("different jobtype");
        (await svc.GetLastTabAsync(10, 1, null)).Should().BeNull("different (null) jobtype");

        await svc.SetLastTabAsync(10, 1, null, tech!.Id); // null-jobtype (Tech) scope
        (await svc.GetLastTabAsync(10, 1, null)).Should().Be(tech.Id);

        await svc.SetLastTabAsync(10, 1, 10, null); // explicit clear
        (await svc.GetLastTabAsync(10, 1, 10)).Should().BeNull();
        (await svc.GetLastTabAsync(11, 1, 10)).Should().BeNull("different user");
    }

    [Fact]
    public async Task GetShiftTypeTabMap_EmptySelectionTab_MapsAllMoleculeJobtypeShiftTypes()
    {
        // molecule 1, jobtype 10: shift types A(100),B(101) jobtype 10 + shared S(102, jobtype null) + home H(103).
        // Tab "Geo" selects only A; tab "Wide" selects nothing (empty = all).
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" },       // A
            new ShiftType { Id = 101, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "NIGHT" },         // B
            new ShiftType { Id = 102, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = null, Key = "CUSTOM_SHARED" },// S (shared, null jobtype)
            new ShiftType { Id = 103, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "HOME" });         // H
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);

        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        var wide = await svc.CreateAsync(1, 10, "Wide", "רחב");
        (await svc.SetShiftTypesForTabAsync(geo!.Id, new[] { 100 })).Should().BeTrue();
        // wide: leave empty (= all)

        var map = await svc.GetShiftTypeTabMapAsync(1, 10);

        map[100].Should().BeEquivalentTo(new[] { geo.Id, wide!.Id });  // A on Geo (explicit) + Wide (empty=all)
        map[101].Should().Contain(wide.Id);                            // B only on Wide (empty=all), NOT Geo
        map[101].Should().NotContain(geo.Id);
        map[102].Should().BeEquivalentTo(new[] { geo.Id, wide.Id });   // shared null-jobtype maps to EVERY tab (PF7)
        map[103].Should().BeEquivalentTo(new[] { geo.Id, wide.Id });   // HOME maps to EVERY tab (PF7)
    }

    [Fact]
    public void Coverage_Lists_ShiftTypes_In_No_Tab_And_Ignores_EmptyTabs()
    {
        // allShiftTypeIds = {1,2,3}; tabA covers {1}; nothing else → {2,3} uncovered.
        var allIds = new[] { 1, 2, 3 };
        var tabSets = new List<HashSet<int>> { new() { 1 } };            // one configured tab
        var uncovered = ShiftManager.Pages.Admin.Organization.Tabs.IndexModel.ComputeUncovered(allIds, tabSets);
        uncovered.Should().BeEquivalentTo(new[] { 2, 3 });

        // If ANY tab is empty (= all), coverage is complete → no warning.
        var withEmpty = new List<HashSet<int>> { new() { 1 }, new HashSet<int>() };
        ShiftManager.Pages.Admin.Organization.Tabs.IndexModel.ComputeUncovered(allIds, withEmpty)
            .Should().BeEmpty();
    }
}
