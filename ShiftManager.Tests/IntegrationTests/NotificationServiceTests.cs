using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// Comprehensive integration tests for NotificationService.
/// Verifies that every notification method correctly creates and persists
/// UserNotification records with the expected Type, UserId, Title, Message,
/// and metadata fields.
///
/// Uses in-memory SQLite + Moq for IMailService, ILocalizationService, etc.
/// </summary>
public class NotificationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolver;
    private readonly Mock<IMailService> _mailService;
    private readonly Mock<IStringLocalizer<SharedResources>> _localizer;
    private readonly Mock<IConfiguration> _configuration;
    private readonly Mock<ILocalizationService> _localization;
    private readonly Mock<ILogger<NotificationService>> _logger;
    private readonly NotificationService _sut;

    // Seed data references
    private Company _company = null!;
    private AppUser _employee = null!;
    private AppUser _owner = null!;
    private AppUser _trainee = null!;

    private const int TestCompanyId = 1;

    public NotificationServiceTests()
    {
        // Use in-memory database (same pattern as SignupFlowTests)
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("NotificationServiceTests_" + Guid.NewGuid())
            .Options;

        // Create tenant resolver mock BEFORE constructing db context
        _tenantResolver = new Mock<ITenantResolver>();
        _tenantResolver.Setup(t => t.GetCurrentTenantId()).Returns(TestCompanyId);
        _tenantResolver.Setup(t => t.HasTenant()).Returns(true);

        _db = new AppDbContext(options, _tenantResolver.Object);

        // Mock IMailService — all methods return true (we're testing notifications, not email)
        _mailService = new Mock<IMailService>();
        _mailService.Setup(m => m.SendShiftAssignedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendShiftDeletedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendTimeOffApprovedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendTimeOffDeclinedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendTimeOffDeletedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendSwapRequestApprovedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendSwapRequestDeclinedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendChoreAssignedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendChoreCanceledEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendOnDutyAssignedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendOnDutyCanceledEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendAccessRequestSubmittedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendTraineeAddedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendSlotRemovedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Ok());
        _mailService.Setup(m => m.SendShiftModifiedEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Ok());

        // Localizer returns key as value (same pattern as SignupFlowTests)
        _localizer = new Mock<IStringLocalizer<SharedResources>>();
        _localizer.Setup(l => l[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));
        _localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns<string, object[]>((key, args) =>
            {
                // Use string.Format so {0}, {1}, etc. get replaced with actual values
                try
                {
                    return new LocalizedString(key, string.Format(key, args));
                }
                catch
                {
                    return new LocalizedString(key, key);
                }
            });

        // ILocalizationService — return simple formatted strings
        _localization = new Mock<ILocalizationService>();
        _localization.Setup(l => l.FormatMediumDate(It.IsAny<DateOnly>()))
            .Returns<DateOnly>(d => d.ToString("yyyy-MM-dd"));
        _localization.Setup(l => l.FormatTime(It.IsAny<TimeOnly>()))
            .Returns<TimeOnly>(t => t.ToString("HH:mm"));
        _localization.Setup(l => l.FormatDateTime(It.IsAny<DateTime>()))
            .Returns<DateTime>(dt => dt.ToString("yyyy-MM-dd HH:mm"));

        // IConfiguration — minimal setup
        _configuration = new Mock<IConfiguration>();
        _configuration.Setup(c => c[It.IsAny<string>()]).Returns("http://localhost:5000");

        _logger = new Mock<ILogger<NotificationService>>();

        // Seed data
        SeedData().Wait();

        // Create the service under test
        var companyLocalizationMock = new Mock<ICompanyLocalizationService>();
        companyLocalizationMock.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);
        _sut = new NotificationService(
            _db, _logger.Object, _tenantResolver.Object,
            _mailService.Object, _localizer.Object,
            _configuration.Object, _localization.Object,
            companyLocalizationMock.Object);
    }

    private async Task SeedData()
    {
        // Company
        _company = new Company
        {
            Id = TestCompanyId,
            Name = "TestCo",
            DisplayName = "Test Company",
            MoleculeId = 1
        };
        _db.Companies.Add(_company);
        await _db.SaveChangesAsync();

        // Employee user
        _employee = new AppUser
        {
            Email = "employee@test.com",
            DisplayName = "Test Employee",
            CompanyId = TestCompanyId,
            Role = UserRole.Employee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(_employee);

        // Owner user (for access request notifications)
        _owner = new AppUser
        {
            Email = "owner@test.com",
            DisplayName = "Test Owner",
            CompanyId = TestCompanyId,
            Role = UserRole.Owner,
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(_owner);

        // Trainee user (for trainee notification tests)
        _trainee = new AppUser
        {
            Email = "trainee@test.com",
            DisplayName = "Test Trainee",
            CompanyId = TestCompanyId,
            Role = UserRole.Trainee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(_trainee);

        await _db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────
    // Helper: Assert common notification properties
    // ──────────────────────────────────────────────────────────

    private static void AssertNotificationBase(UserNotification notification, int expectedUserId, NotificationType expectedType)
    {
        notification.Should().NotBeNull();
        notification.UserId.Should().Be(expectedUserId);
        notification.Type.Should().Be(expectedType);
        notification.Title.Should().NotBeNullOrWhiteSpace("notification title must be set");
        notification.Message.Should().NotBeNullOrWhiteSpace("notification message must be set");
        notification.IsRead.Should().BeFalse("new notification should be unread");
        notification.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30),
            "notification should have a recent CreatedAt timestamp");
    }

    // ──────────────────────────────────────────────────────────
    // 1. CreateNotificationAsync (generic)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateNotificationAsync_PersistsNotification_WithCorrectFields()
    {
        var result = await _sut.CreateNotificationAsync(
            _employee.Id,
            NotificationType.ShiftAdded,
            "Test Title",
            "Test Message",
            relatedEntityId: 42,
            relatedEntityType: "TestEntity");

        result.Should().BeTrue("CreateNotificationAsync should return true on success");

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.ShiftAdded);

        notification.Should().NotBeNull();
        notification!.CompanyId.Should().Be(TestCompanyId);
        notification.Title.Should().Be("Test Title");
        notification.Message.Should().Be("Test Message");
        notification.IsRead.Should().BeFalse();
        notification.RelatedEntityId.Should().Be(42);
        notification.RelatedEntityType.Should().Be("TestEntity");
        notification.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    // ──────────────────────────────────────────────────────────
    // 2. CreateShiftAddedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShiftAddedNotificationAsync_PersistsCorrectNotification()
    {
        var date = new DateOnly(2026, 4, 1);
        var start = new TimeOnly(8, 0);
        var end = new TimeOnly(16, 0);

        await _sut.CreateShiftAddedNotificationAsync(_employee.Id, "Morning", date, start, end);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.ShiftAdded);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.ShiftAdded);
        notification!.RelatedEntityType.Should().Be("ShiftAssignment");

        // Verify email was also attempted
        _mailService.Verify(m => m.SendShiftAssignedEmailAsync(
            _employee.Email, _employee.DisplayName, "Morning", date, start, end), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 3. CreateShiftRemovedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShiftRemovedNotificationAsync_PersistsCorrectNotification()
    {
        var date = new DateOnly(2026, 4, 2);
        var start = new TimeOnly(16, 0);
        var end = new TimeOnly(0, 0);

        await _sut.CreateShiftRemovedNotificationAsync(_employee.Id, "Evening", date, start, end);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.ShiftRemoved);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.ShiftRemoved);
        notification!.RelatedEntityType.Should().Be("ShiftAssignment");

        _mailService.Verify(m => m.SendShiftDeletedEmailAsync(
            _employee.Email, _employee.DisplayName, "Evening", date, start, end), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 4. CreateTimeOffNotificationAsync — Approved
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTimeOffNotificationAsync_Approved_PersistsCorrectNotification()
    {
        var startDate = new DateOnly(2026, 5, 1);
        var endDate = new DateOnly(2026, 5, 3);

        await _sut.CreateTimeOffNotificationAsync(_employee.Id, RequestStatus.Approved, startDate, endDate, requestId: 100);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.TimeOffApproved);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.TimeOffApproved);
        notification!.RelatedEntityId.Should().Be(100);
        notification.RelatedEntityType.Should().Be("TimeOffRequest");

        _mailService.Verify(m => m.SendTimeOffApprovedEmailAsync(
            _employee.Email, _employee.DisplayName, startDate, endDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 5. CreateTimeOffNotificationAsync — Declined
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTimeOffNotificationAsync_Declined_PersistsCorrectNotification()
    {
        var startDate = new DateOnly(2026, 6, 10);
        var endDate = new DateOnly(2026, 6, 10); // single day

        await _sut.CreateTimeOffNotificationAsync(_employee.Id, RequestStatus.Declined, startDate, endDate, requestId: 101);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.TimeOffDeclined);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.TimeOffDeclined);
        notification!.RelatedEntityId.Should().Be(101);
        notification.RelatedEntityType.Should().Be("TimeOffRequest");

        _mailService.Verify(m => m.SendTimeOffDeclinedEmailAsync(
            _employee.Email, _employee.DisplayName, startDate, endDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 6. CreateSwapRequestNotificationAsync — Approved
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSwapRequestNotificationAsync_Approved_PersistsCorrectNotification()
    {
        await _sut.CreateSwapRequestNotificationAsync(
            _employee.Id, RequestStatus.Approved, "Morning 2026-04-01", requestId: 200);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.SwapRequestApproved);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.SwapRequestApproved);
        notification!.RelatedEntityId.Should().Be(200);
        notification.RelatedEntityType.Should().Be("SwapRequest");

        _mailService.Verify(m => m.SendSwapRequestApprovedEmailAsync(
            _employee.Email, _employee.DisplayName, "Morning 2026-04-01"), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 7. CreateSwapRequestNotificationAsync — Declined
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSwapRequestNotificationAsync_Declined_PersistsCorrectNotification()
    {
        await _sut.CreateSwapRequestNotificationAsync(
            _employee.Id, RequestStatus.Declined, "Evening 2026-04-05", requestId: 201);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.SwapRequestDeclined);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.SwapRequestDeclined);
        notification!.RelatedEntityId.Should().Be(201);
        notification.RelatedEntityType.Should().Be("SwapRequest");

        _mailService.Verify(m => m.SendSwapRequestDeclinedEmailAsync(
            _employee.Email, _employee.DisplayName, "Evening 2026-04-05"), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 8. CreateChoreAssignedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateChoreAssignedNotificationAsync_PersistsCorrectNotification()
    {
        var choreDate = new DateOnly(2026, 4, 10);

        await _sut.CreateChoreAssignedNotificationAsync(_employee.Id, "Kitchen Cleanup", choreDate, choreId: 300);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.ChoreAssigned);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.ChoreAssigned);
        notification!.RelatedEntityId.Should().Be(300);
        notification.RelatedEntityType.Should().Be("Chore");

        _mailService.Verify(m => m.SendChoreAssignedEmailAsync(
            _employee.Email, _employee.DisplayName, "Kitchen Cleanup", choreDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 9. CreateChoreCanceledNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateChoreCanceledNotificationAsync_PersistsCorrectNotification()
    {
        var choreDate = new DateOnly(2026, 4, 11);

        await _sut.CreateChoreCanceledNotificationAsync(_employee.Id, "Floor Mopping", choreDate, choreId: 301);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.ChoreCanceled);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.ChoreCanceled);
        notification!.RelatedEntityId.Should().Be(301);
        notification.RelatedEntityType.Should().Be("Chore");

        _mailService.Verify(m => m.SendChoreCanceledEmailAsync(
            _employee.Email, _employee.DisplayName, "Floor Mopping", choreDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 10. CreateOnDutyAssignedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOnDutyAssignedNotificationAsync_PersistsCorrectNotification()
    {
        var onDutyDate = new DateOnly(2026, 4, 15);

        await _sut.CreateOnDutyAssignedNotificationAsync(_employee.Id, OnDutyType.Hakam, onDutyDate, onDutyId: 400);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.OnDutyAssigned);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.OnDutyAssigned);
        notification!.RelatedEntityId.Should().Be(400);
        notification.RelatedEntityType.Should().Be("OnDuty");

        _mailService.Verify(m => m.SendOnDutyAssignedEmailAsync(
            _employee.Email, _employee.DisplayName, It.IsAny<string>(), onDutyDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 11. CreateOnDutyCanceledNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOnDutyCanceledNotificationAsync_PersistsCorrectNotification()
    {
        var onDutyDate = new DateOnly(2026, 4, 16);

        await _sut.CreateOnDutyCanceledNotificationAsync(_employee.Id, OnDutyType.Lead, onDutyDate, onDutyId: 401);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.OnDutyCanceled);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.OnDutyCanceled);
        notification!.RelatedEntityId.Should().Be(401);
        notification.RelatedEntityType.Should().Be("OnDuty");

        _mailService.Verify(m => m.SendOnDutyCanceledEmailAsync(
            _employee.Email, _employee.DisplayName, It.IsAny<string>(), onDutyDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 12. CreateTimeOffDeletedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTimeOffDeletedNotificationAsync_PersistsCorrectNotification()
    {
        var startDate = new DateOnly(2026, 7, 1);
        var endDate = new DateOnly(2026, 7, 5);

        await _sut.CreateTimeOffDeletedNotificationAsync(_employee.Id, startDate, endDate);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.TimeOffDeleted);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.TimeOffDeleted);
        notification!.RelatedEntityType.Should().Be("TimeOffRequest");

        _mailService.Verify(m => m.SendTimeOffDeletedEmailAsync(
            _employee.Email, _employee.DisplayName, startDate, endDate), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 13. NotifyOwnersOfAccessRequestAsync (CRITICAL — Fix 34)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyOwnersOfAccessRequestAsync_NotifiesOwnerInTargetCompany()
    {
        // This tests the exact code path that Fix 34 modified.
        // The method uses IgnoreQueryFilters() to find owners scoped by companyId.
        await _sut.NotifyOwnersOfAccessRequestAsync(
            "New User", "newuser@test.com", "Test Company", requestId: 500, companyId: TestCompanyId);

        // Owner should have received the notification
        var notification = await _db.UserNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.UserId == _owner.Id && n.Type == NotificationType.AccessRequestSubmitted);

        AssertNotificationBase(notification!, _owner.Id, NotificationType.AccessRequestSubmitted);
        notification!.RelatedEntityId.Should().Be(500);
        notification.RelatedEntityType.Should().Be("UserJoinRequest");
        // Note: message content depends on actual .resx values; with mock localizer the key
        // is returned as the format template, so {0}/{1}/{2} may not resolve. We verify the
        // notification was persisted with non-empty title and message via AssertNotificationBase.

        // Email should also have been sent to the owner
        _mailService.Verify(m => m.SendAccessRequestSubmittedEmailAsync(
            _owner.Email, _owner.DisplayName,
            "New User", "newuser@test.com", "Test Company"), Times.Once);
    }

    [Fact]
    public async Task NotifyOwnersOfAccessRequestAsync_DoesNotNotifyNonOwners()
    {
        await _sut.NotifyOwnersOfAccessRequestAsync(
            "Another User", "another@test.com", "Test Company", requestId: 501, companyId: TestCompanyId);

        // Employee should NOT have received the notification
        var employeeNotification = await _db.UserNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.AccessRequestSubmitted);

        employeeNotification.Should().BeNull("non-owner users should not receive access request notifications");
    }

    [Fact]
    public async Task NotifyOwnersOfAccessRequestAsync_NoOwnersInCompany_DoesNotThrow()
    {
        // Use a company ID that has no owners
        var emptyCompanyId = 9999;

        // Should not throw, just log a warning
        await _sut.Invoking(s => s.NotifyOwnersOfAccessRequestAsync(
            "Orphan User", "orphan@test.com", "Empty Company", requestId: 502, companyId: emptyCompanyId))
            .Should().NotThrowAsync();

        // No notifications should have been created for this request
        var notifications = await _db.UserNotifications
            .IgnoreQueryFilters()
            .Where(n => n.RelatedEntityId == 502)
            .ToListAsync();

        notifications.Should().BeEmpty("no owners exist in the target company");
    }

    // ──────────────────────────────────────────────────────────
    // 14. CreateAccessRequestApprovedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccessRequestApprovedNotificationAsync_PersistsCorrectNotification()
    {
        await _sut.CreateAccessRequestApprovedNotificationAsync(_employee.Id, "Test Company", "Employee");

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.AccessRequestApproved);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.AccessRequestApproved);
        notification!.RelatedEntityType.Should().Be("User");
    }

    // ──────────────────────────────────────────────────────────
    // 15. CreateTraineeAddedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTraineeAddedNotificationAsync_PersistsCorrectNotification()
    {
        var date = new DateOnly(2026, 4, 20);
        var start = new TimeOnly(8, 0);
        var end = new TimeOnly(16, 0);

        await _sut.CreateTraineeAddedNotificationAsync(
            _employee.Id, _trainee.Id, "Test Trainee", "Morning", date, start, end);

        // Primary user gets the EmployeeTraineeAdded notification
        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.EmployeeTraineeAdded);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.EmployeeTraineeAdded);
        notification!.RelatedEntityType.Should().Be("ShiftAssignment");

        // Email was sent to primary user
        _mailService.Verify(m => m.SendTraineeAddedEmailAsync(
            _employee.Email, _employee.DisplayName,
            "Test Trainee", "Morning", date, start, end), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 16. CreateTraineeChangedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTraineeChangedNotificationAsync_PersistsNotificationsForAllParties()
    {
        // Create a second trainee for the "new trainee" role
        var newTrainee = new AppUser
        {
            Email = "newtainee@test.com",
            DisplayName = "New Trainee",
            CompanyId = TestCompanyId,
            Role = UserRole.Trainee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(newTrainee);
        await _db.SaveChangesAsync();

        var date = new DateOnly(2026, 4, 21);

        await _sut.CreateTraineeChangedNotificationAsync(
            _employee.Id, _trainee.Id, newTrainee.Id,
            "Test Trainee", "New Trainee", "Morning", date);

        // Primary user gets EmployeeTraineeAdded (change) notification
        var primaryNotification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.EmployeeTraineeAdded);

        AssertNotificationBase(primaryNotification!, _employee.Id, NotificationType.EmployeeTraineeAdded);

        // Old trainee gets TraineeShadowingRemoved notification
        var oldTraineeNotification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _trainee.Id && n.Type == NotificationType.TraineeShadowingRemoved);

        AssertNotificationBase(oldTraineeNotification!, _trainee.Id, NotificationType.TraineeShadowingRemoved);

        // New trainee gets TraineeShadowingAdded notification
        var newTraineeNotification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == newTrainee.Id && n.Type == NotificationType.TraineeShadowingAdded);

        AssertNotificationBase(newTraineeNotification!, newTrainee.Id, NotificationType.TraineeShadowingAdded);
    }

    // ──────────────────────────────────────────────────────────
    // 17. CreateTraineeRemovedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTraineeRemovedNotificationAsync_PersistsNotificationsForBothParties()
    {
        var date = new DateOnly(2026, 4, 22);

        await _sut.CreateTraineeRemovedNotificationAsync(
            _employee.Id, _trainee.Id, "Test Trainee", "Afternoon", date);

        // Primary user gets EmployeeTraineeRemoved notification
        var primaryNotification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.EmployeeTraineeRemoved);

        AssertNotificationBase(primaryNotification!, _employee.Id, NotificationType.EmployeeTraineeRemoved);
        primaryNotification!.RelatedEntityType.Should().Be("ShiftAssignment");

        // Trainee gets TraineeShadowingRemoved notification
        var traineeNotification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _trainee.Id && n.Type == NotificationType.TraineeShadowingRemoved);

        AssertNotificationBase(traineeNotification!, _trainee.Id, NotificationType.TraineeShadowingRemoved);
    }

    // ──────────────────────────────────────────────────────────
    // 18. CreateSlotRemovedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSlotRemovedNotificationAsync_PersistsCorrectNotification()
    {
        var date = new DateOnly(2026, 4, 25);
        var start = new TimeOnly(8, 0);
        var end = new TimeOnly(16, 0);

        await _sut.CreateSlotRemovedNotificationAsync(
            _employee.Id, "Morning", date, start, end, "Staffing reduced");

        // SlotRemoved uses NotificationType.ShiftRemoved
        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.ShiftRemoved);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.ShiftRemoved);
        notification!.RelatedEntityType.Should().Be("ShiftAssignment");

        _mailService.Verify(m => m.SendSlotRemovedEmailAsync(
            _employee.Email, _employee.DisplayName,
            "Morning", date, start, end, "Staffing reduced"), Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // 19. CreateShiftModifiedNotificationAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShiftModifiedNotificationAsync_PersistsNotificationForEachUser()
    {
        var date = new DateOnly(2026, 4, 28);
        var userIds = new List<int> { _employee.Id, _owner.Id };

        await _sut.CreateShiftModifiedNotificationAsync(
            userIds, "Night", date, "Time changed to 22:00-06:00");

        // Both users should receive notifications (ShiftModified uses NotificationType.ShiftAdded)
        foreach (var userId in userIds)
        {
            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.UserId == userId && n.Type == NotificationType.ShiftAdded
                    && n.RelatedEntityType == "ShiftAssignment"
                    && n.Title == "NotificationShiftModifiedTitle");

            AssertNotificationBase(notification!, userId, NotificationType.ShiftAdded);
            notification!.RelatedEntityType.Should().Be("ShiftAssignment");
        }

        // Email should have been sent to both users
        _mailService.Verify(m => m.SendShiftModifiedEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            "Night", date, "Time changed to 22:00-06:00"), Times.Exactly(2));
    }

    // ──────────────────────────────────────────────────────────
    // Edge case: notification with empty user list
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShiftModifiedNotificationAsync_EmptyUserList_CreatesNoNotifications()
    {
        var date = new DateOnly(2026, 5, 1);
        var initialCount = await _db.UserNotifications.CountAsync();

        await _sut.CreateShiftModifiedNotificationAsync(
            new List<int>(), "Morning", date, "No one affected");

        var finalCount = await _db.UserNotifications.CountAsync();
        finalCount.Should().Be(initialCount, "no notifications should be created for an empty user list");
    }

    // ──────────────────────────────────────────────────────────
    // Edge case: on-duty with Lead type
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOnDutyAssignedNotificationAsync_Lead_PersistsCorrectNotification()
    {
        var onDutyDate = new DateOnly(2026, 4, 17);

        await _sut.CreateOnDutyAssignedNotificationAsync(_employee.Id, OnDutyType.Lead, onDutyDate, onDutyId: 402);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.OnDutyAssigned
                && n.RelatedEntityId == 402);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.OnDutyAssigned);
        notification!.RelatedEntityType.Should().Be("OnDuty");
    }

    // ──────────────────────────────────────────────────────────
    // Edge case: time-off single day range
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTimeOffDeletedNotificationAsync_SingleDay_PersistsCorrectNotification()
    {
        var singleDay = new DateOnly(2026, 8, 15);

        await _sut.CreateTimeOffDeletedNotificationAsync(_employee.Id, singleDay, singleDay);

        var notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.UserId == _employee.Id && n.Type == NotificationType.TimeOffDeleted);

        AssertNotificationBase(notification!, _employee.Id, NotificationType.TimeOffDeleted);
        notification!.RelatedEntityType.Should().Be("TimeOffRequest");
    }

    public void Dispose() => _db?.Dispose();
}
