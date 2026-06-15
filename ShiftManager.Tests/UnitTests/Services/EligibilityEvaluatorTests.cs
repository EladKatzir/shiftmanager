using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Pure unit coverage for <see cref="EligibilityEvaluator"/> — no DB, no DI. Mirrors the
/// <c>MilitaryRankExtensionsTests</c> style (in-memory objects, FluentAssertions). Severity
/// mapping is NOT tested here (it lives in BusyService); this proves WHICH violations fire.
/// </summary>
public class EligibilityEvaluatorTests
{
    private static readonly EligibilityEvaluator Sut = new();

    private static EligibilityRule GenderRule(Gender g) => new()
    {
        ChoreTypeId = 1,
        RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = g
    };

    private static EligibilityRule OfficerRule() => new()
    {
        ChoreTypeId = 1,
        RuleKind = EligibilityRuleKind.RequiresOfficerRank
    };

    private static AppUser User(Gender g = Gender.Unspecified, MilitaryRank rank = MilitaryRank.Turai)
        => new() { Id = 5, Gender = g, Rank = rank };

    [Fact]
    public void No_Rules_No_Exemption_Is_Eligible()
    {
        var r = Sut.Evaluate(User(), Array.Empty<EligibilityRule>(), userHasExemptionForSubject: false);
        r.IsEligible.Should().BeTrue();
        r.Violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Female)]
    public void Matching_Gender_Is_Eligible(Gender required)
    {
        var r = Sut.Evaluate(User(g: required), new[] { GenderRule(required) }, false);
        r.IsEligible.Should().BeTrue();
        r.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Definite_Gender_Mismatch_Violates_Gender()
    {
        var r = Sut.Evaluate(User(g: Gender.Female), new[] { GenderRule(Gender.Male) }, false);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.RequiresGender);
    }

    [Fact]
    public void Unspecified_Gender_Also_Violates_Gender()
    {
        var r = Sut.Evaluate(User(g: Gender.Unspecified), new[] { GenderRule(Gender.Male) }, false);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.RequiresGender);
    }

    [Fact]
    public void Officer_Passes_Officer_Rule()
    {
        var r = Sut.Evaluate(User(rank: MilitaryRank.Seren), new[] { OfficerRule() }, false);
        r.IsEligible.Should().BeTrue();
    }

    [Fact]
    public void Enlisted_Violates_Officer_Rule()
    {
        var r = Sut.Evaluate(User(rank: MilitaryRank.Turai), new[] { OfficerRule() }, false);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.RequiresOfficerRank);
    }

    [Fact]
    public void Exemption_Violates_Even_With_No_Rules()
    {
        var r = Sut.Evaluate(User(), Array.Empty<EligibilityRule>(), userHasExemptionForSubject: true);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.Exempt);
    }

    [Fact]
    public void Multiple_Violations_All_Reported()
    {
        // Female-required + officer-required, user is an enlisted male with an exemption → all three fire.
        var user = User(g: Gender.Male, rank: MilitaryRank.Turai);
        var rules = new[] { GenderRule(Gender.Female), OfficerRule() };
        var r = Sut.Evaluate(user, rules, userHasExemptionForSubject: true);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().BeEquivalentTo(new[]
        {
            EligibilityViolation.RequiresGender,
            EligibilityViolation.RequiresOfficerRank,
            EligibilityViolation.Exempt
        });
    }

    [Fact]
    public void Gender_Rule_With_Null_GenderValue_Is_Ignored()
    {
        // Defensive: a RequiresGender rule with no GenderValue is malformed config — never blocks.
        var rule = new EligibilityRule
        {
            ChoreTypeId = 1,
            RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = null
        };
        var r = Sut.Evaluate(User(g: Gender.Unspecified), new[] { rule }, false);
        r.IsEligible.Should().BeTrue();
        r.Violations.Should().BeEmpty();
    }
}
