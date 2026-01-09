using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Join table linking a MasterProgram to its constituent Programs.
/// Allows a MasterProgram to compose multiple Programs with custom ordering.
/// </summary>
public class MasterProgramItem
{
    public int Id { get; set; }

    [Required]
    public int MasterProgramId { get; set; }

    [Required]
    public int ProgramId { get; set; }

    /// <summary>
    /// Display order for UI. Lower numbers appear first.
    /// </summary>
    public int SortOrder { get; set; } = 0;

    // Navigation
    public MasterProgram MasterProgram { get; set; } = null!;
    public ShiftProgram Program { get; set; } = null!;
}
