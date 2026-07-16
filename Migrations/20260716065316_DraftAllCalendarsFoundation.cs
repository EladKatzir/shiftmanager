using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class DraftAllCalendarsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec F §8 — auto-discard in-flight Active drafts. They were captured under the old schema
            // (no trainee baseline, no Surface) and the new per-surface filtered-UNIQUE indexes could
            // reject duplicate Active rows the old non-unique index allowed. Drafts are ephemeral private
            // sandboxes, so discarding is safe and cheaper than back-filling. DraftCells cascade via FK.
            migrationBuilder.Sql("DELETE FROM DraftSessions WHERE Status = 0;");

            // NOTE: EF tried to fold a ShiftAssignments TraineeId->TraineeUserId FK conversion into this
            // migration. That is a PRE-EXISTING dev drift, NOT part of the draft foundation: dev's model +
            // snapshot already use TraineeUserId (AppDbContext Fluent map), but dev's migration CHAIN never
            // performs the DB conversion (it lives on the unmerged fix/calendar-molecule-gate branch, c3bff1c),
            // so IX_ShiftAssignments_TraineeId does not exist to drop. The conversion is intentionally OMITTED
            // here so Foundation touches only draft schema; the merge session applies it once, via c3bff1c.
            migrationBuilder.DropIndex(
                name: "IX_DraftSessions_OwnerUserId_MoleculeId_Status",
                table: "DraftSessions");

            migrationBuilder.AlterColumn<int>(
                name: "MoleculeId",
                table: "DraftSessions",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "AreaId",
                table: "DraftSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Surface",
                table: "DraftSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BaselineTrainees",
                table: "DraftCells",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StagedTrainees",
                table: "DraftCells",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DraftChoreCells",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DraftSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BaselineChores = table.Column<string>(type: "TEXT", nullable: false),
                    StagedChores = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftChoreCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftChoreCells_DraftSessions_DraftSessionId",
                        column: x => x.DraftSessionId,
                        principalTable: "DraftSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DraftDutyCells",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DraftSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DutyTypeValue = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BaselineUserIds = table.Column<string>(type: "TEXT", nullable: false),
                    StagedUserIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftDutyCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftDutyCells_DraftSessions_DraftSessionId",
                        column: x => x.DraftSessionId,
                        principalTable: "DraftSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DraftSessions_Active_Chores",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "Surface", "MoleculeId", "WeekStart" },
                unique: true,
                filter: "\"Status\" = 0 AND \"Surface\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSessions_Active_OnCall",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "Surface", "AreaId", "WeekStart" },
                unique: true,
                filter: "\"Status\" = 0 AND \"Surface\" = 2 AND \"AreaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSessions_Active_OnCall_AllAreas",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "Surface", "WeekStart" },
                unique: true,
                filter: "\"Status\" = 0 AND \"Surface\" = 2 AND \"AreaId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSessions_Active_Shifts",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "Surface", "MoleculeId", "JobTypeId", "WeekStart" },
                unique: true,
                filter: "\"Status\" = 0 AND \"Surface\" = 0 AND \"JobTypeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSessions_Active_Shifts_NullJob",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "Surface", "MoleculeId", "WeekStart" },
                unique: true,
                filter: "\"Status\" = 0 AND \"Surface\" = 0 AND \"JobTypeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DraftChoreCells_DraftSessionId_UserId_WorkDate",
                table: "DraftChoreCells",
                columns: new[] { "DraftSessionId", "UserId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftDutyCells_DraftSessionId_DutyTypeValue_WorkDate",
                table: "DraftDutyCells",
                columns: new[] { "DraftSessionId", "DutyTypeValue", "WorkDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftChoreCells");

            migrationBuilder.DropTable(
                name: "DraftDutyCells");

            migrationBuilder.DropIndex(
                name: "UX_DraftSessions_Active_Chores",
                table: "DraftSessions");

            migrationBuilder.DropIndex(
                name: "UX_DraftSessions_Active_OnCall",
                table: "DraftSessions");

            migrationBuilder.DropIndex(
                name: "UX_DraftSessions_Active_OnCall_AllAreas",
                table: "DraftSessions");

            migrationBuilder.DropIndex(
                name: "UX_DraftSessions_Active_Shifts",
                table: "DraftSessions");

            migrationBuilder.DropIndex(
                name: "UX_DraftSessions_Active_Shifts_NullJob",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "AreaId",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "Surface",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "BaselineTrainees",
                table: "DraftCells");

            migrationBuilder.DropColumn(
                name: "StagedTrainees",
                table: "DraftCells");

            migrationBuilder.AlterColumn<int>(
                name: "MoleculeId",
                table: "DraftSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftSessions_OwnerUserId_MoleculeId_Status",
                table: "DraftSessions",
                columns: new[] { "OwnerUserId", "MoleculeId", "Status" });
        }
    }
}
