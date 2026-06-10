using System.ComponentModel.DataAnnotations;
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Per-user, per-category email mute (notifications overhaul Phase 2). Presence of an active row
/// means the user has muted EMAIL for that notification category; in-app notifications are still
/// always persisted. Security-critical events ignore mutes entirely (see the dispatcher gate).
///
/// <para>
/// <see cref="Category"/> stores the integer value of the <c>NotificationCategory</c> enum
/// (which lives in the Services layer). It is stored as a plain int here so the Models layer
/// carries no dependency on Services; the notification gate maps it back to the enum.
/// </para>
/// </summary>
public class NotificationCategoryMute : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy scoping
    public int CompanyId { get; set; }

    [Required]
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>
    /// Integer value of the muted NotificationCategory (Services-layer enum). Stored as int to
    /// keep Models free of a Services dependency.
    /// </summary>
    [Required]
    public int Category { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
