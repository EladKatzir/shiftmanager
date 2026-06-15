namespace ShiftManager.Models;

/// <summary>
/// Many-to-many join between a user and the chore categories they participate in. Only meaningful
/// when AppUser.DoesChores = true. Mirrors <see cref="UserShiftCategory"/>.
/// </summary>
public class UserChoreCategory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ChoreCategoryId { get; set; }

    public AppUser User { get; set; } = null!;
    public ChoreCategory ChoreCategory { get; set; } = null!;
}
