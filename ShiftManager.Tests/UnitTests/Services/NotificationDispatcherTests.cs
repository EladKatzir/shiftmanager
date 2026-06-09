using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Services;
using ShiftManager.Services.Notifications;
using ShiftManager.Services.Notifications.Events;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// ND-01: ShiftAssignedEvent delegates to CreateShiftAddedNotificationAsync with exact args.
/// ND-02: ShiftRemovedEvent delegates to CreateShiftRemovedNotificationAsync with exact args.
/// ND-03: An unmapped event type does NOT throw and calls no INotificationService method.
/// </summary>
public class NotificationDispatcherTests
{
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ILogger<NotificationDispatcher>> _logger = new();

    private NotificationDispatcher CreateSut() => new(_notifications.Object, _logger.Object);

    [Fact] // ND-01
    public async Task RaiseAsync_ShiftAssignedEvent_DelegatesToCreateShiftAdded()
    {
        var sut = CreateSut();
        var evt = new ShiftAssignedEvent
        {
            RecipientUserId = 42,
            ShiftTypeName = "Morning",
            ShiftDate = new DateOnly(2026, 6, 11),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0)
        };

        await sut.RaiseAsync(evt);

        _notifications.Verify(n => n.CreateShiftAddedNotificationAsync(
            42, "Morning", new DateOnly(2026, 6, 11), new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Times.Once);
    }

    [Fact] // ND-02
    public async Task RaiseAsync_ShiftRemovedEvent_DelegatesToCreateShiftRemoved()
    {
        var sut = CreateSut();
        var evt = new ShiftRemovedEvent
        {
            RecipientUserId = 7,
            ShiftTypeName = "Night",
            ShiftDate = new DateOnly(2026, 6, 12),
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0)
        };

        await sut.RaiseAsync(evt);

        _notifications.Verify(n => n.CreateShiftRemovedNotificationAsync(
            7, "Night", new DateOnly(2026, 6, 12), new TimeOnly(22, 0), new TimeOnly(6, 0)),
            Times.Once);
    }

    [Fact] // ND-03
    public async Task RaiseAsync_UnmappedEvent_DoesNotThrow_AndCallsNothing()
    {
        var sut = CreateSut();
        var act = async () => await sut.RaiseAsync(new UnmappedTestEvent());

        await act.Should().NotThrowAsync();
        _notifications.VerifyNoOtherCalls();
    }

    private sealed record UnmappedTestEvent : NotificationEvent
    {
        public override NotificationCategory Category => NotificationCategory.System;
        public override bool PersonallyActionable => false;
    }
}
