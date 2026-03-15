using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTypeIsWorkforceOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWorkforceOnly",
                table: "JobTypes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Data migration: Alhut and Text are workforce-only (should not appear for Tech molecules)
            migrationBuilder.Sql("UPDATE JobTypes SET IsWorkforceOnly = 1 WHERE Name IN ('Alhut', 'Text');");

            // Rename ProjectManager display name
            migrationBuilder.Sql("UPDATE JobTypes SET DisplayName = 'פרויקטור' WHERE Name = 'ProjectManager';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsWorkforceOnly",
                table: "JobTypes");
        }
    }
}
