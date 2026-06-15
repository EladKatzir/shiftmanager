using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>Covers the gap-1 bilingual NameEn/NameHe extension on ChoreCategoryService Create/Rename.</summary>
public sealed class ChoreCategoryServiceBilingualTests
{
    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Persists_Bilingual_Names()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        var c = await svc.CreateAsync(1, "Physical", "Physical", color: null, nameEn: "Physical", nameHe: "פיזי");
        c.Should().NotBeNull();
        c!.NameEn.Should().Be("Physical");
        c.NameHe.Should().Be("פיזי");
    }

    [Fact]
    public async Task Rename_Updates_Bilingual_Names_And_Blank_Becomes_Null()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var c = await svc.CreateAsync(1, "Physical", "Physical", null, "Physical", "פיזי");

        (await svc.RenameAsync(c!.Id, "Manual", "Manual", color: "#3b82f6", nameEn: "Manual", nameHe: "ידני"))
            .Should().BeTrue();
        var reread = await svc.GetCategoryAsync(c.Id);
        reread!.NameEn.Should().Be("Manual");
        reread.NameHe.Should().Be("ידני");

        (await svc.RenameAsync(c.Id, "Manual", "Manual", null, nameEn: "  ", nameHe: null)).Should().BeTrue();
        reread = await svc.GetCategoryAsync(c.Id);
        reread!.NameEn.Should().BeNull("blank trims to null");
        reread.NameHe.Should().BeNull();
    }

    [Fact]
    public async Task Create_WithoutBilingual_Defaults_To_Null()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        // Backward-compat call (Phase 2 signature) leaves NameEn/NameHe null.
        var c = await svc.CreateAsync(1, "Physical", "Physical");
        c.Should().NotBeNull();
        c!.NameEn.Should().BeNull();
        c.NameHe.Should().BeNull();
    }
}
