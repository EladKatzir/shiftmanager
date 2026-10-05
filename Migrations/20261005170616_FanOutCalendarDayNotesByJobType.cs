using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <summary>
    /// Data half of the move from one-per-(molecule, day) day notes to one-per-CALENDAR. The SQL lives
    /// in <see cref="CalendarDayNoteJobTypeFanOutSql"/> so CalendarDayNoteJobTypeFanOutTests can run
    /// the exact statements this migration applies.
    ///
    /// <para><b>Why this is a separate migration from ScopeCalendarDayNotesToCalendar.</b> Not for the
    /// reason previously recorded in this repo — EF Core's SQLite generator hoists rebuild-triggering
    /// operations (AddForeignKey/AlterColumn/DropColumn) to the END of a migration wherever they are
    /// written, while ALTER TABLE ADD COLUMN and DROP INDEX are native and run first. Raw SQL in the
    /// same migration therefore DOES see the new columns.
    ///
    /// The real reason is that in one migration the correctness would rest on an invisible intra-body
    /// ordering rule: the Sql() calls must sit after the DropIndex() call. Written before it, the
    /// fan-out fails at runtime with
    /// "UNIQUE constraint failed: CalendarDayNotes.MoleculeId, CalendarDayNotes.Date", because the old
    /// unique index is still live and the copies share (MoleculeId, Date) by construction. Splitting
    /// makes the order a property of the migration SEQUENCE, which EF enforces, rather than of
    /// statement order inside one method, which nothing enforces and no test would catch.</para>
    ///
    /// <para><b>ONE-WAY.</b> Down() is deliberately empty. After the fan-out, a molecule/day can hold
    /// several notes, so the unique index IX_CalendarDayNotes_MoleculeId_Date that
    /// ScopeCalendarDayNotesToCalendar.Down() recreates cannot be restored without deleting rows a
    /// user typed. The preceding ScopeCalendarDayNotesToMolecule migration already carries the same
    /// caveat; here it is a guarantee rather than a risk. To roll back, restore the pre-migration
    /// database backup that Program.cs writes at startup.</para>
    /// </summary>
    public partial class FanOutCalendarDayNotesByJobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order is load-bearing: INSERT the per-job-type copies first, THEN key the original row
            // to the anchor job type. Both statements select on `JobTypeId IS NULL`, so running the
            // UPDATE first consumes that marker, the INSERT matches nothing, and the whole backfill
            // becomes a silent no-op that still reports success.
            // Pinned by CalendarDayNoteJobTypeFanOutTests.RunningTheUpdateBeforeTheInsert_SilentlyMakesNoCopies.
            foreach (var sql in CalendarDayNoteJobTypeFanOutSql.Forward)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see the class remarks. Reversing the fan-out would mean choosing
            // which of a day's notes to destroy.
        }
    }
}
