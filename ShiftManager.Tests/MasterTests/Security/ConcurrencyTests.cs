using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.Security;

/// <summary>
/// Tests for ConcurrencyService.SaveWithConcurrencyHandlingAsync.
/// Since InMemory provider doesn't support real concurrency tokens,
/// these tests verify the service's behavior by controlling the saveAction delegate.
/// </summary>
public class ConcurrencyTests : MasterTestBase
{
    public ConcurrencyTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // SUCCESS PATH
    // ================================================================

    [Fact]
    public async Task Save_Success_ReturnsTrue()
    {
        var service = CreateConcurrencyService();

        var result = await service.SaveWithConcurrencyHandlingAsync(
            () => Task.FromResult(1),
            "TestEntity",
            42);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Save_Success_NoFailureType()
    {
        var service = CreateConcurrencyService();

        var result = await service.SaveWithConcurrencyHandlingAsync(
            () => Task.FromResult(1),
            "TestEntity",
            42);

        result.FailureType.Should().BeNull(
            "Successful save should have no failure type");
        result.ErrorMessage.Should().BeNull(
            "Successful save should have no error message");
    }

    // ================================================================
    // CONCURRENCY CONFLICT PATH
    // ================================================================

    [Fact]
    public async Task Save_ConcurrencyConflict_ReturnsFalse()
    {
        var service = CreateConcurrencyService();

        var result = await service.SaveWithConcurrencyHandlingAsync(
            () => throw new DbUpdateConcurrencyException("test conflict"),
            "TestEntity",
            1);

        result.Success.Should().BeFalse(
            "Concurrency conflict should result in failure");
        result.FailureType.Should().Be(ConcurrencyFailureType.ConcurrentModification,
            "A DbUpdateConcurrencyException with no entries should be ConcurrentModification");
    }

    [Fact]
    public async Task Save_ConcurrencyConflict_ErrorMessageSet()
    {
        var service = CreateConcurrencyService();

        var result = await service.SaveWithConcurrencyHandlingAsync(
            () => throw new DbUpdateConcurrencyException("test conflict"),
            "TestEntity",
            1);

        result.ErrorMessage.Should().NotBeNullOrEmpty(
            "Concurrency conflict should include a user-facing error message");
        result.ErrorMessage.Should().Contain("modified",
            "Error message should indicate the record was modified by another user");
    }
}
