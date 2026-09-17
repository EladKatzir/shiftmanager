using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCalendarDayNoteMolecule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: key existing day notes to their desk's molecule. Runs AFTER
            // ScopeCalendarDayNotesToMolecule so the MoleculeId column and its filtered unique index exist —
            // and in its own migration because that one rebuilds the table for its foreign keys, and SQL
            // queued behind a pending SQLite rebuild runs against the pre-rebuild table.
            // The SQL lives in CalendarDayNoteMoleculeBackfillSql so this migration and its regression
            // test (CalendarDayNoteMoleculeBackfillTests) run identical SQL. It deletes nothing.
            foreach (var sql in CalendarDayNoteMoleculeBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE CalendarDayNotes SET MoleculeId = NULL;");
        }
    }
}
