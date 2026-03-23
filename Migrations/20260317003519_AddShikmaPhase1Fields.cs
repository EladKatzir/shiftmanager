using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddShikmaPhase1Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrimaryShiftTypeId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTraineeShift",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ShiftAssignments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndTime",
                table: "Chores",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartTime",
                table: "Chores",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PrimaryShiftTypeId",
                table: "Users",
                column: "PrimaryShiftTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_ShiftTypes_PrimaryShiftTypeId",
                table: "Users",
                column: "PrimaryShiftTypeId",
                principalTable: "ShiftTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_ShiftTypes_PrimaryShiftTypeId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PrimaryShiftTypeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PrimaryShiftTypeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsTraineeShift",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Chores");
        }
    }
}
