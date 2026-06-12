using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCalendarRowOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserCalendarRowOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContextKey = table.Column<string>(type: "TEXT", nullable: false),
                    GroupId = table.Column<string>(type: "TEXT", nullable: false),
                    RowId = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCalendarRowOrders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCalendarRowOrders_UserId_ContextKey",
                table: "UserCalendarRowOrders",
                columns: new[] { "UserId", "ContextKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCalendarRowOrders_UserId_ContextKey_GroupId_RowId",
                table: "UserCalendarRowOrders",
                columns: new[] { "UserId", "ContextKey", "GroupId", "RowId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCalendarRowOrders");
        }
    }
}
