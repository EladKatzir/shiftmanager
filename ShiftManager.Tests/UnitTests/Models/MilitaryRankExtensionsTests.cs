using FluentAssertions;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

public class MilitaryRankExtensionsTests
{
    [Theory]
    [InlineData(MilitaryRank.Turai, false)]
    [InlineData(MilitaryRank.RavNagad, false)]
    [InlineData(MilitaryRank.SegenMishne, true)]
    [InlineData(MilitaryRank.RavAluf, true)]
    public void IsOfficer_ReturnsCorrectValue(MilitaryRank rank, bool expected)
    {
        rank.IsOfficer().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, true)]
    [InlineData(MilitaryRank.RavTurai, true)]
    [InlineData(MilitaryRank.Samal, false)]
    [InlineData(MilitaryRank.SegenMishne, false)]
    public void IsEnlisted_ReturnsCorrectValue(MilitaryRank rank, bool expected)
    {
        rank.IsEnlisted().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Samal, true)]
    [InlineData(MilitaryRank.RavNagad, true)]
    [InlineData(MilitaryRank.Turai, false)]
    [InlineData(MilitaryRank.SegenMishne, false)]
    public void IsNCO_ReturnsCorrectValue(MilitaryRank rank, bool expected)
    {
        rank.IsNCO().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, "he", "טוראי")]
    [InlineData(MilitaryRank.Seren, "he", "סרן")]
    [InlineData(MilitaryRank.Turai, "en", "Private")]
    [InlineData(MilitaryRank.Seren, "en", "Captain")]
    public void GetDisplayName_ReturnsLocalizedName(MilitaryRank rank, string lang, string expected)
    {
        rank.GetDisplayName(lang).Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, "טר'")]
    [InlineData(MilitaryRank.Samal, "סמל")]
    [InlineData(MilitaryRank.Seren, "סרן")]
    public void GetAbbreviation_ReturnsShortForm(MilitaryRank rank, string expected)
    {
        rank.GetAbbreviation().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, "enlisted")]
    [InlineData(MilitaryRank.RavTurai, "enlisted")]
    [InlineData(MilitaryRank.Samal, "nco")]
    [InlineData(MilitaryRank.RavNagad, "nco")]
    [InlineData(MilitaryRank.SegenMishne, "officer")]
    [InlineData(MilitaryRank.RavAluf, "officer")]
    public void GetBadgeClass_ReturnsCorrectCssClass(MilitaryRank rank, string expected)
    {
        rank.GetBadgeClass().Should().Be(expected);
    }
}
