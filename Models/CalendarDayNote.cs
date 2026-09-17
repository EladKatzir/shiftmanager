using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A free-text note attached to a whole calendar DAY (not a specific user) on a molecule's Shifts
/// calendar. Created via Quick Entry's "Add note to this day" option (shift-mode or user-mode).
///
/// Keyed to the MOLECULE, because that is what the Shifts calendar spans: its rows come from every
/// desk in the molecule, so every viewer of that calendar must see the same notes. (It was once keyed
/// to the writer's active desk, which hid a note from colleagues in other desks looking at the very
/// same calendar.) One editable note per (MoleculeId, Date), enforced by a filtered unique index and
/// by upsert semantics in CalendarDayNoteService.
///
/// Intentionally distinct from <see cref="CalendarTextEntry"/>, whose notes attach to a UserId; a
/// shift-type cell has no single user, so day notes need a user-agnostic home.
///
/// READ ONLY THROUGH CalendarDayNoteService. The inherited company query filter below is defence in
/// depth for the writer's desk, not the visibility boundary — a molecule note is legitimately
/// visible to viewers from other desks, which is exactly why the service bypasses that filter.
/// </summary>
public class CalendarDayNote : IBelongsToCompany
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    /// <summary>The desk the note was first written from — provenance only; it no longer scopes visibility.</summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// The molecule whose Shifts calendar this note annotates. NULL only for legacy rows the molecule
    /// backfill could not key — their desk had no molecule, or a newer note from another desk claimed
    /// the same day. Those rows are kept (never deleted) but are not shown on any calendar.
    /// </summary>
    public int? MoleculeId { get; set; }

    /// <summary>The user who ORIGINALLY wrote the note. Never changes on edit.</summary>
    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>The user who last changed the text; NULL if never edited or if that user was deleted.</summary>
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}
