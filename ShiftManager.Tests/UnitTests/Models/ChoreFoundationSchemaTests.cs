using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

/// <summary>Schema-level guards for the chore-parity entities: round-trip persistence,
/// the (MoleculeId,Name) / (UserId,ChoreCategoryId) / (UserId,ChoreTypeId) unique indexes,
/// and ChoreType↔ChoreCategory SetNull behavior. Real SQLite (exercises SQL translation + FKs).</summary>
public sealed class ChoreFoundationSchemaTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        _db.Users.Add(new AppUser { Id = 1, CompanyId = 1, Email = "u@x.mil", DisplayName = "U", AccountType = AccountType.Standard });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ChoreCategory_Name_Is_Unique_Per_Molecule()
    {
        _db.ChoreCategories.Add(new ChoreCategory { MoleculeId = 1, Name = "Physical", DisplayName = "Physical" });
        await _db.SaveChangesAsync();
        _db.ChoreCategories.Add(new ChoreCategory { MoleculeId = 1, Name = "Physical", DisplayName = "Physical 2" });

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("(MoleculeId, Name) is unique");
    }

    [Fact]
    public async Task ChoreType_Category_SetNull_On_Category_Delete()
    {
        var cat = new ChoreCategory { MoleculeId = 1, Name = "Computer", DisplayName = "Computer" };
        _db.ChoreCategories.Add(cat);
        await _db.SaveChangesAsync();
        var type = new ChoreType { MoleculeId = 1, Name = "Backups", DisplayName = "Backups", CreatedByUserId = 1, ChoreCategoryId = cat.Id };
        _db.ChoreTypes.Add(type);
        await _db.SaveChangesAsync();

        _db.ChoreCategories.Remove(cat);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var reloaded = await _db.ChoreTypes.IgnoreQueryFilters().SingleAsync(t => t.Id == type.Id);
        reloaded.ChoreCategoryId.Should().BeNull("SetNull un-categorizes the type, does not delete it");
    }

    [Fact]
    public async Task UserChoreCategory_Is_Unique_Per_Pair()
    {
        var cat = new ChoreCategory { MoleculeId = 1, Name = "Phys", DisplayName = "Phys" };
        _db.ChoreCategories.Add(cat);
        await _db.SaveChangesAsync();
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 1, ChoreCategoryId = cat.Id });
        await _db.SaveChangesAsync();
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 1, ChoreCategoryId = cat.Id });

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("(UserId, ChoreCategoryId) is unique");
    }

    [Fact]
    public async Task UserChoreExemption_Is_Unique_Per_User_And_Type()
    {
        var type = new ChoreType { MoleculeId = 1, Name = "Heavy", DisplayName = "Heavy", CreatedByUserId = 1 };
        _db.ChoreTypes.Add(type);
        await _db.SaveChangesAsync();
        _db.UserChoreExemptions.Add(new UserChoreExemption { UserId = 1, ChoreTypeId = type.Id, CreatedBy = 1 });
        await _db.SaveChangesAsync();
        _db.UserChoreExemptions.Add(new UserChoreExemption { UserId = 1, ChoreTypeId = type.Id, CreatedBy = 1 });

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("(UserId, ChoreTypeId) is unique");
    }

    [Fact]
    public async Task EligibilityRule_And_ChoreTemplate_RoundTrip()
    {
        var type = new ChoreType { MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1 };
        _db.ChoreTypes.Add(type);
        await _db.SaveChangesAsync();

        _db.EligibilityRules.Add(new EligibilityRule
        {
            ChoreTypeId = type.Id,
            RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = Gender.Female, CreatedBy = 1
        });
        _db.ChoreTemplates.Add(new ChoreTemplate
        {
            MoleculeId = 1, Name = "Daily Kitchen", ChoreTypeId = type.Id, DefaultTitle = "Clean kitchen",
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(13, 0), CreatedBy = 1
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await _db.EligibilityRules.IgnoreQueryFilters().SingleAsync()).GenderValue.Should().Be(Gender.Female);
        var tpl = await _db.ChoreTemplates.IgnoreQueryFilters().SingleAsync();
        tpl.StartTime.Should().Be(new TimeOnly(9, 0), "TimeOnly converter round-trips HH:mm");
        tpl.EndTime.Should().Be(new TimeOnly(13, 0));
    }

    [Fact]
    public async Task AppUser_Gender_Defaults_To_Unspecified()
    {
        var reloaded = await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 1);
        reloaded.Gender.Should().Be(Gender.Unspecified);
        reloaded.DoesChores.Should().BeFalse();
    }
}
