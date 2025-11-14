using ShiftManager.Models.Support;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// DTO for feedback responses
/// </summary>
public class FeedbackDto
{
    public int Id { get; set; }
    public int SubmittedBy { get; set; }
    public string Type { get; set; } = string.Empty; // "Error" or "Suggestion"
    public string Status { get; set; } = string.Empty; // "New" or "ToWorkOn"
    public string Content { get; set; } = string.Empty;
    public string? ImageFileName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StatusUpdatedAt { get; set; }
    public int? StatusUpdatedBy { get; set; }

    // Related entities (optional, included based on query)
    public UserDto? Submitter { get; set; }
    public UserDto? StatusUpdater { get; set; }
}

/// <summary>
/// DTO for creating feedback
/// </summary>
public class CreateFeedbackDto
{
    /// <summary>
    /// Type of feedback: "Error" or "Suggestion"
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The feedback content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional image file name (must be uploaded separately)
    /// </summary>
    public string? ImageFileName { get; set; }
}

/// <summary>
/// DTO for updating feedback status
/// </summary>
public class UpdateFeedbackStatusDto
{
    /// <summary>
    /// New status: "New" or "ToWorkOn"
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
