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
            // EF Core 9 SQLite provider may drop indexes during table rebuilds in prior
            // migrations (e.g. AddForeignKey triggers rebuild). Use IF EXISTS to be safe.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_CompanyId_Key\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_MoleculeId_JobTypeId_Key\";");

            // EF Core 9 SQLite provider: RenameColumn/AlterColumn below trigger a table
            // rebuild that automatically includes new columns (AreaId, NameHe, Scope) from
            // the model snapshot. Explicit AddColumn calls are NOT needed and would fail
            // with "duplicate column" because the rebuild already created them.
            // (Original EF Core 8 design required AddColumn BEFORE rebuild — no longer true.)

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
