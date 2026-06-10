using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddEngagementModeAndCategoryMutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CatchUpEmailPending",
                table: "DailyNotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EngagementMode",
                table: "DailyNotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCatchUpEmailAt",
                table: "DailyNotificationPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationCategoryMutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationCategoryMutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationCategoryMutes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCategoryMutes_CompanyId_UserId_Category",
                table: "NotificationCategoryMutes",
                columns: new[] { "CompanyId", "UserId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCategoryMutes_UserId",
                table: "NotificationCategoryMutes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationCategoryMutes");

            migrationBuilder.DropColumn(
                name: "CatchUpEmailPending",
                table: "DailyNotificationPreferences");

            migrationBuilder.DropColumn(
                name: "EngagementMode",
                table: "DailyNotificationPreferences");

            migrationBuilder.DropColumn(
                name: "LastCatchUpEmailAt",
                table: "DailyNotificationPreferences");
        }
    }
}
