using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class UserDataExportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IRoleService> _roleServiceMock;
    private readonly UserDataExportService _service;

    private const int CompanyId = 1;

    public UserDataExportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _roleServiceMock = new Mock<IRoleService>();

        // Default: no role assignments
        _roleServiceMock.Setup(r => r.GetUserRolesAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<UserRoleAssignment>());

        _service = new UserDataExportService(
            _db,
            Mock.Of<ILogger<UserDataExportService>>(),
            _roleServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<AppUser> SeedUserAsync(int id = 1, string email = "user@test.com",
        string displayName = "Test User", UserRole role = UserRole.Employee)
    {
        var user = new AppUser
        {
            Id = id,
            CompanyId = CompanyId,
            Email = email,
            DisplayName = displayName,
            Role = role,
            IsActive = true,
            Phone = "050-1234567",
            City = "Tel Aviv"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // --- ExportUserDataAsync ---

    [Fact]
    public async Task ExportUserDataAsync_UserNotFound_ReturnsNull()
    {
        var result = await _service.ExportUserDataAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExportUserDataAsync_ExistingUser_ReturnsNonNull()
    {
        await SeedUserAsync();

        var result = await _service.ExportUserDataAsync(1);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesProfile()
    {
        await SeedUserAsync(email: "export@test.com", displayName: "Export User");

        var result = (await _service.ExportUserDataAsync(1))!;

        // Use reflection to check anonymous type properties
        var type = result.GetType();
        var profileProp = type.GetProperty("Profile");
        profileProp.Should().NotBeNull();
        var profile = profileProp!.GetValue(result)!;
        var profileType = profile.GetType();
        ((string)profileType.GetProperty("Email")!.GetValue(profile)!).Should().Be("export@test.com");
        ((string)profileType.GetProperty("DisplayName")!.GetValue(profile)!).Should().Be("Export User");
    }

    [Fact]
    public async Task ExportUserDataAsync_ProfileExcludesPasswordHash()
    {
        await SeedUserAsync();

        var result = (await _service.ExportUserDataAsync(1))!;

        var type = result.GetType();
        var profile = type.GetProperty("Profile")!.GetValue(result)!;
        var profileType = profile.GetType();

        // Password hash and salt should NOT be in the exported profile
        profileType.GetProperty("PasswordHash").Should().BeNull();
        profileType.GetProperty("PasswordSalt").Should().BeNull();
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesExportMetadata()
    {
        await SeedUserAsync();

        var result = (await _service.ExportUserDataAsync(1))!;

        var type = result.GetType();
        var exportedAtProp = type.GetProperty("ExportedAt");
        exportedAtProp.Should().NotBeNull();
        var versionProp = type.GetProperty("ExportVersion");
        versionProp.Should().NotBeNull();

        var version = (string)versionProp!.GetValue(result)!;
        version.Should().Be("1.0");
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesShiftAssignments()
    {
        var user = await SeedUserAsync();

        var shiftType = new ShiftType
        {
            Id = 1,
            Name = "Morning",
            Key = ShiftType.KEY_MORNING,
            MoleculeId = 1,
            Start = new TimeOnly(7, 0),
            End = new TimeOnly(15, 0)
        };
        _db.Add(shiftType);

        var instance = new ShiftInstance
        {
            Id = 1,
            CompanyId = CompanyId,
            ShiftTypeId = 1,
            WorkDate = new DateOnly(2026, 3, 15)
        };
        _db.ShiftInstances.Add(instance);

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1,
            CompanyId = CompanyId,
            ShiftInstanceId = 1,
            UserId = user.Id
        });
        await _db.SaveChangesAsync();

        dynamic result = (await _service.ExportUserDataAsync(user.Id))!;

        var type = result.GetType();
        var assignments = type.GetProperty("ShiftAssignments")!.GetValue(result);
        ((System.Collections.IList)assignments).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesChores()
    {
        var user = await SeedUserAsync();

        _db.Chores.Add(new Chore
        {
            Id = 1,
            CompanyId = CompanyId,
            UserId = user.Id,
            Date = new DateOnly(2026, 3, 15),
            Title = "Guard Duty",
            CreatedBy = user.Id
        });
        await _db.SaveChangesAsync();

        dynamic result = (await _service.ExportUserDataAsync(user.Id))!;

        var type = result.GetType();
        var chores = type.GetProperty("Chores")!.GetValue(result);
        ((System.Collections.IList)chores).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesOnDutyEntries()
    {
        var user = await SeedUserAsync();

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1,
            UserId = user.Id,
            Date = new DateOnly(2026, 3, 15),
            Type = 0,
            CreatedBy = user.Id
        });
        await _db.SaveChangesAsync();

        dynamic result = (await _service.ExportUserDataAsync(user.Id))!;

        var type = result.GetType();
        var onDuties = type.GetProperty("OnDutyEntries")!.GetValue(result);
        ((System.Collections.IList)onDuties).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesTimeOffRequests()
    {
        var user = await SeedUserAsync();

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1,
            CompanyId = CompanyId,
            UserId = user.Id,
            StartDate = new DateOnly(2026, 3, 10),
            EndDate = new DateOnly(2026, 3, 12),
            Type = TimeOffType.Vacation,
            Status = RequestStatus.Pending
        });
        await _db.SaveChangesAsync();

        dynamic result = (await _service.ExportUserDataAsync(user.Id))!;

        var type = result.GetType();
        var timeOff = type.GetProperty("TimeOffRequests")!.GetValue(result);
        ((System.Collections.IList)timeOff).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_DoesNotIncludeOtherUsersData()
    {
        await SeedUserAsync(id: 1, email: "user1@test.com");
        await SeedUserAsync(id: 2, email: "user2@test.com", displayName: "Other User");

        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CompanyId, UserId = 1, Date = new DateOnly(2026, 3, 15), Title = "User1 Chore", CreatedBy = 1 },
            new Chore { Id = 2, CompanyId = CompanyId, UserId = 2, Date = new DateOnly(2026, 3, 15), Title = "User2 Chore", CreatedBy = 2 }
        );
        await _db.SaveChangesAsync();

        dynamic result = (await _service.ExportUserDataAsync(1))!;

        var type = result.GetType();
        var chores = type.GetProperty("Chores")!.GetValue(result);
        ((System.Collections.IList)chores).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesRoleAssignments()
    {
        var user = await SeedUserAsync();

        var roleAssignments = new List<UserRoleAssignment>
        {
            new UserRoleAssignment
            {
                Id = 1,
                UserId = user.Id,
                CompanyId = CompanyId,
                IsActive = true,
                AssignedAt = DateTime.UtcNow,
                RoleTemplate = new RoleTemplate { Id = 1, Key = "TestRole" }
            }
        };
        _roleServiceMock.Setup(r => r.GetUserRolesAsync(user.Id))
            .ReturnsAsync(roleAssignments);

        dynamic result = (await _service.ExportUserDataAsync(user.Id))!;

        var type = result.GetType();
        var roles = type.GetProperty("RoleAssignments")!.GetValue(result);
        ((System.Collections.IList)roles).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesFriendships()
    {
        var user = await SeedUserAsync(id: 1);
        await SeedUserAsync(id: 2, email: "friend@test.com", displayName: "Friend");

        _db.UserFriendships.Add(new UserFriendship
        {
            Id = 1,
            UserId = 1,
            FriendId = 2,
            Status = FriendshipStatus.Accepted,
            RequestedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        dynamic result = (await _service.ExportUserDataAsync(1))!;

        var type = result.GetType();
        var friendships = type.GetProperty("Friendships")!.GetValue(result);
        ((System.Collections.IList)friendships).Count.Should().Be(1);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesAllSections()
    {
        await SeedUserAsync();

        var result = (await _service.ExportUserDataAsync(1))!;

        var type = result.GetType();
        var expectedProperties = new[]
        {
            "ExportedAt", "ExportVersion", "Profile",
            "ShiftAssignments", "Chores", "OnDutyEntries",
            "TimeOffRequests", "SwapRequests", "Notifications",
            "GameScores", "AuditLogEntries", "Grants",
            "RoleAssignments", "DayNotes", "Friendships"
        };

        foreach (var prop in expectedProperties)
        {
            type.GetProperty(prop).Should().NotBeNull($"export should include {prop}");
        }
    }
}
