using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for <see cref="ChoreCategoryService"/>: category CRUD + uniqueness, the
/// chore-type↔category assignment (incl. cross-molecule rejection), usage counts, per-user membership
/// replace semantics, and FK behavior on delete (SetNull for chore types, cascade for memberships).
/// Cloned from <c>ShiftCategoryServiceTests</c>.
/// </summary>
public sealed class ChoreCategoryServiceTests
{
    private static async Task SeedHierarchyAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });
        // Creator for any ChoreType rows the tests seed (ChoreType.CreatedByUserId is a real FK).
        f.Db.Users.Add(new AppUser { Id = 1, CompanyId = 1, Email = "creator@x.mil", DisplayName = "Creator", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Enforces_Name_Uniqueness_Within_Molecule_But_Allows_Reuse_Across_Molecules()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        var first = await svc.CreateAsync(1, "Physical", "Physical");
        first.Should().NotBeNull();

        var dup = await svc.CreateAsync(1, "Physical", "Physical");
        dup.Should().BeNull("a category with that name already exists in molecule 1");

        var otherMolecule = await svc.CreateAsync(2, "Physical", "Physical");
        otherMolecule.Should().NotBeNull("the same name is free in a different molecule");
    }

    [Fact]
    public async Task Rename_Rejects_A_Colliding_Name()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        var a = await svc.CreateAsync(1, "Physical", "Physical");
        var b = await svc.CreateAsync(1, "Computer", "Computer");

        (await svc.RenameAsync(b!.Id, "Physical", "Physical", null))
            .Should().BeFalse("renaming Computer to Physical collides within the molecule");
        (await svc.RenameAsync(b.Id, "Computer 2", "Computer 2", "#ff0000"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AssignChoreType_Honors_Molecule_Boundary()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ChoreTypes.Add(new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1 });
        await f.Db.SaveChangesAsync();
        var svc = new ChoreCategoryService(f.Db);

        var catM1 = await svc.CreateAsync(1, "Physical", "Physical");
        var catM2 = await svc.CreateAsync(2, "Other", "Other");

        (await svc.AssignChoreTypeAsync(100, catM2!.Id))
            .Should().BeFalse("category in molecule 2 cannot own a molecule-1 chore type");

        (await svc.AssignChoreTypeAsync(100, catM1!.Id)).Should().BeTrue();
        (await f.Db.ChoreTypes.FindAsync(100))!.ChoreCategoryId.Should().Be(catM1.Id);

        (await svc.AssignChoreTypeAsync(100, null)).Should().BeTrue("clearing is always allowed");
        (await f.Db.ChoreTypes.FindAsync(100))!.ChoreCategoryId.Should().BeNull();
    }

    [Fact]
    public async Task GetUsage_Counts_ChoreTypes_And_Members()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var cat = await svc.CreateAsync(1, "Physical", "Physical");

        f.Db.ChoreTypes.AddRange(
            new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1, ChoreCategoryId = cat!.Id },
            new ChoreType { Id = 101, MoleculeId = 1, Name = "Guard", DisplayName = "Guard", CreatedByUserId = 1, ChoreCategoryId = cat.Id });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
        await svc.SetUserCategoriesAsync(10, new[] { cat.Id });

        var (choreTypeCount, memberCount) = await svc.GetUsageAsync(cat.Id);
        choreTypeCount.Should().Be(2);
        memberCount.Should().Be(1);
    }

    [Fact]
    public async Task SetUserCategories_Replaces_The_Membership_Set()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var physical = await svc.CreateAsync(1, "Physical", "Physical");
        var computer = await svc.CreateAsync(1, "Computer", "Computer");
        var extra = await svc.CreateAsync(1, "Extra", "Extra");
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();

        await svc.SetUserCategoriesAsync(10, new[] { physical!.Id, computer!.Id });
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEquivalentTo(new[] { physical.Id, computer.Id });

        await svc.SetUserCategoriesAsync(10, new[] { physical.Id, extra!.Id });
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEquivalentTo(new[] { physical.Id, extra.Id });

        await svc.SetUserCategoriesAsync(10, Array.Empty<int>());
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NullsChoreTypeFK_And_CascadesMembership()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var cat = await svc.CreateAsync(1, "Physical", "Physical");
        f.Db.ChoreTypes.Add(new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1, ChoreCategoryId = cat!.Id });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
        await svc.SetUserCategoriesAsync(10, new[] { cat.Id });

        (await svc.DeleteAsync(cat.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ChoreTypes.FindAsync(100))!.ChoreCategoryId.Should().BeNull("FK SetNull un-categorizes the chore type");
        (await f.Db.UserChoreCategories.CountAsync(m => m.ChoreCategoryId == cat.Id))
            .Should().Be(0, "membership cascades on delete");
    }
}
