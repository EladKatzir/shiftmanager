using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddMyTeamCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamCalendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamCalendars_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamCalendars_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamCalendarMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamCalendarId = table.Column<int>(type: "INTEGER", nullable: false),
                    MemberUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamCalendarMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamCalendarMembers_TeamCalendars_TeamCalendarId",
                        column: x => x.TeamCalendarId,
                        principalTable: "TeamCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamCalendarMembers_Users_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendarMembers_MemberUserId",
                table: "TeamCalendarMembers",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendarMembers_TeamCalendarId_MemberUserId",
                table: "TeamCalendarMembers",
                columns: new[] { "TeamCalendarId", "MemberUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendars_CompanyId_OwnerId_CreatedAt",
                table: "TeamCalendars",
                columns: new[] { "CompanyId", "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendars_CompanyId_OwnerId_Name",
                table: "TeamCalendars",
                columns: new[] { "CompanyId", "OwnerId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendars_OwnerId",
                table: "TeamCalendars",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamCalendarMembers");

            migrationBuilder.DropTable(
                name: "TeamCalendars");
        }
    }
}
