using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the chore-eligibility gates added to <see cref="BusyService.ValidateChoreAsync"/>:
/// officer-rank + exemption are HARD errors (NOT clearable by an override token); gender is an
/// overrideable WARNING (definite mismatch AND Unspecified); free-text (null ChoreTypeId) bypasses
/// eligibility entirely. Mirrors the ShiftCategoryBackfillTests SQLite setup idiom.
/// </summary>
public sealed class BusyServiceChoreEligibilityTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private BusyService _sut = null!;

    private const int Molecule = 1;
    private const int OfficerChoreType = 200;
    private const int FemaleChoreType = 201;
    private const int ExemptChoreType = 202;
    private static readonly DateOnly Date = new(2026, 7, 1);

    // A no-op IStringLocalizer that echoes the key back as the value (key-as-value).
    private sealed class EchoLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, false);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = Molecule, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = Molecule });

        // Users (all active Standard so they pass USER_*/CanDoChores; differ on Gender + Rank).
        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "officer@x.mil", DisplayName = "Officer",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Seren, Gender = Gender.Male },
            new AppUser { Id = 11, CompanyId = 1, Email = "enlisted@x.mil", DisplayName = "Enlisted",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Turai, Gender = Gender.Male },
            new AppUser { Id = 12, CompanyId = 1, Email = "female@x.mil", DisplayName = "Female",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Turai, Gender = Gender.Female },
            new AppUser { Id = 13, CompanyId = 1, Email = "unspec@x.mil", DisplayName = "Unspec",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Turai, Gender = Gender.Unspecified });

        _db.ChoreTypes.AddRange(
            new ChoreType { Id = OfficerChoreType, MoleculeId = Molecule, Name = "Guard", DisplayName = "Guard", CreatedByUserId = 10 },
            new ChoreType { Id = FemaleChoreType, MoleculeId = Molecule, Name = "FemaleOnly", DisplayName = "FemaleOnly", CreatedByUserId = 10 },
            new ChoreType { Id = ExemptChoreType, MoleculeId = Molecule, Name = "Heavy", DisplayName = "Heavy", CreatedByUserId = 10 });
        await _db.SaveChangesAsync();

        _db.EligibilityRules.AddRange(
            new EligibilityRule { ChoreTypeId = OfficerChoreType,
                                  RuleKind = EligibilityRuleKind.RequiresOfficerRank, CreatedBy = 10 },
            new EligibilityRule { ChoreTypeId = FemaleChoreType,
                                  RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = Gender.Female, CreatedBy = 10 });
        // User 11 (enlisted male) is exempt from the Heavy chore type.
        _db.UserChoreExemptions.Add(new UserChoreExemption { UserId = 11, ChoreTypeId = ExemptChoreType, CreatedBy = 10 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeyHmacSecret"] = "test-secret-please-change" })
            .Build();

        // The only ICompanyMembershipService member BusyService.IsUserInMoleculeAsync touches is
        // GetMembershipsAsync — a permissive Moq returning an empty list keeps a user in their own
        // primary company only.
        var membership = new Mock<ICompanyMembershipService>();
        membership.Setup(m => m.GetMembershipsAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<CompanyMembership>());

        _sut = new BusyService(
            _db, new EchoLocalizer(), NullLogger<BusyService>.Instance,
            hierarchySettingsService: null!, configCache: null!, configuration: config,
            membershipService: membership.Object,
            eligibilityEvaluator: new EligibilityEvaluator());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private BusyTarget.Chore Target(int? choreTypeId) => new(Date, Molecule, choreTypeId);

    [Fact]
    public async Task Officer_Rule_Blocks_Enlisted_As_Hard_Error()
    {
        var v = await _sut.ValidateAsync(Target(OfficerChoreType), userId: 11, actorUserId: 0);
        v.CanProceed.Should().BeFalse();
        v.Errors.Should().ContainSingle(e => e.Key == "ELIG_OFFICER_RANK");
    }

    [Fact]
    public async Task Officer_Rule_Passes_Officer()
    {
        var v = await _sut.ValidateAsync(Target(OfficerChoreType), userId: 10, actorUserId: 0);
        v.CanProceed.Should().BeTrue();
        v.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Officer_Hard_Error_Is_Not_Cleared_By_Override_Token()
    {
        var token = _sut.GenerateOverrideToken(Target(OfficerChoreType), userId: 11, new[] { "ELIG_OFFICER_RANK" });
        var v = await _sut.ValidateAsync(Target(OfficerChoreType), userId: 11, actorUserId: 0, overrideToken: token);
        v.CanProceed.Should().BeFalse("officer-rank is a HARD error — tokens only clear warnings");
        v.Errors.Should().ContainSingle(e => e.Key == "ELIG_OFFICER_RANK");
    }

    [Fact]
    public async Task Exemption_Blocks_As_Hard_Error_Not_Cleared_By_Token()
    {
        var token = _sut.GenerateOverrideToken(Target(ExemptChoreType), userId: 11, new[] { "ELIG_EXEMPT" });
        var v = await _sut.ValidateAsync(Target(ExemptChoreType), userId: 11, actorUserId: 0, overrideToken: token);
        v.CanProceed.Should().BeFalse();
        v.Errors.Should().ContainSingle(e => e.Key == "ELIG_EXEMPT");
    }

    [Fact]
    public async Task Female_Rule_On_Male_Is_An_Overrideable_Warning()
    {
        // User 11 is male → mismatch against a female-only chore → WARNING, blocks WITHOUT a token.
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 11, actorUserId: 0);
        v.CanProceed.Should().BeTrue("gender is a warning, not a hard error — errors are empty");
        v.Warnings.Should().ContainSingle(w => w.Key == "ELIG_GENDER");
    }

    [Fact]
    public async Task Gender_Warning_Is_Cleared_By_A_Valid_Override_Token()
    {
        var token = _sut.GenerateOverrideToken(Target(FemaleChoreType), userId: 11, new[] { "ELIG_GENDER" });
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 11, actorUserId: 0, overrideToken: token);
        v.CanProceed.Should().BeTrue();
        v.Warnings.Should().BeEmpty("a valid token clears the gender warning");
    }

    [Fact]
    public async Task Unspecified_Gender_Also_Warns_And_Is_Overrideable()
    {
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 13, actorUserId: 0);
        v.Warnings.Should().ContainSingle(w => w.Key == "ELIG_GENDER");

        var token = _sut.GenerateOverrideToken(Target(FemaleChoreType), userId: 13, new[] { "ELIG_GENDER" });
        var cleared = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 13, actorUserId: 0, overrideToken: token);
        cleared.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Matching_Female_Passes_Clean()
    {
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 12, actorUserId: 0);
        v.CanProceed.Should().BeTrue();
        v.Warnings.Should().NotContain(w => w.Key == "ELIG_GENDER");
    }

    [Fact]
    public async Task Free_Text_Chore_Bypasses_Eligibility_Entirely()
    {
        // No ChoreTypeId → no rules → no exemption lookup → enlisted user is eligible.
        var v = await _sut.ValidateAsync(Target(choreTypeId: null), userId: 11, actorUserId: 0);
        v.CanProceed.Should().BeTrue();
        v.Errors.Should().BeEmpty();
        v.Warnings.Should().BeEmpty();
    }
}
