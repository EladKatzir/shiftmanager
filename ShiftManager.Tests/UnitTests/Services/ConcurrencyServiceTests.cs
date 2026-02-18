using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Unit tests for ConcurrencyService (B-018)
/// </summary>
public class ConcurrencyServiceTests
{
    private readonly Mock<ILogger<ConcurrencyService>> _loggerMock;
    private readonly ConcurrencyService _sut;

    public ConcurrencyServiceTests()
    {
        _loggerMock = new Mock<ILogger<ConcurrencyService>>();
        _sut = new ConcurrencyService(_loggerMock.Object);
    }

    [Fact]
    public async Task SaveWithConcurrencyHandlingAsync_WhenSaveSucceeds_ReturnsSuccess()
    {
        // Arrange
        var saveAction = new Func<Task<int>>(() => Task.FromResult(1));

        // Act
        var result = await _sut.SaveWithConcurrencyHandlingAsync(saveAction, "ShiftInstance", 123);

        // Assert
        result.Success.Should().BeTrue();
        result.FailureType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task SaveWithConcurrencyHandlingAsync_WhenConcurrencyException_ReturnsConflict()
    {
        // Arrange
        var concurrencyException = new DbUpdateConcurrencyException("A concurrency error occurred");

        var saveAction = new Func<Task<int>>(() => throw concurrencyException);

        // Act
        var result = await _sut.SaveWithConcurrencyHandlingAsync(saveAction, "ShiftInstance", 456);

        // Assert
        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(ConcurrencyFailureType.ConcurrentModification);
        result.ErrorMessage.Should().Contain("modified by another user");
    }

    [Fact]
    public async Task SaveWithConcurrencyHandlingAsync_WhenDbUpdateException_Propagates()
    {
        // Arrange - DbUpdateException (non-concurrency) should propagate to caller
        var dbException = new DbUpdateException("Database error");
        var saveAction = new Func<Task<int>>(() => throw dbException);

        // Act & Assert - exception should not be caught by ConcurrencyService
        var act = () => _sut.SaveWithConcurrencyHandlingAsync(saveAction, "ShiftAssignment", 789);
        await act.Should().ThrowAsync<DbUpdateException>()
            .WithMessage("Database error");
    }

    [Fact]
    public async Task SaveWithConcurrencyHandlingAsync_LogsWarningOnConcurrencyConflict()
    {
        // Arrange
        var concurrencyException = new DbUpdateConcurrencyException("A concurrency error occurred");

        var saveAction = new Func<Task<int>>(() => throw concurrencyException);

        // Act
        await _sut.SaveWithConcurrencyHandlingAsync(saveAction, "ShiftInstance", 123);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Concurrency conflict")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveWithConcurrencyHandlingAsync_DbUpdateException_DoesNotLog()
    {
        // Arrange - DbUpdateException (non-concurrency) propagates without logging
        var dbException = new DbUpdateException("Database error");
        var saveAction = new Func<Task<int>>(() => throw dbException);

        // Act - catch the propagated exception
        try
        {
            await _sut.SaveWithConcurrencyHandlingAsync(saveAction, "ShiftAssignment", 789);
        }
        catch (DbUpdateException)
        {
            // Expected
        }

        // Assert - no error logging occurred (exception was not caught by the service)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
