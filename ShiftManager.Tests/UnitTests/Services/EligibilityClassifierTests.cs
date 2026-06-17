using System;
using System.Collections.Generic;
using System.Linq;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Services.Eligibility;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Pure classification of (in)eligibility reasons — mirrors the silent shift candidate predicates
/// and reuses the chore evaluator's rule violations, but EMITS reasons.
/// </summary>
public class EligibilityClassifierTests
{
    // ---- shift membership ----
    [Fact]
    public void Shift_StandardParticipatingMember_IsEligible()
    {
        var r = EligibilityClassifier.ClassifyMembership(AccountType.Standard, participates: true, isMember: true, isChore: false);
        Assert.Empty(r);
        Assert.True(EligibilityClassifier.IsEligible(r));
    }

    [Fact]
    public void Shift_GroupUser_IsAccountTypeIneligible()
    {
        var r = EligibilityClassifier.ClassifyMembership(AccountType.GroupUser, participates: true, isMember: true, isChore: false);
        Assert.Contains(EligibilityReason.AccountTypeIneligible, r);
    }

    [Fact]
    public void Shift_NotParticipating_And_NotMember_ListsBothReasons()
    {
        var r = EligibilityClassifier.ClassifyMembership(AccountType.Standard, participates: false, isMember: false, isChore: false);
        Assert.Contains(EligibilityReason.NotParticipating, r);
        Assert.Contains(EligibilityReason.NotCategoryMember, r);
        Assert.False(EligibilityClassifier.IsEligible(r));
    }

    [Fact]
    public void Shift_MilAccount_IsAllowed()
    {
        // Mil users DO shifts (only GroupUser is excluded on the shift axis).
        var r = EligibilityClassifier.ClassifyMembership(AccountType.Mil, participates: true, isMember: true, isChore: false);
        Assert.Empty(r);
    }

    // ---- chore membership (Standard-only) ----
    [Fact]
    public void Chore_NonStandardAccount_IsAccountTypeIneligible()
    {
        var mil = EligibilityClassifier.ClassifyMembership(AccountType.Mil, participates: true, isMember: true, isChore: true);
        var grp = EligibilityClassifier.ClassifyMembership(AccountType.GroupUser, participates: true, isMember: true, isChore: true);
        Assert.Contains(EligibilityReason.AccountTypeIneligible, mil);
        Assert.Contains(EligibilityReason.AccountTypeIneligible, grp);
    }

    [Fact]
    public void Chore_StandardParticipatingMember_IsEligible()
    {
        var r = EligibilityClassifier.ClassifyMembership(AccountType.Standard, participates: true, isMember: true, isChore: true);
        Assert.Empty(r);
    }

    // ---- chore rule mapping ----
    [Fact]
    public void MapRuleViolations_MapsAllThreeKinds()
    {
        var result = new EligibilityResult(new[]
        {
            EligibilityViolation.RequiresGender,
            EligibilityViolation.RequiresOfficerRank,
            EligibilityViolation.Exempt
        });
        var mapped = EligibilityClassifier.MapRuleViolations(result).ToList();
        Assert.Contains(EligibilityReason.RequiresGender, mapped);
        Assert.Contains(EligibilityReason.RequiresOfficerRank, mapped);
        Assert.Contains(EligibilityReason.Exempt, mapped);
    }

    [Fact]
    public void MapRuleViolations_NoViolations_IsEmpty()
    {
        Assert.Empty(EligibilityClassifier.MapRuleViolations(EligibilityResult.Eligible));
    }
}
