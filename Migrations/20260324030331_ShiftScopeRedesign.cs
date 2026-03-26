using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ShiftScopeRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_CompanyId_Key",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_MoleculeId_JobTypeId_Key",
                table: "ShiftTypes");

            // AddColumn BEFORE AlterColumn/RenameColumn — SQLite rebuilds the table
            // on ALTER, and the rebuild references all model columns. If these columns
            // don't exist yet, the INSERT SELECT into ef_temp fails.
            migrationBuilder.AddColumn<int>(
                name: "AreaId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameHe",
                table: "ShiftTypes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // Default = Molecule (1), not Company (0)

            migrationBuilder.RenameColumn(
                name: "CustomName",
                table: "ShiftTypes",
                newName: "NameEn");

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_AreaId",
                table: "ShiftTypes",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_CompanyId_Key",
                table: "ShiftTypes",
                columns: new[] { "CompanyId", "Key" },
                filter: "CompanyId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_MoleculeId_JobTypeId_Key",
                table: "ShiftTypes",
                columns: new[] { "MoleculeId", "JobTypeId", "Key" },
                unique: true,
                filter: "MoleculeId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_Scope_AreaId",
                table: "ShiftTypes",
                columns: new[] { "Scope", "AreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_Scope_MoleculeId",
                table: "ShiftTypes",
                columns: new[] { "Scope", "MoleculeId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftType_Area_Scope",
                table: "ShiftTypes",
                sql: "Scope != 2 OR AreaId IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftType_Company_Scope",
                table: "ShiftTypes",
                sql: "Scope != 0 OR CompanyId IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftType_Molecule_Scope",
                table: "ShiftTypes",
                sql: "Scope != 1 OR MoleculeId IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_Areas_AreaId",
                table: "ShiftTypes",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_Companies_CompanyId",
                table: "ShiftTypes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_Areas_AreaId",
                table: "ShiftTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_Companies_CompanyId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_AreaId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_CompanyId_Key",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_MoleculeId_JobTypeId_Key",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_Scope_AreaId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_Scope_MoleculeId",
                table: "ShiftTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftType_Area_Scope",
                table: "ShiftTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftType_Company_Scope",
                table: "ShiftTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftType_Molecule_Scope",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "AreaId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "NameHe",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ShiftTypes");

            migrationBuilder.RenameColumn(
                name: "NameEn",
                table: "ShiftTypes",
                newName: "CustomName");

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_CompanyId_Key",
                table: "ShiftTypes",
                columns: new[] { "CompanyId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_MoleculeId_JobTypeId_Key",
                table: "ShiftTypes",
                columns: new[] { "MoleculeId", "JobTypeId", "Key" },
                unique: true,
                filter: "MoleculeId IS NOT NULL AND JobTypeId IS NOT NULL");
        }
    }
}
