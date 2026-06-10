using System.ComponentModel.DataAnnotations;
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Per-user secret token for the self-hosted iCalendar subscription feed
/// (<c>/calendar/feed/{token}.ics</c>) — notifications overhaul Phase 4. The user adds the feed
/// URL to Outlook once and all their shifts/chores/on-duty auto-sync, staying correct over time
/// (the self-healing safety net behind Felix's fire-and-forget per-event invites).
///
/// <para>
/// <see cref="LastPolledAt"/> is stamped on every feed fetch and drives subscription-aware routing
/// (Phase 5): when a user is actively polling the feed, the per-event Felix calendar push is
/// suppressed to avoid a duplicate calendar entry.
/// </para>
/// </summary>
public class CalendarFeedToken : IBelongsToCompany
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [Required]
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Opaque URL-safe secret embedded in the feed URL. Unique; revoked by regenerating.</summary>
    [Required]
    [StringLength(64)]
    public string Token { get; set; } = "";

    /// <summary>When Outlook (or any client) last fetched this feed. Null = never polled.</summary>
    public DateTime? LastPolledAt { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
