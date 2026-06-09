namespace ShiftManager.Models;

/// <summary>
/// Many-to-many join between a user and the shift categories they participate in. A user with
/// AppUser.DoesShifts = true appears under EACH mapped category as a (mirrored) calendar row;
/// DoesShifts = false means the user appears under their company header instead. The many-to-many
/// shape is what lets one user belong to several categories at once (e.g. Yekev AND Hazon).
/// </summary>
public class UserShiftCategory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ShiftCategoryId { get; set; }

    public AppUser User { get; set; } = null!;
    public ShiftCategory ShiftCategory { get; set; } = null!;
}
