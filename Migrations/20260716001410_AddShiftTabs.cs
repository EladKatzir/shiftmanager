using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTabs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TabId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShiftTabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTabs_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTabCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShiftTabId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTabCompanies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTabCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftTabCompanies_ShiftTabs_ShiftTabId",
                        column: x => x.ShiftTabId,
                        principalTable: "ShiftTabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserShiftTabPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TabId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserShiftTabPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserShiftTabPreferences_ShiftTabs_TabId",
                        column: x => x.TabId,
                        principalTable: "ShiftTabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_TabId",
                table: "ShiftTypes",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabCompanies_CompanyId",
                table: "ShiftTabCompanies",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTabCompanies_ShiftTabId",
                table: "ShiftTabCompanies",
                column: "ShiftTabId");

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
                name: "IX_UserShiftTabPreferences_TabId",
                table: "UserShiftTabPreferences",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_UserShiftTabPreferences_UserId_MoleculeId",
                table: "UserShiftTabPreferences",
                columns: new[] { "UserId", "MoleculeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_ShiftTabs_TabId",
                table: "ShiftTypes",
                column: "TabId",
                principalTable: "ShiftTabs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_ShiftTabs_TabId",
                table: "ShiftTypes");

            migrationBuilder.DropTable(
                name: "ShiftTabCompanies");

            migrationBuilder.DropTable(
                name: "UserShiftTabPreferences");

            migrationBuilder.DropTable(
                name: "ShiftTabs");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_TabId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "TabId",
                table: "ShiftTypes");
        }
    }
}
