using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexMoleculeShiftTypeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_MoleculeId",
                table: "ShiftTypes");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_MoleculeId_JobTypeId_Key",
                table: "ShiftTypes",
                columns: new[] { "MoleculeId", "JobTypeId", "Key" },
                unique: true,
                filter: "MoleculeId IS NOT NULL AND JobTypeId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_MoleculeId_JobTypeId_Key",
                table: "ShiftTypes");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_MoleculeId",
                table: "ShiftTypes",
                column: "MoleculeId");
        }
    }
}
