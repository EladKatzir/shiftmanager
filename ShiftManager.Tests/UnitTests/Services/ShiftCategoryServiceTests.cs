using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for <see cref="ShiftCategoryService"/>: category CRUD + uniqueness, the
/// shift-type↔category assignment (incl. cross-molecule rejection), usage counts, per-user membership
/// replace semantics, and FK behavior on delete (SetNull for shift types, cascade for memberships).
/// </summary>
public sealed class ShiftCategoryServiceTests
{
    private static async Task SeedHierarchyAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Enforces_Name_Uniqueness_Within_Molecule_But_Allows_Reuse_Across_Molecules()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftCategoryService(f.Db);

        var first = await svc.CreateAsync(1, "Yekev", "Yekev");
        first.Should().NotBeNull();

        var dup = await svc.CreateAsync(1, "Yekev", "Yekev");
        dup.Should().BeNull("a category with that name already exists in molecule 1");

        var otherMolecule = await svc.CreateAsync(2, "Yekev", "Yekev");
        otherMolecule.Should().NotBeNull("the same name is free in a different molecule");
    }

    [Fact]
    public async Task Rename_Rejects_A_Colliding_Name()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftCategoryService(f.Db);

        var a = await svc.CreateAsync(1, "Yekev", "Yekev");
        var b = await svc.CreateAsync(1, "Hazon", "Hazon");

        (await svc.RenameAsync(b!.Id, "Yekev", "Yekev", null))
            .Should().BeFalse("renaming Hazon to Yekev collides within the molecule");
        (await svc.RenameAsync(b.Id, "Hazon 2", "Hazon 2", "#ff0000"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AssignShiftType_Honors_Molecule_Boundary()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftCategoryService(f.Db);

        var catM1 = await svc.CreateAsync(1, "Yekev", "Yekev");
        var catM2 = await svc.CreateAsync(2, "Other", "Other");

        (await svc.AssignShiftTypeAsync(100, catM2!.Id))
            .Should().BeFalse("category in molecule 2 cannot own a molecule-1 shift type");

        (await svc.AssignShiftTypeAsync(100, catM1!.Id)).Should().BeTrue();
        (await f.Db.ShiftTypes.FindAsync(100))!.CategoryId.Should().Be(catM1.Id);

        (await svc.AssignShiftTypeAsync(100, null)).Should().BeTrue("clearing is always allowed");
        (await f.Db.ShiftTypes.FindAsync(100))!.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task GetUsage_Counts_ShiftTypes_And_Members()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftCategoryService(f.Db);
        var cat = await svc.CreateAsync(1, "Yekev", "Yekev");

        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING", CategoryId = cat!.Id },
            new ShiftType { Id = 101, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "NIGHT", CategoryId = cat.Id });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" });
        await f.Db.SaveChangesAsync();
        await svc.SetUserCategoriesAsync(10, new[] { cat.Id });

        var (shiftTypeCount, memberCount) = await svc.GetUsageAsync(cat.Id);
        shiftTypeCount.Should().Be(2);
        memberCount.Should().Be(1);
    }

    [Fact]
    public async Task SetUserCategories_Replaces_The_Membership_Set()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftCategoryService(f.Db);
        var yekev = await svc.CreateAsync(1, "Yekev", "Yekev");
        var hazon = await svc.CreateAsync(1, "Hazon", "Hazon");
        var extra = await svc.CreateAsync(1, "Extra", "Extra");
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" });
        await f.Db.SaveChangesAsync();

        await svc.SetUserCategoriesAsync(10, new[] { yekev!.Id, hazon!.Id });
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEquivalentTo(new[] { yekev.Id, hazon.Id });

        // Replace: drop Hazon, add Extra, keep Yekev.
        await svc.SetUserCategoriesAsync(10, new[] { yekev.Id, extra!.Id });
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEquivalentTo(new[] { yekev.Id, extra.Id });

        // Empty set clears all.
        await svc.SetUserCategoriesAsync(10, Array.Empty<int>());
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NullsShiftTypeFK_And_CascadesMembership()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftCategoryService(f.Db);
        var cat = await svc.CreateAsync(1, "Yekev", "Yekev");
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING", CategoryId = cat!.Id });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" });
        await f.Db.SaveChangesAsync();
        await svc.SetUserCategoriesAsync(10, new[] { cat.Id });

        (await svc.DeleteAsync(cat.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ShiftTypes.FindAsync(100))!.CategoryId.Should().BeNull("FK SetNull un-categorizes the shift");
        (await f.Db.UserShiftCategories.CountAsync(m => m.ShiftCategoryId == cat.Id))
            .Should().Be(0, "membership cascades on delete");
    }
}
