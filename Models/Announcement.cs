namespace ShiftManager.Models;

/// <summary>
/// Announcement visibility scope enumeration
/// </summary>
public enum AnnouncementScope
{
    All = 0,           // Visible to all employees in company
    Department = 1,    // Visible to specific department
    Role = 2           // Visible to specific role
}

/// <summary>
/// Company-wide announcement model for the announcements feed
/// </summary>
public class Announcement : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// Announcement title/headline
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Full announcement content (supports markdown)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Who created the announcement
    /// </summary>
    public int CreatedBy { get; set; }
    public AppUser? Creator { get; set; }

    /// <summary>
    /// When announcement was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optional expiration date (null = never expires)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Visibility scope
    /// </summary>
    public AnnouncementScope Scope { get; set; } = AnnouncementScope.All;

    /// <summary>
    /// Target department ID (when Scope = Department)
    /// </summary>
    public int? TargetDepartmentId { get; set; }
    public Department? TargetDepartment { get; set; }

    /// <summary>
    /// Target role (when Scope = Role)
    /// </summary>
    public string? TargetRole { get; set; }

    /// <summary>
    /// Whether announcement is pinned to top
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Whether announcement is active
    /// </summary>
    public bool IsActive { get; set; } = true;
}
