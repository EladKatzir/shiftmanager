using System.Text.Json.Serialization;

namespace ShiftManager.Models.Api.Dto;

/// <summary>
/// Data Transfer Object for User API responses
/// </summary>
public class UserDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("preferredName")]
    public string? PreferredName { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("dateOfBirth")]
    public string? DateOfBirth { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("hireDate")]
    public string? HireDate { get; set; }

    [JsonPropertyName("skills")]
    public List<string>? Skills { get; set; }

    [JsonPropertyName("certifications")]
    public List<string>? Certifications { get; set; }

    [JsonPropertyName("emergencyContact")]
    public EmergencyContactDto? EmergencyContact { get; set; }

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("profileLastUpdated")]
    public string? ProfileLastUpdated { get; set; }

    /// <summary>
    /// Maps from AppUser entity to UserDto
    /// </summary>
    public static UserDto FromEntity(AppUser user, bool includeFullDetails = false)
    {
        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            Department = user.Department,
            JobTitle = user.JobTitle
        };

        if (includeFullDetails)
        {
            dto.PreferredName = user.PreferredName;
            dto.Phone = user.Phone;
            dto.City = user.City;
            dto.DateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd");
            dto.HireDate = user.HireDate?.ToString("yyyy-MM-dd");
            dto.ProfileLastUpdated = user.ProfileLastUpdated?.ToString("O");

            // Parse JSON arrays
            if (!string.IsNullOrEmpty(user.Skills))
            {
                try
                {
                    dto.Skills = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.Skills);
                }
                catch { dto.Skills = null; }
            }

            if (!string.IsNullOrEmpty(user.Certifications))
            {
                try
                {
                    dto.Certifications = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.Certifications);
                }
                catch { dto.Certifications = null; }
            }

            // Emergency contact
            if (!string.IsNullOrEmpty(user.EmergencyContactName))
            {
                dto.EmergencyContact = new EmergencyContactDto
                {
                    Name = user.EmergencyContactName,
                    Phone = user.EmergencyContactPhone ?? string.Empty,
                    Relation = user.EmergencyContactRelation ?? string.Empty
                };
            }

            // Avatar URL
            if (!string.IsNullOrEmpty(user.AvatarFileName))
            {
                dto.AvatarUrl = $"/avatars/{user.CompanyId}/{user.AvatarFileName}";
            }
        }

        return dto;
    }
}

public class EmergencyContactDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;
}

/// <summary>
/// Paginated response wrapper
/// </summary>
public class PaginatedResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    [JsonPropertyName("pagination")]
    public PaginationInfo Pagination { get; set; } = new();
}

public class PaginationInfo
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
