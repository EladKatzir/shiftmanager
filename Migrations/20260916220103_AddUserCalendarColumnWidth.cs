using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCalendarColumnWidth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserCalendarColumnWidths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContextKey = table.Column<string>(type: "TEXT", nullable: false),
                    ColumnKey = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCalendarColumnWidths", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCalendarColumnWidths_UserId_ContextKey",
                table: "UserCalendarColumnWidths",
                columns: new[] { "UserId", "ContextKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCalendarColumnWidths_UserId_ContextKey_ColumnKey",
                table: "UserCalendarColumnWidths",
                columns: new[] { "UserId", "ContextKey", "ColumnKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCalendarColumnWidths");
        }
    }
}
