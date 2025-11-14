namespace ShiftManager.Models;

/// <summary>
/// Feedback type enumeration
/// </summary>
public enum FeedbackType
{
    Error = 0,
    Suggestion = 1
}

/// <summary>
/// Feedback status for tracking
/// </summary>
public enum FeedbackStatus
{
    New = 0,
    ToWorkOn = 1
}

/// <summary>
/// User feedback submission model
/// </summary>
public class Feedback : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// The user who submitted the feedback
    /// </summary>
    public int SubmittedBy { get; set; }

    /// <summary>
    /// Navigation property to the submitter
    /// </summary>
    public AppUser? Submitter { get; set; }

    /// <summary>
    /// Type of feedback (Error or Suggestion)
    /// </summary>
    public FeedbackType Type { get; set; }

    /// <summary>
    /// Feedback content/message
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional image filename (screenshot, etc.)
    /// </summary>
    public string? ImageFileName { get; set; }

    /// <summary>
    /// Status of the feedback (New or ToWorkOn)
    /// </summary>
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

    /// <summary>
    /// When the feedback was submitted
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the status was last updated
    /// </summary>
    public DateTime? StatusUpdatedAt { get; set; }

    /// <summary>
    /// Who updated the status (usually the owner)
    /// </summary>
    public int? StatusUpdatedBy { get; set; }

    /// <summary>
    /// Navigation property to the user who updated status
    /// </summary>
    public AppUser? StatusUpdater { get; set; }
}
