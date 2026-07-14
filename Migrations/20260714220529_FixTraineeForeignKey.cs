using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class FixTraineeForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_Users_TraineeId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_TraineeId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "TraineeId",
                table: "ShiftAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_TraineeUserId",
                table: "ShiftAssignments",
                column: "TraineeUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_Users_TraineeUserId",
                table: "ShiftAssignments",
                column: "TraineeUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_Users_TraineeUserId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_TraineeUserId",
                table: "ShiftAssignments");

            migrationBuilder.AddColumn<int>(
                name: "TraineeId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_TraineeId",
                table: "ShiftAssignments",
                column: "TraineeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_Users_TraineeId",
                table: "ShiftAssignments",
                column: "TraineeId",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
