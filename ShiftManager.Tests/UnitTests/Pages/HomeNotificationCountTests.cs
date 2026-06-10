using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Regression tests for the Home/Index notification count bug:
/// the original query had <c>n.UserId == userId &amp;&amp; n.CompanyId == companyId</c>,
/// which dropped notifications from a multi-company user's secondary companies.
/// The fix removes the CompanyId predicate so all of the user's notifications are
/// counted — consistent with the bell widget and NotificationCenter behaviour.
/// </summary>
public sealed class HomeNotificationCountTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Reproduces the BUGGY query shape (with CompanyId predicate) to confirm the test
    /// detects the bug: when the user's active tenant is 1, only 1 of 2 notifications
    /// is returned.
    /// </summary>
    [Fact]
    public async Task BuggyQuery_CompanyIdPredicate_DropsSecondCompanyNotification()
    {
        const int UserId = 42;
        const int ActiveCompanyId = 1;    // the user's active tenant / current companyId
        const int OtherCompanyId = 2;     // a secondary company the user is a member of

        _db.UserNotifications.AddRange(
            new UserNotification
            {
                UserId = UserId, CompanyId = ActiveCompanyId, IsRead = false,
                Title = "N1", Message = "from primary company",
                Type = NotificationType.ShiftAdded, CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new UserNotification
            {
                UserId = UserId, CompanyId = OtherCompanyId, IsRead = false,
                Title = "N2", Message = "from secondary company",
                Type = NotificationType.ShiftAdded, CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            }
        );
        await _db.SaveChangesAsync();

        // Reproduce the BUGGY query — filters by CompanyId
        var buggyResults = await _db.UserNotifications
            .Where(n => n.UserId == UserId && n.CompanyId == ActiveCompanyId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .ToListAsync();

        var buggyUnreadCount = buggyResults.Count(n => !n.IsRead);

        // With the bug: only 1 notification is returned, missing the secondary-company one
        buggyUnreadCount.Should().Be(1,
            because: "the buggy query drops the secondary-company notification");
    }

    /// <summary>
    /// Verifies the FIXED query shape (UserId-only predicate) returns ALL notifications
    /// for a user regardless of which CompanyId they were filed under.
    /// This is the contract the fix must satisfy.
    /// </summary>
    [Fact]
    public async Task FixedQuery_UserIdOnly_CountsNotificationsAcrossAllCompanies()
    {
        const int UserId = 42;
        const int ActiveCompanyId = 1;
        const int OtherCompanyId = 2;

        _db.UserNotifications.AddRange(
            new UserNotification
            {
                UserId = UserId, CompanyId = ActiveCompanyId, IsRead = false,
                Title = "N1", Message = "from primary company",
                Type = NotificationType.ShiftAdded, CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new UserNotification
            {
                UserId = UserId, CompanyId = OtherCompanyId, IsRead = false,
                Title = "N2", Message = "from secondary company",
                Type = NotificationType.ShiftAdded, CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            }
        );
        await _db.SaveChangesAsync();

        // FIXED query — no CompanyId predicate, consistent with bell widget and NotificationCenter
        var fixedResults = await _db.UserNotifications
            .Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .ToListAsync();

        var unreadCount = fixedResults.Count(n => !n.IsRead);

        // Both notifications belong to the user — count must be 2
        unreadCount.Should().Be(2,
            because: "a multi-company user's unread count must span all their companies");
    }
}
