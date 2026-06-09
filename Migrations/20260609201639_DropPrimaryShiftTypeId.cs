using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class DropPrimaryShiftTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrimaryShiftTypeId",
                table: "Users",
                type: "INTEGER",
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
    }
}
