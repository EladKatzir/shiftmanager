using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WeekEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftSessions_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DraftCells",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DraftSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BaselineUserIds = table.Column<string>(type: "TEXT", nullable: false),
                    StagedUserIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftCells_DraftSessions_DraftSessionId",
                        column: x => x.DraftSessionId,
                        principalTable: "DraftSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftCells_DraftSessionId_ShiftTypeId_WorkDate",
                table: "DraftCells",
                columns: new[] { "DraftSessionId", "ShiftTypeId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftSessions_OwnerUserId_MoleculeId_Status",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "MoleculeId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftCells");

            migrationBuilder.DropTable(
                name: "DraftSessions");
        }
    }
}
