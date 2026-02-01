using System.Text.Json.Serialization;
using ShiftManager.Models;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// Data Transfer Object for Shift Instance API responses
/// </summary>
public class ShiftDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("shiftTypeId")]
    public int ShiftTypeId { get; set; }

    [JsonPropertyName("shiftTypeName")]
    public string ShiftTypeName { get; set; } = string.Empty;

    [JsonPropertyName("shiftTypeKey")]
    public string ShiftTypeKey { get; set; } = string.Empty;

    [JsonPropertyName("workDate")]
    public string WorkDate { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("staffingRequired")]
    public int StaffingRequired { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Maps from ShiftInstance entity to ShiftDto
    /// </summary>
    public static ShiftDto FromEntity(ShiftInstance shift)
    {
        var dto = new ShiftDto
        {
            Id = shift.Id,
            ShiftTypeId = shift.ShiftTypeId,
            ShiftTypeName = shift.ShiftType?.Name ?? string.Empty,
            ShiftTypeKey = shift.ShiftType?.Key ?? string.Empty,
            WorkDate = shift.WorkDate.ToString("yyyy-MM-dd"),
            StartTime = shift.ShiftType?.Start.ToString("HH:mm") ?? string.Empty,
            EndTime = shift.ShiftType?.End.ToString("HH:mm") ?? string.Empty,
            StaffingRequired = shift.StaffingRequired,
            Name = shift.Name
        };

        return dto;
    }
}

/// <summary>
/// Data Transfer Object for Shift Assignment API responses
/// </summary>
public class ShiftAssignmentDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("userEmail")]
    public string? UserEmail { get; set; }

    [JsonPropertyName("isUnassigned")]
    public bool IsUnassigned { get; set; }

    /// <summary>
    /// Maps from ShiftAssignment entity to ShiftAssignmentDto
    /// </summary>
    public static ShiftAssignmentDto FromEntity(ShiftAssignment assignment)
    {
        return new ShiftAssignmentDto
        {
            Id = assignment.Id,
            UserId = assignment.UserId,
            UserName = assignment.User?.DisplayName,
            UserEmail = assignment.User?.Email,
            IsUnassigned = assignment.UserId == null
        };
    }
}
