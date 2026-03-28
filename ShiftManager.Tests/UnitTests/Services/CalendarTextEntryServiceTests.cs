using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class CalendarTextEntryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CalendarTextEntryService _service;

    private const int CompanyId = 1;

    public CalendarTextEntryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _service = new CalendarTextEntryService(_db);

        // Seed users
        _db.Users.AddRange(
            new AppUser { Id = 1, Email = "user1@test.com", DisplayName = "User 1", CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = 2, Email = "user2@test.com", DisplayName = "User 2", CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = 3, Email = "user3@test.com", DisplayName = "User 3", CompanyId = 2, IsActive = true, Role = UserRole.Employee }
        );
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // --- AddAsync ---

    [Fact]
    public async Task AddAsync_HappyPath_CreatesEntry()
    {
        var date = new DateOnly(2026, 3, 15);

        var entry = await _service.AddAsync(1, date, "Meeting at 10:00", createdByUserId: 100);

        entry.Should().NotBeNull();
        entry.UserId.Should().Be(1);
        entry.Date.Should().Be(date);
        entry.Text.Should().Be("Meeting at 10:00");
        entry.CreatedByUserId.Should().Be(100);
        entry.CompanyId.Should().Be(CompanyId);
        entry.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AddAsync_SetsTargetUserCompanyId()
    {
        // User 3 is in company 2 — entry should get company 2
        var entry = await _service.AddAsync(3, new DateOnly(2026, 3, 15), "Note", createdByUserId: 1);

        entry.CompanyId.Should().Be(2);
    }

    [Fact]
    public async Task AddAsync_UserNotFound_ThrowsArgumentException()
    {
        var act = async () => await _service.AddAsync(999, new DateOnly(2026, 3, 15), "Note", createdByUserId: 1);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not found*");
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_ReturnsEntry_WhenExists()
    {
        _db.CalendarTextEntries.Add(new CalendarTextEntry
        {
            Id = 1, UserId = 1, Date = new DateOnly(2026, 3, 15),
            Text = "Test entry", CompanyId = CompanyId, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetByIdAsync(1);

        result.Should().NotBeNull();
        result!.Text.Should().Be("Test entry");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(999);

        result.Should().BeNull();
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_ExistingEntry_RemovesAndReturnsTrue()
    {
        _db.CalendarTextEntries.Add(new CalendarTextEntry
        {
            Id = 1, UserId = 1, Date = new DateOnly(2026, 3, 15),
            Text = "Delete me", CompanyId = CompanyId, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.DeleteAsync(1);

        result.Should().BeTrue();
        (await _db.CalendarTextEntries.FindAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        var result = await _service.DeleteAsync(999);

        result.Should().BeFalse();
    }

    // --- GetForDateRangeAsync ---

    [Fact]
    public async Task GetForDateRangeAsync_ReturnsGroupedEntries()
    {
        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 5);

        _db.CalendarTextEntries.AddRange(
            new CalendarTextEntry { Id = 1, UserId = 1, Date = date1, Text = "Entry A", CompanyId = CompanyId, CreatedByUserId = 1 },
            new CalendarTextEntry { Id = 2, UserId = 1, Date = date1, Text = "Entry B", CompanyId = CompanyId, CreatedByUserId = 1 },
            new CalendarTextEntry { Id = 3, UserId = 2, Date = date2, Text = "Entry C", CompanyId = CompanyId, CreatedByUserId = 1 },
            new CalendarTextEntry { Id = 4, UserId = 1, Date = new DateOnly(2026, 4, 1), Text = "Outside range", CompanyId = CompanyId, CreatedByUserId = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetForDateRangeAsync(date1, date2);

        result.Should().HaveCount(2); // two (userId, date) groups
        result[(1, date1)].Should().HaveCount(2);
        result[(2, date2)].Should().HaveCount(1);
    }

    [Fact]
    public async Task GetForDateRangeAsync_EmptyRange_ReturnsEmpty()
    {
        var result = await _service.GetForDateRangeAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        result.Should().BeEmpty();
    }

    // --- GetForUsersAndDateRangeAsync ---

    [Fact]
    public async Task GetForUsersAndDateRangeAsync_FiltersToSpecifiedUsers()
    {
        var date = new DateOnly(2026, 3, 15);

        _db.CalendarTextEntries.AddRange(
            new CalendarTextEntry { Id = 1, UserId = 1, Date = date, Text = "User1 entry", CompanyId = CompanyId, CreatedByUserId = 1 },
            new CalendarTextEntry { Id = 2, UserId = 2, Date = date, Text = "User2 entry", CompanyId = CompanyId, CreatedByUserId = 1 },
            new CalendarTextEntry { Id = 3, UserId = 3, Date = date, Text = "User3 entry", CompanyId = 2, CreatedByUserId = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetForUsersAndDateRangeAsync(new[] { 1, 3 }, date, date);

        result.Should().HaveCount(2);
        result.Should().ContainKey((1, date));
        result.Should().ContainKey((3, date));
        result.Should().NotContainKey((2, date));
    }

    [Fact]
    public async Task GetForUsersAndDateRangeAsync_EmptyUserList_ReturnsEmpty()
    {
        _db.CalendarTextEntries.Add(new CalendarTextEntry
        {
            Id = 1, UserId = 1, Date = new DateOnly(2026, 3, 15),
            Text = "Entry", CompanyId = CompanyId, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetForUsersAndDateRangeAsync(Array.Empty<int>(), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        result.Should().BeEmpty();
    }
}
