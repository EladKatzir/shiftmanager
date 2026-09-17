using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>A day note as a calendar renders it.</summary>
/// <param name="Text">The note text.</param>
/// <param name="AuthorName">Display name of the user who originally wrote the note.</param>
/// <param name="LastEditorName">Display name of the last user to change the text, when that is
/// someone other than the author; otherwise null.</param>
public sealed record DayNoteView(string Text, string AuthorName, string? LastEditorName);

/// <summary>
/// Manages day-scoped free-text notes (<see cref="CalendarDayNote"/>) — one editable note per
/// (MoleculeId, Date), shown to everyone viewing that molecule's Shifts calendar.
///
/// All methods take an explicit moleculeId and bypass the ambient company query filter (a molecule
/// note is legitimately visible to viewers from other desks). Authorization — whether the caller may
/// view the molecule, and may write notes at all — is enforced by the callers: the Shifts page and
/// the QuickAddDayNote endpoint, both through <see cref="ShiftCalendarAccess"/>.
/// </summary>
public interface ICalendarDayNoteService
{
    /// <summary>Upsert the note for (date, moleculeId). A new note records <paramref name="authorCompanyId"/>
    /// and <paramref name="userId"/> as its origin; an edit keeps them and records the editor.</summary>
    Task<CalendarDayNote> SetDayNoteAsync(DateOnly date, int moleculeId, int authorCompanyId, string text, int userId);

    /// <summary>Delete the note for (date, moleculeId). Returns true if a note existed and was removed.</summary>
    Task<bool> DeleteDayNoteAsync(DateOnly date, int moleculeId);

    /// <summary>Bulk-load a molecule's day notes within an inclusive date range, keyed by date.</summary>
    Task<Dictionary<DateOnly, DayNoteView>> GetDayNotesForMoleculeAsync(int moleculeId, DateOnly start, DateOnly end);
}
