using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ExtendNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemindBeforeChores",
                table: "DailyNotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RemindBeforeOnDuty",
                table: "DailyNotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RemindBeforeShifts",
                table: "DailyNotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OnDutyRoleSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    OnDutyTypeValue = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnDutyRoleSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnDutyRoleSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnDutyRoleSubscriptions_CompanyId_UserId_OnDutyTypeValue",
                table: "OnDutyRoleSubscriptions",
                columns: new[] { "CompanyId", "UserId", "OnDutyTypeValue" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OnDutyRoleSubscriptions_UserId",
                table: "OnDutyRoleSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnDutyRoleSubscriptions");

            migrationBuilder.DropColumn(
                name: "RemindBeforeChores",
                table: "DailyNotificationPreferences");

            migrationBuilder.DropColumn(
                name: "RemindBeforeOnDuty",
                table: "DailyNotificationPreferences");

            migrationBuilder.DropColumn(
                name: "RemindBeforeShifts",
                table: "DailyNotificationPreferences");
        }
    }
}
