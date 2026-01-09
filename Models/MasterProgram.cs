using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Represents a collection of Programs that form a complete weekly schedule.
/// A MasterProgram applies multiple shift types across the week in one action.
/// </summary>
public class MasterProgram
{
    public int Id { get; set; }

    [Required]
    public int CompanyId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public int CreatedBy { get; set; }

    [Required]
    public int UpdatedBy { get; set; }

    // Navigation
    public List<MasterProgramItem> Items { get; set; } = new();
}
