using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for DutyRotationService — verifies rotation CRUD, queue management,
/// and assignment logic including vacation-skip behavior.
///
/// DR-01: CreateRotationAsync creates and returns a rotation
/// DR-02: AddUserToQueueAsync prevents duplicate queue entries
/// DR-03: AssignNextAsync skips users who are on vacation
/// DR-04: DeleteRotationAsync performs soft-delete (sets IsActive=false)
/// </summary>
public class DutyRotationServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<IFeatureFlagService> _featureFlagMock;
    private readonly DutyRotationService _service;

    private const int TestCompanyId = 1;

    public DutyRotationServiceTests()
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

        _featureFlagMock = new Mock<IFeatureFlagService>();
        _featureFlagMock.Setup(x => x.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(false);

        var loggerMock = new Mock<ILogger<DutyRotationService>>();

        _service = new DutyRotationService(
            _db,
            _tenantResolverMock.Object,
            loggerMock.Object,
            _featureFlagMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task<AppUser> CreateTestUserAsync(int id, string email, bool isActive = true)
    {
        var user = new AppUser
        {
            Id = id,
            Email = email,
            CompanyId = TestCompanyId,
            Role = UserRole.Employee,
            DisplayName = $"User {id}",
            IsActive = isActive
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// DR-01: CreateRotationAsync inserts a DutyRotation with correct fields
    /// and returns it with a valid Id.
    /// </summary>
    [Fact]
    public async Task DR01_CreateRotationAsync_CreatesAndReturnsRotation()
    {
        // Act
        var result = await _service.CreateRotationAsync(
            name: "Daily Hakam Rotation",
            dutyType: OnDutyType.Hakam,
            frequency: RotationFrequency.Daily,
            includeWeekends: false,
            maxConsecutive: 2,
            createdBy: 1);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.Name.Should().Be("Daily Hakam Rotation");
        result.DutyType.Should().Be(OnDutyType.Hakam);
        result.Frequency.Should().Be(RotationFrequency.Daily);
        result.IncludeWeekends.Should().BeFalse();
        result.MaxConsecutiveDays.Should().Be(2);
        result.CompanyId.Should().Be(TestCompanyId);
        result.IsActive.Should().BeTrue();

        // Verify persisted in DB
        var fromDb = await _db.DutyRotations.FindAsync(result.Id);
        fromDb.Should().NotBeNull();
    }

    /// <summary>
    /// DR-02: AddUserToQueueAsync returns false when the same user is already in the queue.
    /// </summary>
    [Fact]
    public async Task DR02_AddUserToQueueAsync_PreventsDuplicates()
    {
        // Arrange
        var user = await CreateTestUserAsync(1, "user1@test.com");
        var rotation = await _service.CreateRotationAsync(
            "Test Rotation", OnDutyType.Lead, RotationFrequency.Daily, false, 1, 1);

        // First add — should succeed
        var firstAdd = await _service.AddUserToQueueAsync(rotation.Id, user.Id);
        firstAdd.Should().BeTrue();

        // Act — second add of same user should fail
        var secondAdd = await _service.AddUserToQueueAsync(rotation.Id, user.Id);

        // Assert
        secondAdd.Should().BeFalse();

        // Verify only one entry exists
        var queue = await _service.GetQueueAsync(rotation.Id);
        queue.Should().HaveCount(1);
    }

    /// <summary>
    /// DR-03: AssignNextAsync skips a user who has an approved vacation on the target date.
    /// </summary>
    [Fact]
    public async Task DR03_AssignNextAsync_SkipsUsersOnVacation()
    {
        // Arrange — create 2 users
        var user1 = await CreateTestUserAsync(10, "user1@test.com");
        var user2 = await CreateTestUserAsync(11, "user2@test.com");

        var rotation = await _service.CreateRotationAsync(
            "Vacation Test", OnDutyType.Hakam, RotationFrequency.Daily, true, 1, 1);

        await _service.AddUserToQueueAsync(rotation.Id, user1.Id);
        await _service.AddUserToQueueAsync(rotation.Id, user2.Id);

        // Give user1 an approved vacation on the target date
        var targetDate = new DateOnly(2026, 7, 1);
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = TestCompanyId,
            UserId = user1.Id,
            StartDate = targetDate,
            EndDate = targetDate.AddDays(2),
            Status = RequestStatus.Approved
        });
        await _db.SaveChangesAsync();

        // Act — assign next should skip user1 (on vacation) and pick user2
        var (success, message, onDuty) = await _service.AssignNextAsync(rotation.Id, targetDate, assignedBy: 1);

        // Assert
        success.Should().BeTrue();
        onDuty.Should().NotBeNull();
        onDuty!.UserId.Should().Be(user2.Id, "user1 is on vacation, so user2 should be assigned");
    }

    /// <summary>
    /// DR-04: DeleteRotationAsync soft-deletes (sets IsActive=false), doesn't remove from DB.
    /// </summary>
    [Fact]
    public async Task DR04_DeleteRotationAsync_SoftDeletes()
    {
        // Arrange
        var rotation = await _service.CreateRotationAsync(
            "To Delete", OnDutyType.Lead, RotationFrequency.Weekly, false, 1, 1);
        rotation.IsActive.Should().BeTrue();

        // Act
        var result = await _service.DeleteRotationAsync(rotation.Id);

        // Assert
        result.Should().BeTrue();

        // Verify soft-deleted (still in DB but IsActive=false)
        var fromDb = await _db.DutyRotations.FindAsync(rotation.Id);
        fromDb.Should().NotBeNull();
        fromDb!.IsActive.Should().BeFalse();
        fromDb.UpdatedAt.Should().NotBeNull();
    }
}
