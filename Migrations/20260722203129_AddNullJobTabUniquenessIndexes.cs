using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddNullJobTabUniquenessIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_UserShiftTabPreferences_UserId_MoleculeId_NullJob",
                table: "UserShiftTabPreferences",
                columns: new[] { "UserId", "MoleculeId" },
                unique: true,
                filter: "\"JobTypeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ShiftTabs_MoleculeId_NameEn_NullJob",
                table: "ShiftTabs",
                columns: new[] { "MoleculeId", "NameEn" },
                unique: true,
                filter: "\"JobTypeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ShiftTabs_MoleculeId_NameHe_NullJob",
                table: "ShiftTabs",
                columns: new[] { "MoleculeId", "NameHe" },
                unique: true,
                filter: "\"JobTypeId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_UserShiftTabPreferences_UserId_MoleculeId_NullJob",
                table: "UserShiftTabPreferences");

            migrationBuilder.DropIndex(
                name: "UX_ShiftTabs_MoleculeId_NameEn_NullJob",
                table: "ShiftTabs");

            migrationBuilder.DropIndex(
                name: "UX_ShiftTabs_MoleculeId_NameHe_NullJob",
                table: "ShiftTabs");
        }
    }
}
