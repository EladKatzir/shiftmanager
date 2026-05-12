using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Moq;
using FluentAssertions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class AnnouncementServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly AnnouncementService _service;

    public AnnouncementServiceTests()
    {
        // Create InMemory database without tenant resolver to avoid query filters
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(1);

        _service = new AnnouncementService(_db, _tenantResolverMock.Object);
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_FiltersExpiredAnnouncements()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        // Add expired announcement
        var expired = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Expired",
            Content = "This announcement has expired",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            CreatedBy = 1
        };

        // Add active announcement
        var active = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Active",
            Content = "This announcement is active",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        _db.Announcements.AddRange(expired, active);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_FiltersInactiveAnnouncements()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        // Add inactive announcement
        var inactive = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Inactive",
            Content = "This announcement is inactive",
            IsActive = false,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        // Add active announcement
        var active = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Active",
            Content = "This announcement is active",
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        _db.Announcements.AddRange(inactive, active);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_FiltersByDepartmentScope()
    {
        // Arrange
        var dept1 = new Department { Id = 1, Name = "Engineering", MoleculeId = 1 };
        var dept2 = new Department { Id = 999, Name = "Sales", MoleculeId = 1 };
        _db.Departments.AddRange(dept1, dept2);

        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            DepartmentId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        // Add department-scoped announcement for user's department
        var forMyDept = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "My Dept",
            Content = "For my department",
            IsActive = true,
            Scope = AnnouncementScope.Department,
            TargetDepartmentId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        // Add department-scoped announcement for different department
        var otherDept = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Other Dept",
            Content = "For other department",
            IsActive = true,
            Scope = AnnouncementScope.Department,
            TargetDepartmentId = 999,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        _db.Announcements.AddRange(forMyDept, otherDept);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("My Dept");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_FiltersByRoleScope()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Manager,
            DisplayName = "Test Manager",
            IsActive = true
        };
        _db.Users.Add(user);

        // Add role-scoped announcement for user's role
        var forMyRole = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Managers Only",
            Content = "For managers only",
            IsActive = true,
            Scope = AnnouncementScope.Role,
            TargetRole = "Manager",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        // Add role-scoped announcement for different role
        var otherRole = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Directors Only",
            Content = "For directors only",
            IsActive = true,
            Scope = AnnouncementScope.Role,
            TargetRole = "Director",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        _db.Announcements.AddRange(forMyRole, otherRole);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Managers Only");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_ReturnsAllScopeForAnyUser()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        var allScoped = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "For Everyone",
            Content = "This is for everyone",
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        _db.Announcements.Add(allScoped);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("For Everyone");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_ReturnsEmptyListForNonExistentUser()
    {
        // Arrange - no user added

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(999);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_OrdersByPinnedFirst()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        var unpinned = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Unpinned",
            Content = "Not pinned",
            IsActive = true,
            IsPinned = false,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow.AddHours(1), // Created later
            CreatedBy = 1
        };

        var pinned = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Pinned",
            Content = "Pinned to top",
            IsActive = true,
            IsPinned = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow, // Created earlier
            CreatedBy = 1
        };

        _db.Announcements.AddRange(unpinned, pinned);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Pinned", "Pinned announcements should appear first");
        result[1].Title.Should().Be("Unpinned");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_OrdersByCreatedAtDescending()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        var older = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Older",
            Content = "Created earlier",
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            CreatedBy = 1
        };

        var newer = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Newer",
            Content = "Created later",
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 1
        };

        _db.Announcements.AddRange(older, newer);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer", "Newer announcements should appear first");
        result[1].Title.Should().Be("Older");
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_LimitsTo20Results()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        // Add 25 announcements
        for (int i = 1; i <= 25; i++)
        {
            var announcement = new Announcement
            {
                Id = i,
                CompanyId = 1,
                Title = $"Announcement {i}",
                Content = $"Content {i}",
                IsActive = true,
                Scope = AnnouncementScope.All,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                CreatedBy = 1
            };
            _db.Announcements.Add(announcement);
        }
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetActiveAnnouncementsAsync(1);

        // Assert
        result.Should().HaveCount(20, "Results should be limited to 20");
    }

    [Fact]
    public async Task CreateAsync_SetsCompanyIdFromTenant()
    {
        // Arrange
        _tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(42);
        var service = new AnnouncementService(_db, _tenantResolverMock.Object);

        var announcement = new Announcement
        {
            Title = "Test Announcement",
            Content = "Test content",
            Scope = AnnouncementScope.All
        };

        // Act
        var result = await service.CreateAsync(announcement, createdBy: 1);

        // Assert
        result.CompanyId.Should().Be(42, "CompanyId should be set from tenant resolver");
        result.CreatedBy.Should().Be(1);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedAtTimestamp()
    {
        // Arrange
        var announcement = new Announcement
        {
            Title = "Test",
            Content = "Content",
            Scope = AnnouncementScope.All
        };

        var beforeCreate = DateTime.UtcNow;

        // Act
        var result = await _service.CreateAsync(announcement, createdBy: 1);

        // Assert
        result.CreatedAt.Should().BeOnOrAfter(beforeCreate);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForNonExistentId()
    {
        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsAnnouncementWithCreator()
    {
        // Arrange
        var creator = new AppUser
        {
            Id = 1,
            Email = "admin@test.com",
            CompanyId = 1,
            Role = UserRole.Manager,
            DisplayName = "Admin User",
            IsActive = true
        };
        _db.Users.Add(creator);
        await _db.SaveChangesAsync();

        var announcement = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Test",
            Content = "Content",
            CreatedBy = 1,
            Creator = creator,  // Explicitly set navigation property for InMemory DB
            CreatedAt = DateTime.UtcNow,
            Scope = AnnouncementScope.All
        };
        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test");
        result.Creator.Should().NotBeNull();
        result.Creator!.DisplayName.Should().Be("Admin User");
    }

    [Fact]
    public async Task GetAllAnnouncementsAsync_ReturnsAllAnnouncements()
    {
        // Arrange
        var announcement1 = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "First",
            Content = "Content 1",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Scope = AnnouncementScope.All
        };

        var announcement2 = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Second",
            Content = "Content 2",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            IsActive = true,
            Scope = AnnouncementScope.All
        };

        _db.Announcements.AddRange(announcement1, announcement2);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAnnouncementsAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAnnouncementsAsync_ExcludesExpiredByDefault()
    {
        // Arrange
        var active = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Active",
            Content = "Content",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Scope = AnnouncementScope.All
        };

        var expired = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Expired",
            Content = "Content",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            Scope = AnnouncementScope.All
        };

        _db.Announcements.AddRange(active, expired);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAnnouncementsAsync(includeExpired: false);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetAllAnnouncementsAsync_IncludesExpiredWhenRequested()
    {
        // Arrange
        var active = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Active",
            Content = "Content",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Scope = AnnouncementScope.All
        };

        var expired = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Expired",
            Content = "Content",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            Scope = AnnouncementScope.All
        };

        _db.Announcements.AddRange(active, expired);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAnnouncementsAsync(includeExpired: true);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesExistingAnnouncement()
    {
        // Arrange
        var announcement = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Original",
            Content = "Original Content",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Scope = AnnouncementScope.All
        };
        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        // Act
        announcement.Title = "Updated";
        announcement.Content = "Updated Content";
        await _service.UpdateAsync(announcement);

        // Assert
        var updated = await _db.Announcements.FindAsync(1);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Updated");
        updated.Content.Should().Be("Updated Content");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnnouncement()
    {
        // Arrange
        var announcement = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "To Delete",
            Content = "Content",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Scope = AnnouncementScope.All
        };
        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        // Act
        await _service.DeleteAsync(1);

        // Assert
        var deleted = await _db.Announcements.FindAsync(1);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DoesNothingForNonExistentId()
    {
        // Act - should not throw
        await _service.DeleteAsync(999);

        // Assert - no exception means success
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCountOfRecentAnnouncements()
    {
        // Arrange
        var user = new AppUser
        {
            Id = 1,
            Email = "test@test.com",
            CompanyId = 1,
            Role = UserRole.Employee,
            DisplayName = "Test User",
            IsActive = true
        };
        _db.Users.Add(user);

        // Add recent announcement (within last 7 days)
        var recent = new Announcement
        {
            Id = 1,
            CompanyId = 1,
            Title = "Recent",
            Content = "Recent content",
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            CreatedBy = 1
        };

        // Add old announcement (older than 7 days)
        var old = new Announcement
        {
            Id = 2,
            CompanyId = 1,
            Title = "Old",
            Content = "Old content",
            IsActive = true,
            Scope = AnnouncementScope.All,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            CreatedBy = 1
        };

        _db.Announcements.AddRange(recent, old);
        await _db.SaveChangesAsync();

        // Act
        var count = await _service.GetUnreadCountAsync(1);

        // Assert
        count.Should().Be(1, "Only announcements from last 7 days should be counted");
    }

    public void Dispose()
    {
        _db?.Dispose();
        _sqliteConnection.Dispose();
    }
}
