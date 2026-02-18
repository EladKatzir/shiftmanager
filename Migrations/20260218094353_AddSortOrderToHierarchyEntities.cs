using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddSortOrderToHierarchyEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Molecules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Departments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Areas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Seed initial SortOrder based on alphabetical Name ordering per parent
            migrationBuilder.Sql(@"
                UPDATE Areas SET SortOrder = (
                    SELECT COUNT(*) FROM Areas a2
                    WHERE a2.ProjectId = Areas.ProjectId AND a2.Name < Areas.Name
                );
            ");

            migrationBuilder.Sql(@"
                UPDATE Molecules SET SortOrder = (
                    SELECT COUNT(*) FROM Molecules m2
                    WHERE m2.AreaId = Molecules.AreaId AND m2.Name < Molecules.Name
                );
            ");

            migrationBuilder.Sql(@"
                UPDATE Companies SET SortOrder = (
                    SELECT COUNT(*) FROM Companies c2
                    WHERE c2.MoleculeId = Companies.MoleculeId AND c2.Name < Companies.Name
                );
            ");

            migrationBuilder.Sql(@"
                UPDATE Departments SET SortOrder = (
                    SELECT COUNT(*) FROM Departments d2
                    WHERE d2.MoleculeId = Departments.MoleculeId AND d2.Name < Departments.Name
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Molecules");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Areas");
        }
    }
}
