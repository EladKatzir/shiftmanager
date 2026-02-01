using System.Text.Json.Serialization;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// Data Transfer Object for Time-Off Request API responses
/// </summary>
public class TimeOffDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("userEmail")]
    public string? UserEmail { get; set; }

    [JsonPropertyName("startDate")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("endDate")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Maps from TimeOffRequest entity to TimeOffDto
    /// </summary>
    public static TimeOffDto FromEntity(TimeOffRequest request, AppUser? user = null)
    {
        return new TimeOffDto
        {
            Id = request.Id,
            UserId = request.UserId,
            UserName = user?.DisplayName,
            UserEmail = user?.Email,
            StartDate = request.StartDate.ToString("yyyy-MM-dd"),
            EndDate = request.EndDate.ToString("yyyy-MM-dd"),
            Reason = request.Reason,
            Status = request.Status.ToString(),
            CreatedAt = request.CreatedAt.ToString("O")
        };
    }
}
