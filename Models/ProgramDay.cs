using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Represents a single day in a Program's weekly mask.
/// If this record exists, the Program runs on this day of the week.
/// </summary>
public class ProgramDay
{
    public int Id { get; set; }

    [Required]
    public int ProgramId { get; set; }

    [Required]
    public DayOfWeek DayOfWeek { get; set; } // 0=Sunday, 6=Saturday (C# enum)

    /// <summary>
    /// Per-day staffing override. Null means use Program's DefaultStaffingRequired.
    /// </summary>
    [Range(1, 50)]
    public int? StaffingRequired { get; set; }

    // Navigation
    public ShiftProgram Program { get; set; } = null!;
}
