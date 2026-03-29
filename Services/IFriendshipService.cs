using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IFriendshipService
{
    /// <summary>
    /// Gets all friends for a user (accepted friendships).
    /// </summary>
    Task<List<FriendDto>> GetFriendsAsync(int userId);

    /// <summary>
    /// Gets pending friend requests for a user (incoming).
    /// </summary>
    Task<List<FriendRequestDto>> GetPendingRequestsAsync(int userId);

    /// <summary>
    /// Gets outgoing friend requests that haven't been accepted yet.
    /// </summary>
    Task<List<FriendRequestDto>> GetOutgoingRequestsAsync(int userId);

    /// <summary>
    /// Sends a friend request from one user to another.
    /// </summary>
    Task<FriendshipResult> SendRequestAsync(int userId, int friendId);

    /// <summary>
    /// Accepts a pending friend request.
    /// </summary>
    Task<FriendshipResult> AcceptRequestAsync(int friendshipId, int acceptingUserId);

    /// <summary>
    /// Rejects a pending friend request.
    /// </summary>
    Task<FriendshipResult> RejectRequestAsync(int friendshipId, int rejectingUserId);

    /// <summary>
    /// Removes an existing friendship.
    /// </summary>
    Task<FriendshipResult> RemoveFriendAsync(int friendshipId, int removingUserId);

    /// <summary>
    /// Checks if two users are friends.
    /// </summary>
    Task<bool> AreFriendsAsync(int userId1, int userId2);

    /// <summary>
    /// Gets friend IDs for a user (for filtering calendar items).
    /// </summary>
    Task<HashSet<int>> GetFriendIdsAsync(int userId);

    /// <summary>
    /// Searches for users that can be added as friends.
    /// </summary>
    Task<List<PotentialFriendDto>> SearchUsersAsync(int userId, string query, int limit = 20);
}

public record FriendDto(
    int FriendshipId,
    int FriendId,
    string DisplayName,
    string? Email,
    string? CompanyName,
    string? JobTypeName,
    DateTime FriendsSince,
    string? AvatarUrl = null
);

public record FriendRequestDto(
    int FriendshipId,
    int FromUserId,
    string FromUserName,
    int ToUserId,
    string ToUserName,
    DateTime RequestedAt
);

public record PotentialFriendDto(
    int UserId,
    string DisplayName,
    string? Email,
    string? CompanyName,
    string? JobTypeName,
    bool HasPendingRequest,
    bool IsAlreadyFriend,
    string? AvatarUrl = null
);

public record FriendshipResult(
    bool Success,
    int? FriendshipId,
    string? ErrorKey,
    string? ErrorMessage
);
