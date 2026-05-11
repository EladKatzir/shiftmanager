using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services.V3Hierarchy;

/// <summary>
/// Tests for Task 13.4: Circle/Friends Visibility
/// Verifies that friendship system correctly controls cross-molecule visibility.
/// NOTE: Stays on In-Memory; SearchUsersAsync's LINQ translation is independently guarded
/// by `SqliteTranslationGuardTests` against real SQLite to prevent re-regression of the
/// GRIFFIN-USERLOOKUP-510 bug class.
/// </summary>
public class FriendshipServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FriendshipService _service;

    public FriendshipServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        var localizer = Mock.Of<IStringLocalizer<SharedResources>>();
        var logger = Mock.Of<ILogger<FriendshipService>>();
        _service = new FriendshipService(_db, localizer, logger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<(AppUser user1, AppUser user2, Company company1, Company company2)> SetupUsersAsync()
    {
        // Create hierarchy
        var project = new Project { Name = "Test", DisplayName = "Test" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var molecule1 = new Molecule { AreaId = area.Id, Name = "Molecule1", Type = MoleculeType.Workforce };
        var molecule2 = new Molecule { AreaId = area.Id, Name = "Molecule2", Type = MoleculeType.Workforce };
        _db.Molecules.AddRange(molecule1, molecule2);
        await _db.SaveChangesAsync();

        var company1 = new Company { MoleculeId = molecule1.Id, Name = "Company1" };
        var company2 = new Company { MoleculeId = molecule2.Id, Name = "Company2" };
        _db.Companies.AddRange(company1, company2);
        await _db.SaveChangesAsync();

        // Create users in different molecules
        var user1 = new AppUser
        {
            CompanyId = company1.Id,
            Email = "user1@test.com",
            DisplayName = "User One",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var user2 = new AppUser
        {
            CompanyId = company2.Id,
            Email = "user2@test.com",
            DisplayName = "User Two",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.AddRange(user1, user2);
        await _db.SaveChangesAsync();

        return (user1, user2, company1, company2);
    }

    [Fact]
    public async Task SendRequest_CreatesNewFriendship_WithPendingStatus()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();

        // Act
        var result = await _service.SendRequestAsync(user1.Id, user2.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.FriendshipId.Should().HaveValue();

        var friendship = await _db.UserFriendships.FindAsync(result.FriendshipId);
        friendship.Should().NotBeNull();
        friendship!.Status.Should().Be(FriendshipStatus.Pending);
        friendship.UserId.Should().Be(user1.Id);
        friendship.FriendId.Should().Be(user2.Id);
    }

    [Fact]
    public async Task AreFriends_WithPendingRequest_ReturnsFalse()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();
        await _service.SendRequestAsync(user1.Id, user2.Id);

        // Act
        var areFriends = await _service.AreFriendsAsync(user1.Id, user2.Id);

        // Assert
        areFriends.Should().BeFalse("Pending requests should not grant friendship visibility");
    }

    [Fact]
    public async Task AcceptRequest_GrantsFriendshipVisibility()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();
        var sendResult = await _service.SendRequestAsync(user1.Id, user2.Id);

        // Act
        var acceptResult = await _service.AcceptRequestAsync(sendResult.FriendshipId!.Value, user2.Id);

        // Assert
        acceptResult.Success.Should().BeTrue();

        var areFriends = await _service.AreFriendsAsync(user1.Id, user2.Id);
        areFriends.Should().BeTrue("Accepted friendship should grant visibility");
    }

    [Fact]
    public async Task GetFriendIds_ReturnsOnlyAcceptedFriends()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();

        // Create a third user
        var company3 = new Company { MoleculeId = 1, Name = "Company3" };
        _db.Companies.Add(company3);
        await _db.SaveChangesAsync();

        var user3 = new AppUser
        {
            CompanyId = company3.Id,
            Email = "user3@test.com",
            DisplayName = "User Three",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user3);
        await _db.SaveChangesAsync();

        // User1 sends request to User2 (pending)
        await _service.SendRequestAsync(user1.Id, user2.Id);

        // User1 sends request to User3 and User3 accepts
        var request3 = await _service.SendRequestAsync(user1.Id, user3.Id);
        await _service.AcceptRequestAsync(request3.FriendshipId!.Value, user3.Id);

        // Act
        var friendIds = await _service.GetFriendIdsAsync(user1.Id);

        // Assert
        friendIds.Should().Contain(user3.Id, "Accepted friend should be in list");
        friendIds.Should().NotContain(user2.Id, "Pending friend should not be in list");
    }

    [Fact]
    public async Task RejectRequest_DoesNotGrantVisibility()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();
        var sendResult = await _service.SendRequestAsync(user1.Id, user2.Id);

        // Act
        var rejectResult = await _service.RejectRequestAsync(sendResult.FriendshipId!.Value, user2.Id);

        // Assert
        rejectResult.Success.Should().BeTrue();

        var areFriends = await _service.AreFriendsAsync(user1.Id, user2.Id);
        areFriends.Should().BeFalse("Rejected requests should not grant visibility");
    }

    [Fact]
    public async Task RemoveFriend_RevokesVisibility()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();
        var sendResult = await _service.SendRequestAsync(user1.Id, user2.Id);
        await _service.AcceptRequestAsync(sendResult.FriendshipId!.Value, user2.Id);

        // Verify they are friends first
        var areFriendsBefore = await _service.AreFriendsAsync(user1.Id, user2.Id);
        areFriendsBefore.Should().BeTrue();

        // Act
        var removeResult = await _service.RemoveFriendAsync(sendResult.FriendshipId!.Value, user1.Id);

        // Assert
        removeResult.Success.Should().BeTrue();

        var areFriendsAfter = await _service.AreFriendsAsync(user1.Id, user2.Id);
        areFriendsAfter.Should().BeFalse("Removed friendship should revoke visibility");
    }

    [Fact]
    public async Task GetFriends_ReturnsBidirectionalFriendship()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();
        var sendResult = await _service.SendRequestAsync(user1.Id, user2.Id);
        await _service.AcceptRequestAsync(sendResult.FriendshipId!.Value, user2.Id);

        // Act
        var user1Friends = await _service.GetFriendsAsync(user1.Id);
        var user2Friends = await _service.GetFriendsAsync(user2.Id);

        // Assert
        user1Friends.Should().Contain(f => f.FriendId == user2.Id, "User1 should see User2 as friend");
        user2Friends.Should().Contain(f => f.FriendId == user1.Id, "User2 should see User1 as friend");
    }

    [Fact]
    public async Task SendRequest_TwiceToSameUser_Fails()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();
        await _service.SendRequestAsync(user1.Id, user2.Id);

        // Act
        var duplicateResult = await _service.SendRequestAsync(user1.Id, user2.Id);

        // Assert
        duplicateResult.Success.Should().BeFalse("Cannot send duplicate request");
    }

    [Fact]
    public async Task AcceptRequest_ByWrongUser_Fails()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();

        // Create a third user
        var user3 = new AppUser
        {
            CompanyId = 1,
            Email = "user3@test.com",
            DisplayName = "User Three",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user3);
        await _db.SaveChangesAsync();

        var sendResult = await _service.SendRequestAsync(user1.Id, user2.Id);

        // Act - User3 tries to accept User1's request to User2
        var acceptResult = await _service.AcceptRequestAsync(sendResult.FriendshipId!.Value, user3.Id);

        // Assert
        acceptResult.Success.Should().BeFalse("Only the target user can accept requests");
    }

    [Fact]
    public async Task SearchUsers_ExcludesExistingFriends()
    {
        // Arrange
        var (user1, user2, _, _) = await SetupUsersAsync();

        // Create third user with similar name
        var user3 = new AppUser
        {
            CompanyId = 1,
            Email = "user3@test.com",
            DisplayName = "User Three",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user3);
        await _db.SaveChangesAsync();

        // User1 is friends with User2
        var sendResult = await _service.SendRequestAsync(user1.Id, user2.Id);
        await _service.AcceptRequestAsync(sendResult.FriendshipId!.Value, user2.Id);

        // Act
        var searchResults = await _service.SearchUsersAsync(user1.Id, "User");

        // Assert
        var foundUser2 = searchResults.FirstOrDefault(u => u.UserId == user2.Id);
        foundUser2.Should().NotBeNull();
        foundUser2!.IsAlreadyFriend.Should().BeTrue("User2 should be marked as already a friend");
    }
}
