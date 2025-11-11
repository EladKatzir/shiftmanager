using System.Text.Json.Serialization;
using ShiftManager.Models;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// Data Transfer Object for Notification API responses
/// </summary>
public class NotificationDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("isRead")]
    public bool IsRead { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("readAt")]
    public string? ReadAt { get; set; }

    [JsonPropertyName("relatedEntityId")]
    public int? RelatedEntityId { get; set; }

    [JsonPropertyName("relatedEntityType")]
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// Maps from UserNotification entity to NotificationDto
    /// </summary>
    public static NotificationDto FromEntity(UserNotification notification)
    {
        return new NotificationDto
        {
            Id = notification.Id,
            UserId = notification.UserId,
            Type = notification.Type.ToString(),
            Title = notification.Title,
            Message = notification.Message,
            IsRead = notification.IsRead,
            CreatedAt = notification.CreatedAt.ToString("O"),
            ReadAt = notification.ReadAt?.ToString("O"),
            RelatedEntityId = notification.RelatedEntityId,
            RelatedEntityType = notification.RelatedEntityType
        };
    }
}
