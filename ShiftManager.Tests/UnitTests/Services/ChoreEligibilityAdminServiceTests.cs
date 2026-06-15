using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for <see cref="ChoreEligibilityAdminService"/>: rule replace-semantics
/// (gender + officer), idempotent exemption add, exemption remove, and not-found guards.
/// </summary>
public sealed class ChoreEligibilityAdminServiceTests
{
    private const int ChoreTypeId = 100;

    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        f.Db.ChoreTypes.Add(new ChoreType { Id = ChoreTypeId, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 10 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task SetRules_Replaces_With_Gender_And_Officer()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        (await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Female, requiresOfficerRank: true, createdBy: 10))
            .Should().BeTrue();

        var rules = await svc.GetRulesForChoreTypeAsync(ChoreTypeId);
        rules.Should().HaveCount(2);
        rules.Should().ContainSingle(r => r.RuleKind == EligibilityRuleKind.RequiresGender && r.GenderValue == Gender.Female);
        rules.Should().ContainSingle(r => r.RuleKind == EligibilityRuleKind.RequiresOfficerRank && r.GenderValue == null);
    }

    [Fact]
    public async Task SetRules_Is_Replace_Not_Append()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Male, requiresOfficerRank: true, createdBy: 10);
        // Now narrow to female-only, no officer.
        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Female, requiresOfficerRank: false, createdBy: 10);

        var rules = await svc.GetRulesForChoreTypeAsync(ChoreTypeId);
        rules.Should().ContainSingle().Which.RuleKind.Should().Be(EligibilityRuleKind.RequiresGender);
        rules[0].GenderValue.Should().Be(Gender.Female, "the prior Male + officer rules were replaced, not merged");
    }

    [Fact]
    public async Task SetRules_With_All_Cleared_Removes_Every_Rule()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Female, requiresOfficerRank: true, createdBy: 10);
        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, requiredGender: null, requiresOfficerRank: false, createdBy: 10);

        (await svc.GetRulesForChoreTypeAsync(ChoreTypeId)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetRules_Returns_False_For_Missing_Type()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        (await svc.SetRulesForChoreTypeAsync(99999, Gender.Female, false, 10)).Should().BeFalse();
    }

    [Fact]
    public async Task AddExemption_Is_Idempotent()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        var first = await svc.AddExemptionAsync(10, ChoreTypeId, "knee injury", createdBy: 10);
        first.Should().NotBeNull();

        var second = await svc.AddExemptionAsync(10, ChoreTypeId, "different reason", createdBy: 10);
        second!.Id.Should().Be(first!.Id, "re-adding returns the existing waiver, no duplicate");

        (await svc.GetExemptionsForChoreTypeAsync(ChoreTypeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task AddExemption_Returns_Null_For_Missing_User_Or_Type()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        (await svc.AddExemptionAsync(99999, ChoreTypeId, null, 10)).Should().BeNull("missing user");
        (await svc.AddExemptionAsync(10, 99999, null, 10)).Should().BeNull("missing chore type");
    }

    [Fact]
    public async Task RemoveExemption_Works_And_Reports_Missing()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        await svc.AddExemptionAsync(10, ChoreTypeId, null, 10);
        (await svc.RemoveExemptionAsync(10, ChoreTypeId)).Should().BeTrue();
        (await svc.RemoveExemptionAsync(10, ChoreTypeId)).Should().BeFalse("already removed");
        (await svc.GetExemptionsForChoreTypeAsync(ChoreTypeId)).Should().BeEmpty();
    }
}
