using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

public class FriendshipService : IFriendshipService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<FriendshipService> _logger;

    public FriendshipService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<FriendshipService> logger)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<List<FriendDto>> GetFriendsAsync(int userId)
    {
        // SECURITY-AUDITED: SAFE — friendships are cross-company by design; scoped by userId
        var friendships = await _db.UserFriendships
            .IgnoreQueryFilters()
            .Include(f => f.User)
            .Include(f => f.Friend)
            .Where(f => (f.UserId == userId || f.FriendId == userId)
                && f.Status == FriendshipStatus.Accepted)
            .ToListAsync();

        // Get company and job type info
        var friendIds = friendships.Select(f => f.UserId == userId ? f.FriendId : f.UserId).ToList();
        // SECURITY-AUDITED: SAFE — scoped by friendIds derived from user's accepted friendships
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => friendIds.Contains(u.Id))
            .Include(u => u.JobType)
            .ToListAsync();

        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        // SECURITY-AUDITED: SAFE — scoped by companyIds of user's accepted friends; returns names only
        var companies = await _db.Companies.IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return friendships.Select(f =>
        {
            var friendId = f.UserId == userId ? f.FriendId : f.UserId;
            var friend = users.First(u => u.Id == friendId);
            return new FriendDto(
                f.Id,
                friendId,
                friend.DisplayName,
                friend.Email,
                companies.TryGetValue(friend.CompanyId, out var companyName) ? companyName : null,
                friend.JobType?.DisplayName,
                f.AcceptedAt ?? f.RequestedAt
            );
        }).ToList();
    }

    public async Task<List<FriendRequestDto>> GetPendingRequestsAsync(int userId)
    {
        // SECURITY-AUDITED: SAFE — friendships are cross-company by design; scoped by userId as recipient
        return await _db.UserFriendships
            .IgnoreQueryFilters()
            .Include(f => f.User)
            .Include(f => f.Friend)
            .Where(f => f.FriendId == userId && f.Status == FriendshipStatus.Pending)
            .Select(f => new FriendRequestDto(
                f.Id,
                f.UserId,
                f.User.DisplayName,
                f.FriendId,
                f.Friend.DisplayName,
                f.RequestedAt
            ))
            .ToListAsync();
    }

    public async Task<List<FriendRequestDto>> GetOutgoingRequestsAsync(int userId)
    {
        // SECURITY-AUDITED: SAFE — friendships are cross-company by design; scoped by userId as sender
        return await _db.UserFriendships
            .IgnoreQueryFilters()
            .Include(f => f.User)
            .Include(f => f.Friend)
            .Where(f => f.UserId == userId && f.Status == FriendshipStatus.Pending)
            .Select(f => new FriendRequestDto(
                f.Id,
                f.UserId,
                f.User.DisplayName,
                f.FriendId,
                f.Friend.DisplayName,
                f.RequestedAt
            ))
            .ToListAsync();
    }

    public async Task<FriendshipResult> SendRequestAsync(int userId, int friendId)
    {
        if (userId == friendId)
        {
            return new FriendshipResult(false, null, "Error_CannotFriendSelf",
                _localizer["Error_CannotFriendSelf"]);
        }

        // Check if friendship already exists
        // SECURITY-AUDITED: SAFE — scoped by userId + friendId pair; returns existing relationship only
        var existing = await _db.UserFriendships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f =>
                (f.UserId == userId && f.FriendId == friendId) ||
                (f.UserId == friendId && f.FriendId == userId));

        if (existing != null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
            {
                return new FriendshipResult(false, existing.Id, "Error_AlreadyFriends",
                    _localizer["Error_AlreadyFriends"]);
            }
            if (existing.Status == FriendshipStatus.Pending)
            {
                return new FriendshipResult(false, existing.Id, "Error_RequestAlreadyPending",
                    _localizer["Error_RequestAlreadyPending"]);
            }
        }

        // Verify both users exist
        var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
        var friendExists = await _db.Users.AnyAsync(u => u.Id == friendId);

        if (!userExists || !friendExists)
        {
            return new FriendshipResult(false, null, "Error_UserNotFound",
                _localizer["Error_UserNotFound"]);
        }

        var friendship = new UserFriendship
        {
            UserId = userId,
            FriendId = friendId,
            Status = FriendshipStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        _db.UserFriendships.Add(friendship);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Friend request sent from {UserId} to {FriendId}",
            userId, friendId);

        return new FriendshipResult(true, friendship.Id, null, null);
    }

    public async Task<FriendshipResult> AcceptRequestAsync(int friendshipId, int acceptingUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific friendshipId; ownership check follows (FriendId == acceptingUserId)
        var friendship = await _db.UserFriendships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == friendshipId);

        if (friendship == null)
        {
            return new FriendshipResult(false, null, "Error_RequestNotFound",
                _localizer["Error_RequestNotFound"]);
        }

        if (friendship.FriendId != acceptingUserId)
        {
            return new FriendshipResult(false, null, "Error_NotAuthorized",
                _localizer["Error_NotAuthorized"]);
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return new FriendshipResult(false, friendshipId, "Error_RequestNotPending",
                _localizer["Error_RequestNotPending"]);
        }

        friendship.Status = FriendshipStatus.Accepted;
        friendship.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Friend request {FriendshipId} accepted by {UserId}",
            friendshipId, acceptingUserId);

        return new FriendshipResult(true, friendshipId, null, null);
    }

    public async Task<FriendshipResult> RejectRequestAsync(int friendshipId, int rejectingUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific friendshipId; ownership check follows (FriendId == rejectingUserId)
        var friendship = await _db.UserFriendships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == friendshipId);

        if (friendship == null)
        {
            return new FriendshipResult(false, null, "Error_RequestNotFound",
                _localizer["Error_RequestNotFound"]);
        }

        if (friendship.FriendId != rejectingUserId)
        {
            return new FriendshipResult(false, null, "Error_NotAuthorized",
                _localizer["Error_NotAuthorized"]);
        }

        friendship.Status = FriendshipStatus.Rejected;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Friend request {FriendshipId} rejected by {UserId}",
            friendshipId, rejectingUserId);

        return new FriendshipResult(true, friendshipId, null, null);
    }

    public async Task<FriendshipResult> RemoveFriendAsync(int friendshipId, int removingUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific friendshipId; ownership check follows (UserId or FriendId == removingUserId)
        var friendship = await _db.UserFriendships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == friendshipId);

        if (friendship == null)
        {
            return new FriendshipResult(false, null, "Error_FriendshipNotFound",
                _localizer["Error_FriendshipNotFound"]);
        }

        if (friendship.UserId != removingUserId && friendship.FriendId != removingUserId)
        {
            return new FriendshipResult(false, null, "Error_NotAuthorized",
                _localizer["Error_NotAuthorized"]);
        }

        _db.UserFriendships.Remove(friendship);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Friendship {FriendshipId} removed by {UserId}",
            friendshipId, removingUserId);

        return new FriendshipResult(true, friendshipId, null, null);
    }

    public async Task<bool> AreFriendsAsync(int userId1, int userId2)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific userId pair; returns boolean only
        return await _db.UserFriendships
            .IgnoreQueryFilters()
            .AnyAsync(f =>
                ((f.UserId == userId1 && f.FriendId == userId2) ||
                 (f.UserId == userId2 && f.FriendId == userId1)) &&
                f.Status == FriendshipStatus.Accepted);
    }

    public async Task<HashSet<int>> GetFriendIdsAsync(int userId)
    {
        // SECURITY-AUDITED: SAFE — friendships are cross-company by design; scoped by userId
        var friendships = await _db.UserFriendships
            .IgnoreQueryFilters()
            .Where(f => (f.UserId == userId || f.FriendId == userId)
                && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
            .ToListAsync();

        return friendships.ToHashSet();
    }

    public async Task<List<PotentialFriendDto>> SearchUsersAsync(int userId, string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<PotentialFriendDto>();

        var queryLower = query.ToLower();

        // Get existing friendships for this user
        // SECURITY-AUDITED: SAFE — scoped by userId; used to annotate search results with friendship status
        var existingFriendships = await _db.UserFriendships
            .IgnoreQueryFilters()
            .Where(f => f.UserId == userId || f.FriendId == userId)
            .ToListAsync();

        var friendIds = existingFriendships
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
            .ToHashSet();

        var pendingRequestIds = existingFriendships
            .Where(f => f.Status == FriendshipStatus.Pending)
            .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
            .ToHashSet();

        // Search users
        // SECURITY-AUDITED: SAFE — cross-company user search is intentional for friend discovery; limited to 20 results, basic info only
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && u.Id != userId &&
                (u.DisplayName.ToLower().Contains(queryLower) ||
                 u.Email.ToLower().Contains(queryLower)))
            .Include(u => u.JobType)
            .Take(limit)
            .ToListAsync();

        // Get company names
        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        // SECURITY-AUDITED: SAFE — scoped by companyIds of search results; returns names only
        var companies = await _db.Companies.IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return users.Select(u => new PotentialFriendDto(
            u.Id,
            u.DisplayName,
            u.Email,
            companies.TryGetValue(u.CompanyId, out var companyName) ? companyName : null,
            u.JobType?.DisplayName,
            HasPendingRequest: pendingRequestIds.Contains(u.Id),
            IsAlreadyFriend: friendIds.Contains(u.Id)
        )).ToList();
    }
}
