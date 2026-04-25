using FluentAssertions;
using ShiftManager.Models.Results;
using ShiftManager.Models.Validation;

namespace ShiftManager.Tests.UnitTests.Models.Results;

/// <summary>
/// Tests for the project-wide OperationResult / OperationResult&lt;T&gt; primitive.
/// Introduced as part of the error-handling overhaul; replaces the ad-hoc
/// (bool, string, T?) tuple pattern that proliferated across services.
/// </summary>
public class OperationResultTests
{
    [Fact]
    public void Ok_NonGeneric_ProducesSuccessfulResultWithNoIssues()
    {
        var r = OperationResult.Ok();

        r.Success.Should().BeTrue();
        r.ErrorKey.Should().BeNull();
        r.ErrorMessage.Should().BeNull();
        r.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Fail_NonGeneric_PopulatesErrorKeyAndMessage()
    {
        var r = OperationResult.Fail("Error_X", "Localized message");

        r.Success.Should().BeFalse();
        r.ErrorKey.Should().Be("Error_X");
        r.ErrorMessage.Should().Be("Localized message");
        r.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Fail_NonGeneric_AcceptsAdditionalIssues()
    {
        var issue = new ValidationIssue("Sub_X", "Sub message", ValidationSeverity.Warning, ValidationCategory.NotFound);
        var r = OperationResult.Fail("Error_X", "msg", issue);

        r.Issues.Should().HaveCount(1);
        r.Issues[0].Should().Be(issue);
    }

    [Fact]
    public void Ok_Generic_CarriesValue()
    {
        var r = OperationResult<int>.Ok(42);

        r.Success.Should().BeTrue();
        r.Value.Should().Be(42);
        r.ErrorKey.Should().BeNull();
        r.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Fail_Generic_HasNullValueAndPopulatesError()
    {
        var r = OperationResult<int>.Fail("Error_X", "Localized message");

        r.Success.Should().BeFalse();
        r.Value.Should().Be(default);
        r.ErrorKey.Should().Be("Error_X");
        r.ErrorMessage.Should().Be("Localized message");
    }

    [Fact]
    public void Fail_GenericWithReferenceType_ValueIsNull()
    {
        var r = OperationResult<string>.Fail("Error_X", "msg");

        r.Value.Should().BeNull();
    }

    [Fact]
    public void FromIssues_AllWarnings_ProducesSuccess()
    {
        var issues = new[]
        {
            new ValidationIssue("W1", "Warn 1", ValidationSeverity.Warning, ValidationCategory.RestHours),
            new ValidationIssue("W2", "Warn 2", ValidationSeverity.Warning, ValidationCategory.WeeklyHours),
        };

        var r = OperationResult.FromIssues(issues);

        r.Success.Should().BeTrue();
        r.ErrorKey.Should().BeNull();
        r.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void FromIssues_ContainsError_ProducesFailureWithFirstError()
    {
        var issues = new[]
        {
            new ValidationIssue("W1", "Warn 1", ValidationSeverity.Warning, ValidationCategory.RestHours),
            new ValidationIssue("E1", "Err 1", ValidationSeverity.Error, ValidationCategory.JobType),
            new ValidationIssue("E2", "Err 2", ValidationSeverity.Error, ValidationCategory.Overlap),
        };

        var r = OperationResult.FromIssues(issues);

        r.Success.Should().BeFalse();
        r.ErrorKey.Should().Be("E1");
        r.ErrorMessage.Should().Be("Err 1");
        r.Issues.Should().HaveCount(3);
    }

    [Fact]
    public void FromResult_LiftsNonGenericIntoGeneric()
    {
        var failed = OperationResult.Fail("Error_X", "msg");
        var lifted = OperationResult<int>.FromResult(failed, default);

        lifted.Success.Should().BeFalse();
        lifted.ErrorKey.Should().Be("Error_X");
        lifted.ErrorMessage.Should().Be("msg");
    }

    [Fact]
    public void FromResult_SuccessfulNonGenericLiftsWithValue()
    {
        var ok = OperationResult.Ok();
        var lifted = OperationResult<int>.FromResult(ok, 99);

        lifted.Success.Should().BeTrue();
        lifted.Value.Should().Be(99);
    }
}
