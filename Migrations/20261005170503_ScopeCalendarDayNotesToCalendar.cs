using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCalendarDayNotesToCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarDayNotes_Users_CreatedByUserId",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_MoleculeId_Date",
                table: "CalendarDayNotes");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "CalendarDayNotes",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "CalendarDayNotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TabId",
                table: "CalendarDayNotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_JobTypeId",
                table: "CalendarDayNotes",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_MoleculeId_JobTypeId_Date_TabId",
                table: "CalendarDayNotes",
                columns: new[] { "MoleculeId", "JobTypeId", "Date", "TabId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_TabId",
                table: "CalendarDayNotes",
                column: "TabId");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarDayNotes_JobTypes_JobTypeId",
                table: "CalendarDayNotes",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarDayNotes_ShiftTabs_TabId",
                table: "CalendarDayNotes",
                column: "TabId",
                principalTable: "ShiftTabs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarDayNotes_Users_CreatedByUserId",
                table: "CalendarDayNotes",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarDayNotes_JobTypes_JobTypeId",
                table: "CalendarDayNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CalendarDayNotes_ShiftTabs_TabId",
                table: "CalendarDayNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_CalendarDayNotes_Users_CreatedByUserId",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_JobTypeId",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_MoleculeId_JobTypeId_Date_TabId",
                table: "CalendarDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_CalendarDayNotes_TabId",
                table: "CalendarDayNotes");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "CalendarDayNotes");

            migrationBuilder.DropColumn(
                name: "TabId",
                table: "CalendarDayNotes");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "CalendarDayNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDayNotes_MoleculeId_Date",
                table: "CalendarDayNotes",
                columns: new[] { "MoleculeId", "Date" },
                unique: true,
                filter: "MoleculeId IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarDayNotes_Users_CreatedByUserId",
                table: "CalendarDayNotes",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
