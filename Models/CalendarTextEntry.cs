using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

public class CalendarTextEntry : IBelongsToCompany
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }

    [MaxLength(200)]
    public string Text { get; set; } = string.Empty;

    public int CompanyId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
