using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddStoresAndQuickInfoConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores");

            migrationBuilder.AddForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores");

            migrationBuilder.AddForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
