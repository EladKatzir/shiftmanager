using ShiftManager.Models.Support;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// DTO for on-duty responses
/// </summary>
public class OnDutyDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Date { get; set; } = string.Empty; // ISO 8601 date (yyyy-MM-dd)
    public string Type { get; set; } = string.Empty; // "Hakam" or "Lead"
    public string? Notes { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public int? CanceledBy { get; set; }
    public bool IsActive { get; set; }

    // Related entities (optional, included based on query)
    public UserDto? User { get; set; }
    public UserDto? Creator { get; set; }
    public UserDto? Canceler { get; set; }
}

/// <summary>
/// DTO for creating an on-duty assignment
/// </summary>
public class CreateOnDutyDto
{
    /// <summary>
    /// The user to assign on-duty
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Date of the on-duty assignment (yyyy-MM-dd format)
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Type of on-duty: "Hakam" or "Lead"
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Optional notes about this assignment
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for updating an on-duty assignment
/// </summary>
public class UpdateOnDutyDto
{
    /// <summary>
    /// New notes for the on-duty assignment
    /// </summary>
    public string? Notes { get; set; }
}
