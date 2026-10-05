using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// The identity of one Shifts calendar — exactly the triple
/// <c>Pages/Calendar/Shifts.cshtml.cs</c> resolves for each request.
/// </summary>
/// <param name="MoleculeId">The molecule whose calendar is on screen.</param>
/// <param name="JobTypeId">The calendar's job type, or null for a Tech molecule (whose calendar has
/// none). Null is a real value here, never "unset".</param>
/// <param name="TabId">The active tab, or null for the synthetic "All" view. Null is a real value
/// here too: All has no <see cref="ShiftTab"/> row.</param>
public sealed record CalendarScope(int MoleculeId, int? JobTypeId, int? TabId);

/// <summary>A day note as a calendar renders it.</summary>
/// <param name="Id">Needed by the delete affordance, which deletes one note by id.</param>
/// <param name="Text">The note text.</param>
/// <param name="AuthorName">Display name of the user who wrote it. **Never null** — when the author
/// has been deleted this carries the resx KEY <c>Calendar_DayNote_UnknownAuthor</c> for the view to
/// localize, so no view has to branch on a missing author.</param>
/// <param name="CreatedByUserId">Null once the author is deleted. Exposed because authorship is also
/// a delete permission, so the view needs it to decide whether to render the × at all.</param>
/// <param name="TabId">Null for a note that belongs to the "All" view rather than a real tab.</param>
/// <param name="TabNameEn">English tab name, or null when the note is All-scoped.</param>
/// <param name="TabNameHe">Hebrew tab name, or null when the note is All-scoped. Both names travel
/// because this service has no localizer — resolving to one string here would give the Hebrew UI
/// English badges.</param>
/// <param name="TabIsActive">False when the note's tab has been deactivated. Such a tab drops out of
/// the strip, so its notes are reachable only from All; the badge says so rather than hiding them.</param>
/// <param name="TabColor">The tab's optional hex colour, so the inline dot matches that tab's pill in
/// the strip and the two read as the same object. Null when the tab has no colour, or is absent.</param>
public sealed record DayNoteView(
    int Id,
    string Text,
    string AuthorName,
    int? CreatedByUserId,
    int? TabId,
    string? TabNameEn,
    string? TabNameHe,
    bool TabIsActive,
    string? TabColor);

/// <summary>
/// Manages day-scoped free-text notes (<see cref="CalendarDayNote"/>). A note belongs to one
/// CALENDAR — see <see cref="CalendarScope"/> — and a day may hold many of them.
///
/// All methods take the scope explicitly and bypass the ambient company query filter, because a
/// calendar note is legitimately visible to viewers from every desk in the molecule. Authorization —
/// whether the caller may view the calendar at all, and whether they may write or delete — is
/// enforced by the callers: the Shifts page, <c>QuickAddDayNote</c> and <c>DeleteDayNote</c>, all
/// through <see cref="ShiftCalendarAccess"/>.
/// </summary>
public interface ICalendarDayNoteService
{
    /// <summary>
    /// Insert a new note on <paramref name="scope"/>'s calendar. Always inserts — never upserts, so
    /// one writer can never silently replace another's note.
    ///
    /// <para><paramref name="Created"/> is false when an identical note from the same author on the
    /// same calendar and date arrived moments ago and this call was therefore treated as a
    /// double-submit: the EXISTING note comes back and nothing was written. Callers must honour it —
    /// writing an audit row or broadcasting a "created" event for a note you did not create is a
    /// lie about history.</para>
    /// </summary>
    Task<(CalendarDayNote Note, bool Created)> AddDayNoteAsync(DateOnly date, CalendarScope scope, int authorCompanyId, string text, int userId);

    /// <summary>
    /// Load one note by id, so a caller can authorize against its own scope and author before
    /// deleting it. Returns null when no such note exists.
    /// </summary>
    Task<CalendarDayNote?> GetByIdAsync(int noteId);

    /// <summary>Delete one note by id. Returns false when it did not exist (a double-click, say).</summary>
    Task<bool> DeleteDayNoteAsync(int noteId);

    /// <summary>
    /// Bulk-load a calendar's notes in an inclusive date range, keyed by date.
    ///
    /// <para><b><see cref="CalendarScope.TabId"/> null means the "All" union, not "notes with no
    /// tab".</b> Viewing All returns every note of that (molecule, jobType) calendar — including
    /// tab-scoped ones, badged — while a real tab returns only its own. A note written on All does
    /// not appear on a real tab.</para>
    /// </summary>
    Task<Dictionary<DateOnly, List<DayNoteView>>> GetDayNotesForCalendarAsync(CalendarScope scope, DateOnly start, DateOnly end);
}
