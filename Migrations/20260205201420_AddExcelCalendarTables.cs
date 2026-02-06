using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddExcelCalendarTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftGroupings_MoleculeId",
                table: "ShiftGroupings");

            migrationBuilder.AddColumn<string>(
                name: "RowColor",
                table: "ShiftTypes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "ShiftGroupings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ShiftGroupings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChoreTypeId",
                table: "Chores",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChoreTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreTypes_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChoreTypes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftCapacityOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShiftTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftCapacityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftCapacityOverrides_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftCapacityOverrides_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftCapacityOverrides_ShiftTypes_ShiftTypeId",
                        column: x => x.ShiftTypeId,
                        principalTable: "ShiftTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftCapacityOverrides_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserDayNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDayNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDayNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDayNotes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDayNotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupings_JobTypeId",
                table: "ShiftGroupings",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupings_MoleculeId_JobTypeId_Name",
                table: "ShiftGroupings",
                columns: new[] { "MoleculeId", "JobTypeId", "Name" },
                unique: true,
                filter: "[JobTypeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Chores_ChoreTypeId",
                table: "Chores",
                column: "ChoreTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTypes_CreatedByUserId",
                table: "ChoreTypes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTypes_MoleculeId_Name",
                table: "ChoreTypes",
                columns: new[] { "MoleculeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCapacityOverrides_CreatedByUserId",
                table: "ShiftCapacityOverrides",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCapacityOverrides_JobTypeId",
                table: "ShiftCapacityOverrides",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCapacityOverrides_MoleculeId",
                table: "ShiftCapacityOverrides",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCapacityOverrides_ShiftTypeId_MoleculeId_JobTypeId_Date",
                table: "ShiftCapacityOverrides",
                columns: new[] { "ShiftTypeId", "MoleculeId", "JobTypeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDayNotes_CompanyId",
                table: "UserDayNotes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDayNotes_CreatedByUserId",
                table: "UserDayNotes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDayNotes_UserId_Date_CompanyId",
                table: "UserDayNotes",
                columns: new[] { "UserId", "Date", "CompanyId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Chores_ChoreTypes_ChoreTypeId",
                table: "Chores",
                column: "ChoreTypeId",
                principalTable: "ChoreTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftGroupings_JobTypes_JobTypeId",
                table: "ShiftGroupings",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chores_ChoreTypes_ChoreTypeId",
                table: "Chores");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftGroupings_JobTypes_JobTypeId",
                table: "ShiftGroupings");

            migrationBuilder.DropTable(
                name: "ChoreTypes");

            migrationBuilder.DropTable(
                name: "ShiftCapacityOverrides");

            migrationBuilder.DropTable(
                name: "UserDayNotes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftGroupings_JobTypeId",
                table: "ShiftGroupings");

            migrationBuilder.DropIndex(
                name: "IX_ShiftGroupings_MoleculeId_JobTypeId_Name",
                table: "ShiftGroupings");

            migrationBuilder.DropIndex(
                name: "IX_Chores_ChoreTypeId",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "RowColor",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "ShiftGroupings");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ShiftGroupings");

            migrationBuilder.DropColumn(
                name: "ChoreTypeId",
                table: "Chores");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupings_MoleculeId",
                table: "ShiftGroupings",
                column: "MoleculeId");
        }
    }
}
