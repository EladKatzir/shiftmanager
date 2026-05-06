using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveHomeTypeDefaultTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultEndTime",
                table: "HomeTypes");

            migrationBuilder.DropColumn(
                name: "DefaultStartTime",
                table: "HomeTypes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultEndTime",
                table: "HomeTypes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultStartTime",
                table: "HomeTypes",
                type: "TEXT",
                nullable: true);
        }
    }
}
