using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for NotificationService — verifies notification creation,
/// type-specific notifications, and error handling.
///
/// NS-01: CreateNotificationAsync creates a DB record with correct fields
/// NS-02: CreateShiftAddedNotificationAsync creates shift notification
/// NS-03: CreateChoreAssignedNotificationAsync creates chore notification
/// NS-04: CreateNotificationAsync returns false on invalid user (no crash)
/// NS-05: NotifyOwnersOfAccessRequestAsync creates notifications for all owners
/// NS-06: CreateTimeOffNotificationAsync creates time-off notification
/// </summary>
public class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<IMailService> _mailServiceMock;
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly Mock<IStringLocalizer<SharedResources>> _localizerMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILocalizationService> _localizationMock;
    private readonly Mock<ICompanyLocalizationService> _companyLocalizationMock;
    private readonly NotificationService _service;

    private const int TestCompanyId = 1;

    public NotificationServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(TestCompanyId);

        _loggerMock = new Mock<ILogger<NotificationService>>();
        _mailServiceMock = new Mock<IMailService>();
        _mailServiceMock.Setup(x => x.SendMailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(OperationResult.Ok());

        _localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        // Return the key itself as the localized value (common test pattern)
        _localizerMock.Setup(x => x[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));
        _localizerMock.Setup(x => x[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns<string, object[]>((key, args) => new LocalizedString(key, string.Format(key, args)));

        _configMock = new Mock<IConfiguration>();
        _configMock.Setup(x => x["BaseUrl"]).Returns("http://localhost:5000");

        _localizationMock = new Mock<ILocalizationService>();

        _companyLocalizationMock = new Mock<ICompanyLocalizationService>();
        _companyLocalizationMock.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);

        _service = new NotificationService(
            _db,
            _loggerMock.Object,
            _tenantResolverMock.Object,
            _mailServiceMock.Object,
            _localizerMock.Object,
            _configMock.Object,
            _localizationMock.Object,
            _companyLocalizationMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task<AppUser> CreateTestUserAsync(int id = 1, string email = "test@test.com")
    {
        var user = new AppUser
        {
            Id = id,
            Email = email,
            CompanyId = TestCompanyId,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// NS-01: CreateNotificationAsync inserts a UserNotification record with correct fields.
    /// </summary>
    [Fact]
    public async Task NS01_CreateNotificationAsync_CreatesDbRecord()
    {
        // Arrange
        await CreateTestUserAsync();

        // Act
        var result = await _service.CreateNotificationAsync(
            userId: 1,
            type: NotificationType.ShiftAdded,
            title: "New Shift",
            message: "You have been assigned to Morning shift",
            relatedEntityId: 42,
            relatedEntityType: "ShiftAssignment");

        // Assert
        result.Should().BeTrue();

        var notification = await _db.UserNotifications.FirstOrDefaultAsync();
        notification.Should().NotBeNull();
        notification!.UserId.Should().Be(1);
        notification.CompanyId.Should().Be(TestCompanyId);
        notification.Type.Should().Be(NotificationType.ShiftAdded);
        notification.Title.Should().Be("New Shift");
        notification.Message.Should().Contain("Morning shift");
        notification.IsRead.Should().BeFalse();
        notification.RelatedEntityId.Should().Be(42);
        notification.RelatedEntityType.Should().Be("ShiftAssignment");
    }

    /// <summary>
    /// NS-02: CreateShiftAddedNotificationAsync creates a shift-type notification in the DB.
    /// </summary>
    [Fact]
    public async Task NS02_CreateShiftAddedNotification_CreatesRecord()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        await _service.CreateShiftAddedNotificationAsync(
            userId: user.Id,
            shiftTypeName: "Morning",
            shiftDate: new DateOnly(2026, 3, 1),
            startTime: new TimeOnly(7, 0),
            endTime: new TimeOnly(15, 0));

        // Assert
        var notifications = await _db.UserNotifications.ToListAsync();
        notifications.Should().HaveCount(1);
        notifications[0].Type.Should().Be(NotificationType.ShiftAdded);
        notifications[0].UserId.Should().Be(user.Id);
    }

    /// <summary>
    /// NS-03: CreateChoreAssignedNotificationAsync creates a chore-type notification.
    /// </summary>
    [Fact]
    public async Task NS03_CreateChoreAssignedNotification_CreatesRecord()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        await _service.CreateChoreAssignedNotificationAsync(
            userId: user.Id,
            choreTitle: "Kitchen Cleanup",
            choreDate: new DateOnly(2026, 3, 5),
            choreId: 100);

        // Assert
        var notifications = await _db.UserNotifications.ToListAsync();
        notifications.Should().HaveCount(1);
        notifications[0].Type.Should().Be(NotificationType.ChoreAssigned);
        notifications[0].UserId.Should().Be(user.Id);
    }

    /// <summary>
    /// NS-04: CreateNotificationAsync returns false (not throws) on DB error.
    /// The service catches DbUpdateException and returns false gracefully.
    /// </summary>
    [Fact]
    public async Task NS04_CreateNotificationAsync_ReturnsFalse_OnError()
    {
        // Arrange — don't create any user; the notification will still be saved
        // (UserNotification doesn't enforce FK in InMemory). Instead, test with
        // a disposed context to trigger an error. Uses a LOCAL connection (not the
        // fixture's readonly _sqliteConnection field) so this test can immediately
        // dispose the DbContext without affecting other tests in the class.
        using var localConn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        localConn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(localConn)
            .Options;
        var disposedDb = new AppDbContext(options);
        disposedDb.Dispose();

        var service = new NotificationService(
            disposedDb,
            _loggerMock.Object,
            _tenantResolverMock.Object,
            _mailServiceMock.Object,
            _localizerMock.Object,
            _configMock.Object,
            _localizationMock.Object,
            _companyLocalizationMock.Object);

        // Act
        var result = await service.CreateNotificationAsync(
            userId: 999,
            type: NotificationType.ShiftAdded,
            title: "Test",
            message: "Test");

        // Assert — should return false, not throw
        result.Should().BeFalse();
    }

    /// <summary>
    /// NS-05: NotifyOwnersOfAccessRequestAsync creates a notification for each Owner user in the company.
    /// </summary>
    [Fact]
    public async Task NS05_NotifyOwnersOfAccessRequest_CreatesNotificationsForAllOwners()
    {
        // Arrange — create 2 owners and 1 employee in company
        _db.Users.AddRange(
            new AppUser { Id = 10, Email = "owner1@test.com", CompanyId = TestCompanyId, Role = UserRole.Owner, DisplayName = "Owner One", IsActive = true },
            new AppUser { Id = 11, Email = "owner2@test.com", CompanyId = TestCompanyId, Role = UserRole.Owner, DisplayName = "Owner Two", IsActive = true },
            new AppUser { Id = 12, Email = "emp@test.com", CompanyId = TestCompanyId, Role = UserRole.Employee, DisplayName = "Employee", IsActive = true }
        );
        await _db.SaveChangesAsync();

        // Act
        await _service.NotifyOwnersOfAccessRequestAsync(
            requesterName: "New User",
            requesterEmail: "newuser@test.com",
            companyName: "Test Company",
            requestId: 1,
            companyId: TestCompanyId);

        // Assert — should create notifications for both owners, not the employee
        var notifications = await _db.UserNotifications.ToListAsync();
        notifications.Should().HaveCount(2);
        notifications.Select(n => n.UserId).Should().BeEquivalentTo(new[] { 10, 11 });
        notifications.Should().AllSatisfy(n => n.Type.Should().Be(NotificationType.AccessRequestSubmitted));
    }

    /// <summary>
    /// NS-06: CreateTimeOffNotificationAsync creates a time-off notification with correct type.
    /// </summary>
    [Fact]
    public async Task NS06_CreateTimeOffNotification_CreatesRecord()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        await _service.CreateTimeOffNotificationAsync(
            userId: user.Id,
            status: RequestStatus.Approved,
            startDate: new DateOnly(2026, 4, 1),
            endDate: new DateOnly(2026, 4, 5),
            requestId: 50);

        // Assert
        var notifications = await _db.UserNotifications.ToListAsync();
        notifications.Should().HaveCount(1);
        notifications[0].Type.Should().Be(NotificationType.TimeOffApproved);
        notifications[0].UserId.Should().Be(user.Id);
        notifications[0].RelatedEntityId.Should().Be(50);
    }
}
