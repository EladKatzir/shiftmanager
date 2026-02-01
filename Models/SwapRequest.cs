using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Represents a request to swap shift assignments between users
/// </summary>
public class SwapRequest : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy Phase 1: Tenant scoping
    public int CompanyId { get; set; }

    /// <summary>
    /// The assignment the requester wants to give away
    /// </summary>
    public int FromAssignmentId { get; set; }

    /// <summary>
    /// Optional: The assignment the requester wants to take
    /// </summary>
    public int? ToAssignmentId { get; set; }

    /// <summary>
    /// The user requesting the swap
    /// </summary>
    public int FromUserId { get; set; }

    /// <summary>
    /// The target user to swap with (optional if ToAssignmentId is specified)
    /// </summary>
    public int? ToUserId { get; set; }

    /// <summary>
    /// Status of the swap request
    /// </summary>
    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    /// <summary>
    /// Reason for the swap request
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Reason for declining (if declined)
    /// </summary>
    public string? DeclineReason { get; set; }

    /// <summary>
    /// When the swap request was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the swap request was reviewed (approved/declined)
    /// </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Who reviewed the swap request
    /// </summary>
    public int? ReviewedBy { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public ShiftAssignment? FromAssignment { get; set; }
    public ShiftAssignment? ToAssignment { get; set; }
    public AppUser? FromUser { get; set; }
    public AppUser? ToUser { get; set; }
    public AppUser? Reviewer { get; set; }
}
