using ShiftManager.Models.Support;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// DTO for swap request responses
/// </summary>
public class SwapRequestDto
{
    public int Id { get; set; }
    public int FromAssignmentId { get; set; }
    public int? ToAssignmentId { get; set; }
    public int FromUserId { get; set; }
    public int? ToUserId { get; set; }
    public RequestStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedBy { get; set; }

    // Related entities (optional, included based on query)
    public SwapShiftAssignmentDto? FromAssignment { get; set; }
    public SwapShiftAssignmentDto? ToAssignment { get; set; }
    public UserDto? FromUser { get; set; }
    public UserDto? ToUser { get; set; }
    public UserDto? Reviewer { get; set; }
}

/// <summary>
/// DTO for creating a swap request
/// </summary>
public class CreateSwapRequestDto
{
    /// <summary>
    /// The assignment ID the user wants to swap away from
    /// </summary>
    public int FromAssignmentId { get; set; }

    /// <summary>
    /// Optional: The assignment ID the user wants to take instead
    /// </summary>
    public int? ToAssignmentId { get; set; }

    /// <summary>
    /// Optional: The target user to swap with (if not specified by assignment)
    /// </summary>
    public int? ToUserId { get; set; }

    /// <summary>
    /// Reason for the swap request
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// DTO for declining a swap request
/// </summary>
public class DeclineSwapRequestDto
{
    /// <summary>
    /// Reason for declining the swap request
    /// </summary>
    public string? DeclineReason { get; set; }
}

/// <summary>
/// DTO for shift assignment (used in swap responses with extended info)
/// </summary>
public class SwapShiftAssignmentDto
{
    public int Id { get; set; }
    public int ShiftInstanceId { get; set; }
    public int? UserId { get; set; }

    // Related shift instance info
    public SwapShiftInstanceDto? ShiftInstance { get; set; }
}

/// <summary>
/// DTO for shift instance (used in swap/assignment responses)
/// </summary>
public class SwapShiftInstanceDto
{
    public int Id { get; set; }
    public int ShiftTypeId { get; set; }
    public string WorkDate { get; set; } = string.Empty; // ISO 8601 date
    public int StaffingRequired { get; set; }

    // Related shift type info
    public SwapShiftTypeDto? ShiftType { get; set; }
}

/// <summary>
/// DTO for shift type (used in shift responses)
/// </summary>
public class SwapShiftTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty; // HH:mm format
    public string End { get; set; } = string.Empty; // HH:mm format
}
