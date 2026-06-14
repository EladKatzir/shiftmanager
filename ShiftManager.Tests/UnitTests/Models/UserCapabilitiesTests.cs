using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

public class UserCapabilitiesTests
{
    private static AppUser U(AccountType t, bool doesShifts = true) =>
        new() { AccountType = t, DoesShifts = doesShifts };

    [Theory]
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, true)]
    [InlineData(AccountType.GroupUser, false)]
    public void CanBeAssignedShift_matrix(AccountType t, bool expected) =>
        U(t).CanBeAssignedShift().Should().Be(expected);

    [Theory]
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, false)]
    [InlineData(AccountType.GroupUser, false)]
    public void CanDoChores_matrix(AccountType t, bool expected) =>
        U(t).CanDoChores().Should().Be(expected);

    [Theory]
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, false)]
    [InlineData(AccountType.GroupUser, false)]
    public void Requests_matrix(AccountType t, bool expected)
    {
        U(t).CanAccessRequests().Should().Be(expected);
        U(t).CanBeVacationApprover().Should().Be(expected);
    }

    [Theory]
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, false)]
    [InlineData(AccountType.GroupUser, false)]
    public void OverviewAndChoreVisibility_matrix(AccountType t, bool expected)
    {
        U(t).IsVisibleOnOverview().Should().Be(expected);
        U(t).IsVisibleOnChoresCalendar().Should().Be(expected);
    }

    [Theory]
    [InlineData(AccountType.Standard, true)]
    [InlineData(AccountType.Mil, true)]
    [InlineData(AccountType.GroupUser, false)]
    public void ShiftsAndAnalyticsVisibility_matrix(AccountType t, bool expected)
    {
        U(t).IsVisibleOnShiftsCalendar().Should().Be(expected);
        U(t).IsVisibleInAnalytics().Should().Be(expected);
    }
}
