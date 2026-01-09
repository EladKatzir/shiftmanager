using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class OpsConsoleScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "ShiftTypes",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDetached",
                table: "ShiftInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OriginalProgramId",
                table: "ShiftInstances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverriddenFields",
                table: "ShiftInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MasterPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultStaffingRequired = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftPrograms_ShiftTypes_ShiftTypeId",
                        column: x => x.ShiftTypeId,
                        principalTable: "ShiftTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MasterProgramItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterProgramId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgramId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterProgramItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MasterProgramItems_MasterPrograms_MasterProgramId",
                        column: x => x.MasterProgramId,
                        principalTable: "MasterPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MasterProgramItems_ShiftPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "ShiftPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProgramDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProgramId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StaffingRequired = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramDays_ShiftPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "ShiftPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftInstances_OriginalProgramId",
                table: "ShiftInstances",
                column: "OriginalProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_MasterProgramItems_MasterProgramId_ProgramId",
                table: "MasterProgramItems",
                columns: new[] { "MasterProgramId", "ProgramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterProgramItems_MasterProgramId_SortOrder",
                table: "MasterProgramItems",
                columns: new[] { "MasterProgramId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterProgramItems_ProgramId",
                table: "MasterProgramItems",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_MasterPrograms_CompanyId_IsActive",
                table: "MasterPrograms",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgramDays_ProgramId_DayOfWeek",
                table: "ProgramDays",
                columns: new[] { "ProgramId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPrograms_CompanyId_IsActive",
                table: "ShiftPrograms",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPrograms_CompanyId_ShiftTypeId",
                table: "ShiftPrograms",
                columns: new[] { "CompanyId", "ShiftTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPrograms_ShiftTypeId",
                table: "ShiftPrograms",
                column: "ShiftTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftInstances_ShiftPrograms_OriginalProgramId",
                table: "ShiftInstances",
                column: "OriginalProgramId",
                principalTable: "ShiftPrograms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftInstances_ShiftPrograms_OriginalProgramId",
                table: "ShiftInstances");

            migrationBuilder.DropTable(
                name: "MasterProgramItems");

            migrationBuilder.DropTable(
                name: "ProgramDays");

            migrationBuilder.DropTable(
                name: "MasterPrograms");

            migrationBuilder.DropTable(
                name: "ShiftPrograms");

            migrationBuilder.DropIndex(
                name: "IX_ShiftInstances_OriginalProgramId",
                table: "ShiftInstances");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "IsDetached",
                table: "ShiftInstances");

            migrationBuilder.DropColumn(
                name: "OriginalProgramId",
                table: "ShiftInstances");

            migrationBuilder.DropColumn(
                name: "OverriddenFields",
                table: "ShiftInstances");
        }
    }
}
