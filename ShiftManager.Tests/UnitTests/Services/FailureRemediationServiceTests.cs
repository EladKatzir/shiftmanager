using Microsoft.Extensions.Localization;
using Moq;
using ShiftManager.Resources;
using ShiftManager.Services.Remediation;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// The failure → "go fix it" remediation mapping. Verifies each known failure code routes to the
/// right place, that officer-rank is user-context-aware, and that unknown codes have no fix.
/// </summary>
public class FailureRemediationServiceTests
{
    private static FailureRemediationService Build()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns((string k) => new LocalizedString(k, k));
        return new FailureRemediationService(loc.Object);
    }

    [Theory]
    [InlineData("ELIG_OFFICER_RANK")]
    [InlineData("OFFICER_RANK_REQUIRED")]
    public void OfficerRank_PointsAtThatUsersProfile(string key)
    {
        var fix = Build().For(key, userId: 42, choreTypeId: 7);
        Assert.NotNull(fix);
        Assert.Equal("/Admin/EditProfile?UserId=42", fix!.Url);
        Assert.Equal("Fix_ChangeRank", fix.Label);
    }

    [Fact]
    public void OfficerRank_WithoutUserId_FallsBackToRoster()
    {
        var fix = Build().For("ELIG_OFFICER_RANK");
        Assert.Equal("/Admin/Users", fix!.Url);
    }

    [Fact]
    public void Exempt_PointsAtChoreTypeRules()
    {
        Assert.Equal("/Admin/Organization/ChoreTypes", Build().For("ELIG_EXEMPT")!.Url);
    }

    [Theory]
    [InlineData("ACCOUNT_CANNOT_DO_CHORES")]
    [InlineData("GROUPUSER_CANNOT_BE_ASSIGNED")]
    [InlineData("USER_INACTIVE")]
    public void AccountIssues_PointAtUserManagement(string key)
    {
        Assert.Equal("/Admin/Users", Build().For(key)!.Url);
    }

    [Theory]
    [InlineData("COMPANY_INELIGIBLE")]
    [InlineData("NOT_IN_SHIFT_GROUPING")]
    [InlineData("USER_NOT_IN_MOLECULE")]
    public void WhoCanDoWhat_PointsAtEligibilityEditor(string key)
    {
        Assert.Equal("/Scheduling/Eligibility", Build().For(key)!.Url);
    }

    [Theory]
    [InlineData("OVERLAP")]
    [InlineData("REST_HOURS_VIOLATION")]
    [InlineData(null)]
    [InlineData("")]
    public void UnknownOrNonRemediableCodes_HaveNoFix(string? key)
    {
        Assert.Null(Build().For(key));
    }
}
