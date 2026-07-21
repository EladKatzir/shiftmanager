using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeShiftTabsForSelectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Test data only (user-confirmed no production tab data). The reshape adds JobTypeId to the
            // ShiftTab uniqueness + the UserShiftTabPreference unique key, and drops ShiftType.TabId — none of
            // which can be back-filled deterministically. Clear all tab rows so the rebuilt tables start clean.
            // Children first (FK order): company links + remembered prefs before the tabs they reference.
            migrationBuilder.Sql("DELETE FROM \"ShiftTabCompanies\";");
            migrationBuilder.Sql("DELETE FROM \"UserShiftTabPreferences\";");
            migrationBuilder.Sql("DELETE FROM \"ShiftTabs\";");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_ShiftTabs_TabId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_UserShiftTabPreferences_UserId_MoleculeId",
                table: "UserShiftTabPreferences");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_TabId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabs_MoleculeId",
                table: "ShiftTabs");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabs_MoleculeId_Name",
                table: "ShiftTabs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShiftTabCompanies",
                table: "ShiftTabCompanies");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabCompanies_CompanyId",
                table: "ShiftTabCompanies");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabCompanies_ShiftTabId",
                table: "ShiftTabCompanies");

            migrationBuilder.DropColumn(
                name: "TabId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "ShiftTabCompanies");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ShiftTabs",
                newName: "NameHe");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "ShiftTabs",
                newName: "NameEn");

            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "UserShiftTabPreferences",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "ShiftTabs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "ShiftTabs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrioritizeCompanyUsers",
                table: "ShiftTabs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ShiftTabs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShiftTabCompanies",
                table: "ShiftTabCompanies",
                columns: new[] { "ShiftTabId", "CompanyId" });

            migrationBuilder.CreateTable(
                name: "ShiftTabShiftTypes",
                columns: table => new
                {
                    ShiftTabId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftTypeId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTabShiftTypes", x => new { x.ShiftTabId, x.ShiftTypeId });
                    table.ForeignKey(
                        name: "FK_ShiftTabShiftTypes_ShiftTabs_ShiftTabId",
                        column: x => x.ShiftTabId,
                        principalTable: "ShiftTabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftTabShiftTypes_ShiftTypes_ShiftTypeId",
                        column: x => x.ShiftTypeId,
                        principalTable: "ShiftTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserShiftTabPreferences_UserId_MoleculeId_JobTypeId",
                table: "UserShiftTabPreferences",
                columns: new[] { "UserId", "MoleculeId", "JobTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabs_JobTypeId",
                table: "ShiftTabs",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabs_MoleculeId_JobTypeId",
                table: "ShiftTabs",
                columns: new[] { "MoleculeId", "JobTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabs_MoleculeId_JobTypeId_NameEn",
                table: "ShiftTabs",
                columns: new[] { "MoleculeId", "JobTypeId", "NameEn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabs_MoleculeId_JobTypeId_NameHe",
                table: "ShiftTabs",
                columns: new[] { "MoleculeId", "JobTypeId", "NameHe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabCompanies_CompanyId",
                table: "ShiftTabCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabShiftTypes_ShiftTypeId",
                table: "ShiftTabShiftTypes",
                column: "ShiftTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTabs_JobTypes_JobTypeId",
                table: "ShiftTabs",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTabs_JobTypes_JobTypeId",
                table: "ShiftTabs");

            migrationBuilder.DropTable(
                name: "ShiftTabShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_UserShiftTabPreferences_UserId_MoleculeId_JobTypeId",
                table: "UserShiftTabPreferences");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabs_JobTypeId",
                table: "ShiftTabs");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabs_MoleculeId_JobTypeId",
                table: "ShiftTabs");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabs_MoleculeId_JobTypeId_NameEn",
                table: "ShiftTabs");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabs_MoleculeId_JobTypeId_NameHe",
                table: "ShiftTabs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShiftTabCompanies",
                table: "ShiftTabCompanies");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTabCompanies_CompanyId",
                table: "ShiftTabCompanies");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "UserShiftTabPreferences");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ShiftTabs");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "ShiftTabs");

            migrationBuilder.DropColumn(
                name: "PrioritizeCompanyUsers",
                table: "ShiftTabs");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ShiftTabs");

            migrationBuilder.RenameColumn(
                name: "NameHe",
                table: "ShiftTabs",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "NameEn",
                table: "ShiftTabs",
                newName: "DisplayName");

            migrationBuilder.AddColumn<int>(
                name: "TabId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ShiftTabCompanies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShiftTabCompanies",
                table: "ShiftTabCompanies",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserShiftTabPreferences_UserId_MoleculeId",
                table: "UserShiftTabPreferences",
                columns: new[] { "UserId", "MoleculeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_TabId",
                table: "ShiftTypes",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabs_MoleculeId",
                table: "ShiftTabs",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabs_MoleculeId_Name",
                table: "ShiftTabs",
                columns: new[] { "MoleculeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabCompanies_CompanyId",
                table: "ShiftTabCompanies",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabCompanies_ShiftTabId",
                table: "ShiftTabCompanies",
                column: "ShiftTabId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_ShiftTabs_TabId",
                table: "ShiftTypes",
                column: "TabId",
                principalTable: "ShiftTabs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
