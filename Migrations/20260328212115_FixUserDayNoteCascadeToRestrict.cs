using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class FixUserDayNoteCascadeToRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserDayNotes_Companies_CompanyId",
                table: "UserDayNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserDayNotes_Users_UserId",
                table: "UserDayNotes");

            migrationBuilder.AddForeignKey(
                name: "FK_UserDayNotes_Companies_CompanyId",
                table: "UserDayNotes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserDayNotes_Users_UserId",
                table: "UserDayNotes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserDayNotes_Companies_CompanyId",
                table: "UserDayNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserDayNotes_Users_UserId",
                table: "UserDayNotes");

            migrationBuilder.AddForeignKey(
                name: "FK_UserDayNotes_Companies_CompanyId",
                table: "UserDayNotes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserDayNotes_Users_UserId",
                table: "UserDayNotes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
