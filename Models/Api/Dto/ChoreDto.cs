namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// DTO for chore responses
/// </summary>
public class ChoreDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Date { get; set; } = string.Empty; // ISO 8601 date (yyyy-MM-dd)
    public string Title { get; set; } = string.Empty;
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
/// DTO for creating a chore
/// </summary>
public class CreateChoreDto
{
    /// <summary>
    /// The user to assign this chore to
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Date of the chore (yyyy-MM-dd format)
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Short title/description of the chore
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional notes/details
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for updating a chore
/// </summary>
public class UpdateChoreDto
{
    /// <summary>
    /// New title for the chore
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// New notes for the chore
    /// </summary>
    public string? Notes { get; set; }
}
