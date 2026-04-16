namespace ShiftManager.Models;

/// <summary>
/// Per-Area override of the unified calendar color palette (2026-04-15).
/// One row per Area (unique AreaId). Every slot is nullable — a null slot
/// means "use the global default from tokens.css for this color".
///
/// Not <c>IBelongsToCompany</c> because Area lives ABOVE Company in the
/// hierarchy (Project -> Area -> Molecule -> Company). One palette applies
/// to every molecule/company inside the area. Write authorization is via
/// the area-scoped grant <c>EditAreaCalendarPalette</c>.
///
/// All hex values must pass <c>ColorUtilities.SanitizeHexColor</c> (strict
/// <c>#RRGGBB</c>) before being persisted — prevents CSS injection when
/// rendered into an inline <c>&lt;style&gt;</c> block.
/// </summary>
public class AreaCalendarPalette
{
    public int Id { get; set; }

    /// <summary>FK to Area. Unique (one palette per area).</summary>
    public int AreaId { get; set; }

    public string? ShiftMorning { get; set; }
    public string? ShiftAfternoon { get; set; }
    public string? ShiftNight { get; set; }
    public string? ShiftHome { get; set; }
    public string? OnDuty { get; set; }
    public string? Chore { get; set; }
    public string? Vacation { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Area Area { get; set; } = null!;
}
