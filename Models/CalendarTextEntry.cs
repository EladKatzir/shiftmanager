using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

public enum CalendarTextEntryType
{
    QuickEntry = 0,      // Created via Quick Entry on Shifts/Chores/OnCall
    OverviewNote = 1     // Created via Overview double-click modal
}

public class CalendarTextEntry : IBelongsToCompany
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    public int CompanyId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public CalendarTextEntryType EntryType { get; set; } = CalendarTextEntryType.QuickEntry;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
