using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ChoreTypeBilingualNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "ChoreTypes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameHe",
                table: "ChoreTypes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE ChoreTypes SET NameEn = DisplayName WHERE NameEn IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "ChoreTypes");

            migrationBuilder.DropColumn(
                name: "NameHe",
                table: "ChoreTypes");
        }
    }
}
