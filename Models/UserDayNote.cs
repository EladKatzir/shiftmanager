namespace ShiftManager.Models;

public class UserDayNote : IBelongsToCompany
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int CompanyId { get; set; }
    public string Note { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
