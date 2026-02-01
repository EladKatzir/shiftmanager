using System.ComponentModel.DataAnnotations;
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Phase 6: Daily notification digest preferences for employees
/// </summary>
public class DailyNotificationPreference : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy scoping
    public int CompanyId { get; set; }

    [Required]
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>
    /// Whether the user wants to receive daily digest emails
    /// </summary>
    public bool ReceiveDailyDigest { get; set; } = true;

    /// <summary>
    /// Preferred time to receive the daily digest (in UTC)
    /// Default: 08:00 UTC
    /// </summary>
    [Required]
    public TimeOnly PreferredTime { get; set; } = new TimeOnly(8, 0);

    /// <summary>
    /// Include upcoming shift assignments in digest
    /// </summary>
    public bool IncludeUpcomingShifts { get; set; } = true;

    /// <summary>
    /// Include pending time-off and swap requests in digest
    /// </summary>
    public bool IncludePendingRequests { get; set; } = true;

    /// <summary>
    /// Include assigned chores in digest
    /// </summary>
    public bool IncludeChores { get; set; } = true;

    /// <summary>
    /// Include on-duty assignments in digest
    /// </summary>
    public bool IncludeOnDuty { get; set; } = true;

    /// <summary>
    /// Send reminder 1 day before user's own shift assignments
    /// </summary>
    public bool RemindBeforeShifts { get; set; } = false;

    /// <summary>
    /// Send reminder 1 day before user's own chore assignments
    /// </summary>
    public bool RemindBeforeChores { get; set; } = false;

    /// <summary>
    /// Send reminder 1 day before user's own on-duty assignments
    /// </summary>
    public bool RemindBeforeOnDuty { get; set; } = false;

    /// <summary>
    /// Soft delete flag
    /// </summary>
    public bool IsActive { get; set; } = true;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
