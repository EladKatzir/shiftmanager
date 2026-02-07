using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyRotationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DutyRotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DutyType = table.Column<int>(type: "INTEGER", nullable: false),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeWeekends = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxConsecutiveDays = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentQueuePosition = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAssignedDate = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyRotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyRotations_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DutyRotationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DutyRotationId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyRotationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyRotationEntries_DutyRotations_DutyRotationId",
                        column: x => x.DutyRotationId,
                        principalTable: "DutyRotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DutyRotationEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DutyRotationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DutyRotationId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    SkippedUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    SkipReason = table.Column<string>(type: "TEXT", nullable: true),
                    AssignmentDate = table.Column<string>(type: "TEXT", nullable: false),
                    OnDutyId = table.Column<int>(type: "INTEGER", nullable: true),
                    WasAutoAssigned = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DutyRotationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DutyRotationLogs_DutyRotations_DutyRotationId",
                        column: x => x.DutyRotationId,
                        principalTable: "DutyRotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DutyRotationLogs_Users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyRotationLogs_Users_SkippedUserId",
                        column: x => x.SkippedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DutyRotationLogs_OnDuties_OnDutyId",
                        column: x => x.OnDutyId,
                        principalTable: "OnDuties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotations_CreatedBy",
                table: "DutyRotations",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotationEntries_DutyRotationId_Position",
                table: "DutyRotationEntries",
                columns: new[] { "DutyRotationId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotationEntries_UserId",
                table: "DutyRotationEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotationLogs_DutyRotationId_AssignmentDate",
                table: "DutyRotationLogs",
                columns: new[] { "DutyRotationId", "AssignmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotationLogs_AssignedUserId",
                table: "DutyRotationLogs",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotationLogs_SkippedUserId",
                table: "DutyRotationLogs",
                column: "SkippedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DutyRotationLogs_OnDutyId",
                table: "DutyRotationLogs",
                column: "OnDutyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DutyRotationLogs");

            migrationBuilder.DropTable(
                name: "DutyRotationEntries");

            migrationBuilder.DropTable(
                name: "DutyRotations");
        }
    }
}
