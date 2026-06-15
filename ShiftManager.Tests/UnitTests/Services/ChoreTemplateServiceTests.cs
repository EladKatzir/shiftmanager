using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>Real-SQLite CRUD coverage for ChoreTemplateService (molecule-scoped, mirrors ChoreTypeService).</summary>
public sealed class ChoreTemplateServiceTests
{
    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" });
        f.Db.Molecules.Add(new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Then_List_And_Update()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);

        var t = await svc.CreateAsync(moleculeId: 1, name: "Kitchen close", choreTypeId: null,
            defaultTitle: "Close kitchen", startTime: null, endTime: null,
            weightMinutesOverride: null, notes: null, userId: 10);
        t.Should().NotBeNull();
        t!.IsActive.Should().BeTrue();

        (await svc.GetTemplatesForMoleculeAsync(1)).Should().ContainSingle();
        (await svc.GetTemplatesForMoleculeAsync(2)).Should().BeEmpty("templates are molecule-scoped");

        (await svc.UpdateAsync(t.Id, "Kitchen open", choreTypeId: null, defaultTitle: "Open kitchen",
            startTime: new TimeOnly(8, 0), endTime: new TimeOnly(10, 0), weightMinutesOverride: 120, notes: "am"))
            .Should().BeTrue();
        var reread = await svc.GetByIdAsync(t.Id);
        reread!.Name.Should().Be("Kitchen open");
        reread.DefaultTitle.Should().Be("Open kitchen");
        reread.WeightMinutesOverride.Should().Be(120);
        reread.StartTime.Should().Be(new TimeOnly(8, 0));
        reread.Notes.Should().Be("am");
    }

    [Fact]
    public async Task Create_BlankName_Returns_Null()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);

        (await svc.CreateAsync(1, "   ", null, "title", null, null, null, null, 10)).Should().BeNull();
        (await svc.CreateAsync(1, "name", null, "   ", null, null, null, null, 10)).Should().BeNull();
    }

    [Fact]
    public async Task Deactivate_Hides_From_Default_List_But_Visible_With_IncludeInactive()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);
        var t = await svc.CreateAsync(1, "T", null, "title", null, null, null, null, 10);

        (await svc.DeactivateAsync(t!.Id)).Should().BeTrue();
        (await svc.GetTemplatesForMoleculeAsync(1)).Should().BeEmpty();
        (await svc.GetTemplatesForMoleculeAsync(1, includeInactive: true)).Should().ContainSingle();

        (await svc.ActivateAsync(t.Id)).Should().BeTrue();
        (await svc.GetTemplatesForMoleculeAsync(1)).Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_Removes_Row()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);
        var t = await svc.CreateAsync(1, "T", null, "title", null, null, null, null, 10);

        (await svc.DeleteAsync(t!.Id)).Should().BeTrue();
        (await svc.GetByIdAsync(t.Id)).Should().BeNull();
        (await svc.DeleteAsync(99999)).Should().BeFalse();
    }
}
