using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DoesShifts",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShiftCategories",
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
                    table.PrimaryKey("PK_ShiftCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftCategories_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserShiftCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftCategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserShiftCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserShiftCategories_ShiftCategories_ShiftCategoryId",
                        column: x => x.ShiftCategoryId,
                        principalTable: "ShiftCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserShiftCategories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_CategoryId",
                table: "ShiftTypes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCategories_MoleculeId",
                table: "ShiftCategories",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCategories_MoleculeId_Name",
                table: "ShiftCategories",
                columns: new[] { "MoleculeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserShiftCategories_ShiftCategoryId",
                table: "UserShiftCategories",
                column: "ShiftCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserShiftCategories_UserId_ShiftCategoryId",
                table: "UserShiftCategories",
                columns: new[] { "UserId", "ShiftCategoryId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_ShiftCategories_CategoryId",
                table: "ShiftTypes",
                column: "CategoryId",
                principalTable: "ShiftCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // NOTE: the PrimaryShiftTypeId → DoesShifts/UserShiftCategory data backfill lives in the
            // SUBSEQUENT migration `BackfillShiftCategoriesFromPrimaryShiftType`. It must run AFTER this
            // one so the SQLite rebuild of the ShiftTypes table (triggered by the new CategoryId FK) is
            // fully settled before the data SQL reads from it — see EF's "rebuild pending" guidance.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_ShiftCategories_CategoryId",
                table: "ShiftTypes");

            migrationBuilder.DropTable(
                name: "UserShiftCategories");

            migrationBuilder.DropTable(
                name: "ShiftCategories");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_CategoryId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "DoesShifts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ShiftTypes");
        }
    }
}
