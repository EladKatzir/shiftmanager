using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for TeamCalendarService — verifies calendar CRUD, member management,
/// ownership validation, and duplicate prevention.
///
/// TC-01: CreateCalendarAsync creates a calendar with correct fields
/// TC-02: CreateCalendarAsync rejects duplicate name for same owner
/// TC-03: CreateCalendarAsync rejects name over 60 chars
/// TC-04: DeleteCalendarAsync soft-deletes (sets IsDeleted=true)
/// TC-05: AddMembersAsync adds members and prevents duplicates
/// TC-06: RemoveMembersAsync removes specified members
/// TC-07: SetMembersAsync replaces all members atomically
/// TC-08: GetCalendarByIdAsync returns null for wrong owner
/// </summary>
public class TeamCalendarServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly TeamCalendarService _service;

    private const int TestCompanyId = 1;
    private const int OwnerId = 10;

    public TeamCalendarServiceTests()
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

        _service = new TeamCalendarService(_db, _tenantResolverMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task<AppUser> CreateTestUserAsync(int id, string name)
    {
        var user = new AppUser
        {
            Id = id,
            Email = $"user{id}@test.com",
            CompanyId = TestCompanyId,
            Role = UserRole.Employee,
            DisplayName = name,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// TC-01: CreateCalendarAsync inserts a TeamCalendar with correct fields.
    /// </summary>
    [Fact]
    public async Task TC01_CreateCalendarAsync_CreatesCalendarWithCorrectFields()
    {
        // Act
        var (success, calendar, error) = await _service.CreateCalendarAsync(OwnerId, "My Team");

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
        calendar.Should().NotBeNull();
        calendar!.Name.Should().Be("My Team");
        calendar.OwnerId.Should().Be(OwnerId);
        calendar.CompanyId.Should().Be(TestCompanyId);
        calendar.IsDeleted.Should().BeFalse();
        calendar.Id.Should().BeGreaterThan(0);

        // Verify persisted
        var fromDb = await _db.TeamCalendars.FindAsync(calendar.Id);
        fromDb.Should().NotBeNull();
    }

    /// <summary>
    /// TC-02: CreateCalendarAsync rejects duplicate name for the same owner.
    /// </summary>
    [Fact]
    public async Task TC02_CreateCalendarAsync_RejectsDuplicateName()
    {
        // Arrange
        await _service.CreateCalendarAsync(OwnerId, "QA Crew");

        // Act — try to create another with the same name
        var (success, calendar, error) = await _service.CreateCalendarAsync(OwnerId, "QA Crew");

        // Assert
        success.Should().BeFalse();
        calendar.Should().BeNull();
        error.Should().Contain("already exists");
    }

    /// <summary>
    /// TC-03: CreateCalendarAsync rejects name over 60 characters.
    /// </summary>
    [Fact]
    public async Task TC03_CreateCalendarAsync_RejectsLongName()
    {
        // Arrange
        var longName = new string('A', 61);

        // Act
        var (success, calendar, error) = await _service.CreateCalendarAsync(OwnerId, longName);

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("1-60 characters");
    }

    /// <summary>
    /// TC-04: DeleteCalendarAsync soft-deletes (sets IsDeleted=true).
    /// </summary>
    [Fact]
    public async Task TC04_DeleteCalendarAsync_SoftDeletes()
    {
        // Arrange
        var (_, calendar, _) = await _service.CreateCalendarAsync(OwnerId, "To Delete");
        calendar.Should().NotBeNull();

        // Act
        var (success, error) = await _service.DeleteCalendarAsync(calendar!.Id, OwnerId);

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();

        var fromDb = await _db.TeamCalendars.FindAsync(calendar.Id);
        fromDb.Should().NotBeNull();
        fromDb!.IsDeleted.Should().BeTrue();

        // GetCalendarsForOwnerAsync should not include deleted calendars
        var calendars = await _service.GetCalendarsForOwnerAsync(OwnerId);
        calendars.Should().NotContain(c => c.Id == calendar.Id);
    }

    /// <summary>
    /// TC-05: AddMembersAsync adds members and prevents duplicates.
    /// </summary>
    [Fact]
    public async Task TC05_AddMembersAsync_AddsMembersAndPreventsDuplicates()
    {
        // Arrange
        var user1 = await CreateTestUserAsync(20, "Alice");
        var user2 = await CreateTestUserAsync(21, "Bob");
        var (_, calendar, _) = await _service.CreateCalendarAsync(OwnerId, "Dev Team");

        // Act — add both users
        var (success, error) = await _service.AddMembersAsync(
            calendar!.Id, OwnerId, new List<int> { user1.Id, user2.Id });

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();

        var members = await _service.GetCalendarMembersAsync(calendar.Id, OwnerId);
        members.Should().HaveCount(2);

        // Act — add user1 again (should not duplicate)
        var (success2, _) = await _service.AddMembersAsync(
            calendar.Id, OwnerId, new List<int> { user1.Id });

        success2.Should().BeTrue();

        // Still 2 members
        var membersAfter = await _service.GetCalendarMembersAsync(calendar.Id, OwnerId);
        membersAfter.Should().HaveCount(2);
    }

    /// <summary>
    /// TC-06: RemoveMembersAsync removes specified members.
    /// </summary>
    [Fact]
    public async Task TC06_RemoveMembersAsync_RemovesSpecifiedMembers()
    {
        // Arrange
        var user1 = await CreateTestUserAsync(30, "Charlie");
        var user2 = await CreateTestUserAsync(31, "Diana");
        var (_, calendar, _) = await _service.CreateCalendarAsync(OwnerId, "Ops Team");
        await _service.AddMembersAsync(calendar!.Id, OwnerId, new List<int> { user1.Id, user2.Id });

        // Act — remove user1
        var (success, error) = await _service.RemoveMembersAsync(
            calendar.Id, OwnerId, new List<int> { user1.Id });

        // Assert
        success.Should().BeTrue();
        var members = await _service.GetCalendarMembersAsync(calendar.Id, OwnerId);
        members.Should().HaveCount(1);
        members.First().Id.Should().Be(user2.Id);
    }

    /// <summary>
    /// TC-07: SetMembersAsync replaces all members atomically.
    /// </summary>
    [Fact]
    public async Task TC07_SetMembersAsync_ReplacesAllMembers()
    {
        // Arrange
        var user1 = await CreateTestUserAsync(40, "Eve");
        var user2 = await CreateTestUserAsync(41, "Frank");
        var user3 = await CreateTestUserAsync(42, "Grace");
        var (_, calendar, _) = await _service.CreateCalendarAsync(OwnerId, "Replace Team");

        // Add initial members (user1, user2)
        await _service.AddMembersAsync(calendar!.Id, OwnerId, new List<int> { user1.Id, user2.Id });

        // Act — replace with user2, user3 (removing user1, adding user3)
        var (success, error) = await _service.SetMembersAsync(
            calendar.Id, OwnerId, new List<int> { user2.Id, user3.Id });

        // Assert
        success.Should().BeTrue();
        var members = await _service.GetCalendarMembersAsync(calendar.Id, OwnerId);
        members.Should().HaveCount(2);
        members.Select(m => m.Id).Should().BeEquivalentTo(new[] { user2.Id, user3.Id });
    }

    /// <summary>
    /// TC-08: GetCalendarByIdAsync returns null for wrong owner (ownership check).
    /// </summary>
    [Fact]
    public async Task TC08_GetCalendarByIdAsync_ReturnsNull_ForWrongOwner()
    {
        // Arrange
        var (_, calendar, _) = await _service.CreateCalendarAsync(OwnerId, "Private Calendar");

        // Act — try to access with a different owner
        var result = await _service.GetCalendarByIdAsync(calendar!.Id, ownerId: 999);

        // Assert
        result.Should().BeNull("calendar should only be accessible by its owner");
    }
}
