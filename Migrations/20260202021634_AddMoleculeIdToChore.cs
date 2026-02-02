using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddMoleculeIdToChore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add nullable MoleculeId column
            migrationBuilder.AddColumn<int>(
                name: "MoleculeId",
                table: "Chores",
                type: "INTEGER",
                nullable: true);

            // Step 2: Populate MoleculeId from Company's MoleculeId for existing chores
            migrationBuilder.Sql(@"
                UPDATE Chores
                SET MoleculeId = (
                    SELECT c.MoleculeId
                    FROM Companies c
                    WHERE c.Id = Chores.CompanyId
                )
            ");

            // Step 3: Create index for molecule-scoped queries
            migrationBuilder.CreateIndex(
                name: "IX_Chores_MoleculeId_Date",
                table: "Chores",
                columns: new[] { "MoleculeId", "Date" });

            // Step 4: Add foreign key constraint
            migrationBuilder.AddForeignKey(
                name: "FK_Chores_Molecules_MoleculeId",
                table: "Chores",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chores_Molecules_MoleculeId",
                table: "Chores");

            migrationBuilder.DropIndex(
                name: "IX_Chores_MoleculeId_Date",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "MoleculeId",
                table: "Chores");
        }
    }
}
