using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCalendarDayNotesToMolecule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_CompanyId_Date",
                table: "CalendarDayNotes");

            migrationBuilder.AddColumn<int>(
                name: "MoleculeId",
                table: "CalendarDayNotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedByUserId",
                table: "CalendarDayNotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_CompanyId",
                table: "CalendarDayNotes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_MoleculeId_Date",
                table: "CalendarDayNotes",
                columns: new[] { "MoleculeId", "Date" },
                unique: true,
                filter: "MoleculeId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_UpdatedByUserId",
                table: "CalendarDayNotes",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarDayNotes_Molecules_MoleculeId",
                table: "CalendarDayNotes",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarDayNotes_Users_UpdatedByUserId",
                table: "CalendarDayNotes",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarDayNotes_Molecules_MoleculeId",
                table: "CalendarDayNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CalendarDayNotes_Users_UpdatedByUserId",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_CompanyId",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_MoleculeId_Date",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_UpdatedByUserId",
                table: "CalendarDayNotes");

            migrationBuilder.DropColumn(
                name: "MoleculeId",
                table: "CalendarDayNotes");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "CalendarDayNotes");

            // ROLLBACK CAVEAT: this restores the old one-note-per-(desk, day) uniqueness. Once molecule
            // keying is live, one desk can legitimately hold a note on the same day for two different
            // molecules (e.g. a director annotating two calendars), and this index creation will then
            // fail. Resolve those duplicates by hand before rolling back.
            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_CompanyId_Date",
                table: "CalendarDayNotes",
                columns: new[] { "CompanyId", "Date" },
                unique: true);
        }
    }
}
