using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyNameHe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameHe",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            // Backfill NameHe from DisplayName for existing companies
            migrationBuilder.Sql("UPDATE Companies SET NameHe = DisplayName WHERE DisplayName IS NOT NULL AND DisplayName != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameHe",
                table: "Companies");
        }
    }
}
