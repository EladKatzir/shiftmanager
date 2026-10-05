using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A free-text note attached to a whole calendar DAY (not a specific user) on the Shifts calendar.
/// Created via Quick Entry's "Add note to this day" option (shift-mode or user-mode).
///
/// <para><b>Keyed to a CALENDAR</b>, by the triple (<see cref="MoleculeId"/>,
/// <see cref="JobTypeId"/>, <see cref="TabId"/>) — exactly the identity
/// <c>Pages/Calendar/Shifts.cshtml.cs</c> resolves for each request. Everyone who can open that
/// calendar sees its notes, whichever desk or molecule they themselves sit in; the only gate is
/// being able to open the calendar at all (<see cref="Services.ShiftCalendarAccess"/>).</para>
///
/// <para>The scope has widened twice, each time to fix a real invisibility bug: it was once keyed
/// to the writer's desk (<see cref="CompanyId"/>), which hid a note from colleagues in other desks
/// looking at the very same calendar; then to the molecule alone, which showed one note on every
/// job-type calendar in that molecule.</para>
///
/// <para><b>MANY notes per day are allowed.</b> There is deliberately no unique index: a note is one
/// person's contribution, created and deleted as a unit, never silently overwritten by the next
/// writer. <see cref="UpdatedAt"/> and <see cref="UpdatedByUserId"/> are therefore vestigial — kept
/// because a historical migration's SQL reads <c>COALESCE(UpdatedAt, CreatedAt)</c> and its
/// regression test builds the schema from this model, so dropping the column would break a test
/// whose whole purpose is pinning frozen migration behaviour.</para>
///
/// <para><b>Both scope parts are legitimately nullable, and neither NULL means "unset".</b>
/// <see cref="JobTypeId"/> is NULL for a <see cref="Support.MoleculeType.Tech"/> molecule, whose
/// calendar runs with no job type at all — so NULL is <i>unavailable</i> as an "applies to every job
/// type" sentinel, which is why the job-type fan-out migration keys on the molecule's Type rather
/// than on this column. <see cref="TabId"/> is NULL for the synthetic "All" view, which has no
/// <see cref="ShiftTab"/> row; reading the All view unions every note of the (molecule, jobType)
/// calendar, while a real tab shows only its own.</para>
///
/// <para><b>Do not "fix" the ambiguous triple.</b> (M, NULL, NULL) means "M's calendar as rendered
/// when it has no job type". That is true both for a Tech molecule and for a non-Tech molecule whose
/// area has no resolvable job types. The encoding is self-consistent and both read back correctly;
/// the write endpoint refuses the second case so it cannot enter the system. Its meaning does depend
/// on mutable data outside the row — flipping a molecule's Type, or adding the first job type to a
/// previously job-type-less area, changes which notes are reachable.</para>
///
/// <para>Intentionally distinct from <see cref="CalendarTextEntry"/>, whose notes attach to a
/// UserId; a shift-type cell has no single user, so day notes need a user-agnostic home.</para>
///
/// <para>READ ONLY THROUGH <see cref="Services.ICalendarDayNoteService"/>. The inherited company
/// query filter is defence in depth for the writer's desk, not the visibility boundary — a calendar
/// note is legitimately visible to viewers from other desks, which is exactly why the service
/// bypasses that filter.</para>
/// </summary>
public class CalendarDayNote : IBelongsToCompany
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    /// <summary>The desk the note was written from — provenance only; it does not scope visibility.</summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// The molecule whose Shifts calendar this note annotates. NULL only for legacy rows the molecule
    /// backfill could not key — their desk had no molecule, or a newer note from another desk claimed
    /// the same day. Those rows are kept (never deleted) but belong to no calendar, so none shows them.
    /// </summary>
    public int? MoleculeId { get; set; }

    /// <summary>
    /// The job type of the calendar this note annotates. NULL is a real value, not a missing one: a
    /// Tech molecule's calendar has no job type. See the class remarks before treating NULL as
    /// "every job type" — it is not, and doing so destroys Tech molecules' notes.
    /// </summary>
    public int? JobTypeId { get; set; }

    /// <summary>
    /// The calendar tab (לשונית) this note annotates, or NULL for the synthetic "All" view. A note on
    /// a real tab shows only on that tab; a NULL-tab note shows only on All. Reading All returns both.
    /// </summary>
    public int? TabId { get; set; }

    /// <summary>
    /// The user who wrote the note. NULL once that user is deleted (the FK is SetNull), which keeps
    /// the note readable and, because authorship is also a delete permission, correctly leaves an
    /// orphaned note deletable only by someone holding the assignment grant for its calendar.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Vestigial — notes are no longer edited in place. See the class remarks.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Vestigial — notes are no longer edited in place. See the class remarks.</summary>
    public int? UpdatedByUserId { get; set; }

    // Navigation. Each nav name matches its own FK property deliberately: this project has already
    // shipped a shadow-FK bug where a nav called `Trainee` pointed at `TraineeId` while the real
    // column was `TraineeUserId`, producing a silent second column.
    public Company Company { get; set; } = null!;
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
    public JobType? JobType { get; set; }
    public ShiftTab? Tab { get; set; }
}
