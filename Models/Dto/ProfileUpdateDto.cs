using ShiftManager.Models.Support;

namespace ShiftManager.Models.Dto;

/// <summary>
/// Data transfer object for profile updates
/// </summary>
public class ProfileUpdateDto
{
    // Basic Info
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? PreferredName { get; set; }

    // Contact
    public string? Phone { get; set; }
    public string? City { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    // Professional
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public DateOnly? HireDate { get; set; }
    public string? Skills { get; set; }
    public string? Certifications { get; set; }

    // Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }

    // System
    public UserRole? Role { get; set; }
    public bool? IsActive { get; set; }
}
