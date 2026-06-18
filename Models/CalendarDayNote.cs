using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A free-text note attached to a whole calendar DAY (not a specific user), scoped to a company.
/// Created via Quick Entry's "Add note to this day" option, available in any calendar view
/// (shift-mode where rows are shift types, or user-mode where rows are workers).
///
/// One editable note per (Date, CompanyId) — uniqueness enforced by a DB unique index AND by
/// upsert semantics at the service level. This is intentionally distinct from
/// <see cref="CalendarTextEntry"/>, whose notes attach to a specific UserId; a shift-type cell
/// has no single user, so day notes need a user-agnostic home.
/// </summary>
public class CalendarDayNote : IBelongsToCompany
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    public int CompanyId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
