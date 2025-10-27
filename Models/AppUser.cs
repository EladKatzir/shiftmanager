using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class AppUser
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;
    public bool IsActive { get; set; } = true;

    // Local password auth (no external integrations)
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();

    // Profile Enhancements - Personal Information
    public string? PreferredName { get; set; }      // What they prefer to be called
    public string? Phone { get; set; }              // Mobile phone
    public string? City { get; set; }               // City of residence
    public DateOnly? DateOfBirth { get; set; }      // For age verification, birthday greetings

    // Profile Enhancements - Professional Information
    public string? Department { get; set; }         // e.g., "Kitchen", "Front of House", "Management"
    public string? JobTitle { get; set; }           // e.g., "Line Cook", "Server", "Shift Manager"
    public DateOnly? HireDate { get; set; }         // When they started
    public string? Skills { get; set; }             // JSON array, e.g., ["Grill", "Prep", "Cleaning"]
    public string? Certifications { get; set; }     // JSON array, e.g., ["Food Safety", "First Aid"]

    // Profile Enhancements - Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; } // e.g., "Spouse", "Parent", "Friend"

    // Profile Enhancements - Avatar & Metadata
    public string? AvatarFileName { get; set; }     // Filename in wwwroot/avatars/{companyId}/
    public DateTime? ProfileLastUpdated { get; set; }
    public int? ProfileLastUpdatedBy { get; set; }  // User ID who made the change
}
