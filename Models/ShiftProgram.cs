using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Represents a weekly template for generating shift instances.
/// A Program defines WHEN a specific shift type runs (weekly mask) and DEFAULT staffing.
/// </summary>
public class ShiftProgram
{
    public int Id { get; set; }

    [Required]
    public int CompanyId { get; set; }

    [Required]
    public int ShiftTypeId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Range(1, 50)]
    public int DefaultStaffingRequired { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public int CreatedBy { get; set; }

    [Required]
    public int UpdatedBy { get; set; }

    // Navigation properties
    public ShiftType ShiftType { get; set; } = null!;
    public List<ProgramDay> ProgramDays { get; set; } = new();
}
