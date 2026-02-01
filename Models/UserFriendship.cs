using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class UserFriendship
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FriendId { get; set; }
    public FriendshipStatus Status { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public AppUser Friend { get; set; } = null!;
}
