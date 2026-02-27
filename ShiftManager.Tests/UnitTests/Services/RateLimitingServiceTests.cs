using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for RateLimitingService — verifies sliding window rate limiting,
/// reset behavior, and active entry tracking.
///
/// RL-01: First request within limit is allowed
/// RL-02: Requests exceeding max attempts are blocked
/// RL-03: Reset clears the counter for a key
/// RL-04: GetActiveEntries returns entries at or over limit
/// </summary>
public class RateLimitingServiceTests : IDisposable
{
    private readonly RateLimitingService _service;

    public RateLimitingServiceTests()
    {
        var loggerMock = new Mock<ILogger<RateLimitingService>>();
        _service = new RateLimitingService(loggerMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    /// <summary>
    /// RL-01: First request for a new key is always allowed when below the limit.
    /// </summary>
    [Fact]
    public void RL01_IsAllowed_ReturnsTrue_WhenUnderLimit()
    {
        // Act
        var result = _service.IsAllowed("login:192.168.1.1", maxAttempts: 5, windowMinutes: 15);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// RL-02: Requests exceeding maxAttempts within the window are blocked.
    /// </summary>
    [Fact]
    public void RL02_IsAllowed_ReturnsFalse_WhenLimitExceeded()
    {
        // Arrange — exhaust the limit (5 attempts)
        var key = "login:10.0.0.1";
        for (int i = 0; i < 5; i++)
        {
            _service.IsAllowed(key, maxAttempts: 5, windowMinutes: 15);
        }

        // Act — 6th attempt should be blocked
        var result = _service.IsAllowed(key, maxAttempts: 5, windowMinutes: 15);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// RL-03: Reset clears the rate limit counter, allowing requests again.
    /// </summary>
    [Fact]
    public void RL03_Reset_ClearsCounter()
    {
        // Arrange — exhaust the limit
        var key = "login:172.16.0.1";
        for (int i = 0; i < 5; i++)
        {
            _service.IsAllowed(key, maxAttempts: 5, windowMinutes: 15);
        }

        // Verify blocked
        _service.IsAllowed(key, maxAttempts: 5, windowMinutes: 15).Should().BeFalse();

        // Act — reset the key
        _service.Reset(key);

        // Assert — should be allowed again
        var result = _service.IsAllowed(key, maxAttempts: 5, windowMinutes: 15);
        result.Should().BeTrue();
    }

    /// <summary>
    /// RL-04: GetActiveEntries returns entries that have reached or exceeded the limit.
    /// </summary>
    [Fact]
    public void RL04_GetActiveEntries_ReturnsEntriesAtLimit()
    {
        // Arrange — create two keys with "login:" prefix, only one at limit
        var key1 = "login:192.168.1.10";
        var key2 = "login:192.168.1.20";

        // key1: 5 attempts (at limit)
        for (int i = 0; i < 5; i++)
            _service.IsAllowed(key1, maxAttempts: 5, windowMinutes: 15);

        // key2: 2 attempts (under limit)
        for (int i = 0; i < 2; i++)
            _service.IsAllowed(key2, maxAttempts: 5, windowMinutes: 15);

        // Act
        var entries = _service.GetActiveEntries("login:", maxAttempts: 5, windowMinutes: 15);

        // Assert — only key1 should be returned (at limit)
        entries.Should().HaveCount(1);
        entries[0].Key.Should().Be(key1);
        entries[0].AttemptCount.Should().Be(5);
        entries[0].LastAttempt.Should().NotBeNull();
    }
}
