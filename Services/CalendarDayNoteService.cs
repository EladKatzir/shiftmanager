using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: Every method takes an explicit CalendarScope and uses IgnoreQueryFilters(). That
// is required, not incidental: a day note is keyed to a CALENDAR and must be visible to viewers from
// every desk in the molecule, while the entity's inherited company filter would restrict it to the
// writer's desk. Callers (the Shifts page, QuickAddDayNote, DeleteDayNote) authorise the molecule via
// ShiftCalendarAccess before calling, and DeleteDayNote additionally authorises the note's author or
// the assignment grant for its calendar.
public class CalendarDayNoteService : ICalendarDayNoteService
{
    /// <summary>
    /// Resx key the view localizes when a note's author has been deleted. Returned instead of null so
    /// <see cref="DayNoteView.AuthorName"/> stays non-null and no view has to branch.
    /// </summary>
    internal const string UnknownAuthorKey = "Calendar_DayNote_UnknownAuthor";

    /// <summary>
    /// How recently an identical note from the same author on the same calendar counts as a
    /// double-submit rather than a deliberate repeat. Quick Entry leaves its input open and focused
    /// for the whole round-trip, so two Enters genuinely do send two POSTs.
    ///
    /// Deliberately NOT solved with a unique index: both JobTypeId and TabId are nullable, and SQLite
    /// treats NULLs as distinct in unique indexes, so it would take four filtered index variants to
    /// cover the null combinations — far too much permanent schema for a double-click. It is also not
    /// a uniqueness RULE: re-stating the same text on a later day, or on another tab, or by another
    /// person, is all legitimate.
    /// </summary>
    private static readonly TimeSpan DuplicateSubmitWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A tab colour is only passed on when it is a strict #RRGGBB literal; anything else becomes null.
    ///
    /// <para>This matters because the value ends up inside a CSS declaration in a <c>style</c>
    /// attribute, and <see cref="Models.ShiftTab.Color"/> is NOT validated on write —
    /// ShiftTabService only trims it. Razor HTML-encodes the attribute, so a value cannot break OUT of
    /// it, but it could still inject further CSS declarations within it. Sanitising here rather than in
    /// the view means every consumer of <see cref="DayNoteView.TabColor"/> is safe by construction.</para>
    ///
    /// <para>Note the pre-existing sibling: Shifts.cshtml's tab strip inlines the same unvalidated
    /// value directly. The root fix is to validate on write in ShiftTabService and its admin page,
    /// the way ChoreTypes and DutyTypes already do with their #RRGGBB regex — deliberately left
    /// out of scope here rather than silently propagated.</para>
    /// </summary>
    private static string? SafeTabColor(string? color) =>
        color != null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")
            ? color
            : null;

    private readonly AppDbContext _db;

    public CalendarDayNoteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(CalendarDayNote Note, bool Created)> AddDayNoteAsync(
        DateOnly date, CalendarScope scope, int authorCompanyId, string text, int userId)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Double-submit guard. Returning the existing note (rather than throwing) is deliberate: a
        // double-click is benign from the user's point of view, so the second request should look
        // like it worked. See DuplicateSubmitWindow for why this is not a unique index.
        // SECURITY-AUDITED: SAFE — same reason as the class note; the scope is already authorised by
        // the caller, and this only ever matches the caller's OWN note on that scope.
        var cutoff = DateTime.UtcNow - DuplicateSubmitWindow;
        var duplicate = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.MoleculeId == scope.MoleculeId
                                   && n.JobTypeId == scope.JobTypeId
                                   && n.TabId == scope.TabId
                                   && n.Date == date
                                   && n.Text == text
                                   && n.CreatedByUserId == userId
                                   && n.CreatedAt >= cutoff);
        if (duplicate != null)
            return (duplicate, false);

        // Otherwise always INSERT. The previous design upserted by (molecule, date), which meant the
        // second writer on a day silently replaced the first author's note with no warning and no trace.
        var note = new CalendarDayNote
        {
            Date = date,
            Text = text,
            MoleculeId = scope.MoleculeId,
            JobTypeId = scope.JobTypeId,
            TabId = scope.TabId,
            // Explicit so CompanyIdInterceptor (which only fires when CompanyId == 0) leaves it alone.
            // This records WHERE the note was written; it does not scope who can see it.
            CompanyId = authorCompanyId,
            CreatedByUserId = userId
        };

        _db.CalendarDayNotes.Add(note);
        await _db.SaveChangesAsync();
        return (note, true);
    }

    public async Task<CalendarDayNote?> GetByIdAsync(int noteId)
    {
        // SECURITY-AUDITED: SAFE — see the class note. Returns the row so the caller can authorise
        // against its MoleculeId / JobTypeId / CreatedByUserId before acting on it.
        return await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == noteId);
    }

    public async Task<bool> DeleteDayNoteAsync(int noteId)
    {
        // SECURITY-AUDITED: SAFE — see the class note. Authorisation happens in DeleteDayNote before
        // this is reached.
        var note = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note == null)
            return false;

        _db.CalendarDayNotes.Remove(note);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Dictionary<DateOnly, List<DayNoteView>>> GetDayNotesForCalendarAsync(
        CalendarScope scope, DateOnly start, DateOnly end)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // SECURITY-AUDITED: SAFE — see the class note.
        var query = _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .Where(n => n.MoleculeId == scope.MoleculeId
                     && n.JobTypeId == scope.JobTypeId
                     && n.Date >= start && n.Date <= end);

        // A real tab narrows to itself. The "All" view (TabId == null) applies NO TabId predicate at
        // all — that absence IS the union.
        //
        // DO NOT collapse these into one expression. `n.TabId == scope.TabId` translates to
        // `TabId IS NULL` when the parameter is null, which returns only the All-scoped notes and
        // SILENTLY DROPS every tab-scoped one — failing the requirement while compiling, translating
        // and looking perfectly correct. `(n.TabId ?? 0) == (scope.TabId ?? 0)` translates too, but
        // defeats the (MoleculeId, JobTypeId, Date, TabId) index.
        if (scope.TabId.HasValue)
            query = query.Where(n => n.TabId == scope.TabId);

        var rows = await query
            // Stable order: it decides which notes are the inline preview chips and which fall into
            // the "+N" overflow, so it must not vary between renders.
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Select(n => new
            {
                n.Id,
                n.Date,
                n.Text,
                n.CreatedByUserId,
                n.TabId,
                AuthorName = n.CreatedByUser != null ? n.CreatedByUser.DisplayName : null,
                TabNameEn = n.Tab != null ? n.Tab.NameEn : null,
                TabNameHe = n.Tab != null ? n.Tab.NameHe : null,
                TabIsActive = n.Tab == null || n.Tab.IsActive,
                TabColor = n.Tab != null ? n.Tab.Color : null
            })
            .ToListAsync();

        // GroupBy, never ToDictionary(n => n.Date, ...): a day legitimately holds many notes now, and
        // keying a dictionary by date would throw ArgumentException on the second one.
        return rows
            .GroupBy(r => r.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new DayNoteView(
                    r.Id,
                    r.Text,
                    r.AuthorName ?? UnknownAuthorKey,
                    r.CreatedByUserId,
                    r.TabId,
                    r.TabNameEn,
                    r.TabNameHe,
                    r.TabIsActive,
                    SafeTabColor(r.TabColor))).ToList());
    }
}
