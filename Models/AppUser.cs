using ShiftManager.Models.Support;

namespace ShiftManager.Models;

// SECURITY FIX: Implement IBelongsToCompany so CompanyIdInterceptor auto-sets CompanyId
// A-11/E-05: WARNING — Changing a user's CompanyId will orphan related records
// (ShiftAssignments, TimeOffRequests, OnDuty, Chores) behind EF query filters.
// If company transfer is implemented, those records must be archived/migrated first.
public class AppUser : IBelongsToCompany
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
    public string? LegacyDepartment { get; set; }   // DEPRECATED: Use DepartmentId/Department navigation. e.g., "Kitchen", "Front of House", "Management"
    public string? JobTitle { get; set; }           // e.g., "Line Cook", "Server", "Shift Manager"
    public DateOnly? HireDate { get; set; }         // When they started
    public string? Skills { get; set; }             // JSON array, e.g., ["Grill", "Prep", "Cleaning"]
    public string? Certifications { get; set; }     // JSON array, e.g., ["Food Safety", "First Aid"]

    // Military Rank (for eligibility checks)
    /// <summary>
    /// User's military rank. Used for duty eligibility (e.g., Katzin requires officer rank).
    /// Defaults to Turai (lowest enlisted rank) for backward compatibility.
    /// </summary>
    public MilitaryRank Rank { get; set; } = MilitaryRank.Turai;

    // Profile Enhancements - Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; } // e.g., "Spouse", "Parent", "Friend"

    // Profile Enhancements - Avatar & Metadata
    public string? AvatarFileName { get; set; }     // Filename in wwwroot/avatars/{companyId}/
    public DateTime? ProfileLastUpdated { get; set; }
    public int? ProfileLastUpdatedBy { get; set; }  // User ID who made the change

    // Security - Account Lockout Protection
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginAttempt { get; set; }

    // A-07: Force password change after temp password reset
    public bool MustChangePassword { get; set; } = false;

    // B-05: Onboarding wizard — shows first-login walkthrough for new users
    public bool HasCompletedOnboarding { get; set; } = false;

    // Organizational - workforce users have JobType, tech users have Department
    public int? JobTypeId { get; set; }    // Workforce molecules only
    public int? DepartmentId { get; set; }  // Tech molecules only

    // Dynamic Role Template — source of truth for user's role
    /// <summary>
    /// User's primary role template. When set, AppUser.Role is auto-derived from RoleTemplate.DerivedUserRole.
    /// </summary>
    public int? RoleTemplateId { get; set; }

    /// <summary>
    /// Which shift type this user primarily operates (for calendar grouping).
    /// NULL = user doesn't do shifts → appears in company group.
    /// Points to a ShiftType which may belong to a different company (cross-tenant FK via IgnoreQueryFilters).
    /// </summary>
    public int? PrimaryShiftTypeId { get; set; }

    /// <summary>
    /// User's assigned home rotation type. Single source of truth for home type assignment.
    /// NULL = no rotation assigned.
    /// </summary>
    public int? HomeTypeId { get; set; }

    // Navigation
    public JobType? JobType { get; set; }
    public Department? Department { get; set; }
    public RoleTemplate? RoleTemplate { get; set; }
    /// <summary>
    /// SECURITY-AUDITED: Cross-tenant FK — PrimaryShiftType may belong to a different company.
    /// Load via IgnoreQueryFilters() when needed.
    /// </summary>
    public ShiftType? PrimaryShiftType { get; set; }
    public HomeType? HomeType { get; set; }
}
