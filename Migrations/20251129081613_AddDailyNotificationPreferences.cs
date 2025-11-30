using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyNotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceiveDailyDigest = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferredTime = table.Column<string>(type: "TEXT", nullable: false),
                    IncludeUpcomingShifts = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludePendingRequests = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeChores = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeOnDuty = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyNotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyNotificationPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyNotificationPreferences_CompanyId_UserId",
                table: "DailyNotificationPreferences",
                columns: new[] { "CompanyId", "UserId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DailyNotificationPreferences_UserId",
                table: "DailyNotificationPreferences",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyNotificationPreferences");
        }
    }
}
