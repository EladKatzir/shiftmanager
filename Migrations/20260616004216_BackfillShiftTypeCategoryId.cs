using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillShiftTypeCategoryId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: stamp ShiftType.CategoryId from the "[id]"-tagged categories the user
            // backfill synthesized. SQL lives in ShiftTypeCategoryBackfillSql so this migration and its
            // regression test (ShiftTypeCategoryBackfillTests) run identical statements.
            foreach (var sql in ShiftTypeCategoryBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-null only the CategoryIds this backfill could have set (those whose category Name
            // carries the source-type "[id]" tag). Manual assignments to untagged categories are kept.
            migrationBuilder.Sql(@"
                UPDATE ShiftTypes
                SET CategoryId = NULL
                WHERE CategoryId IN (
                    SELECT sc.Id FROM ShiftCategories sc
                    LEFT JOIN Companies c ON c.Id = ShiftTypes.CompanyId
                    WHERE sc.MoleculeId = COALESCE(ShiftTypes.MoleculeId, c.MoleculeId)
                      AND sc.Name = COALESCE(NULLIF(TRIM(ShiftTypes.NameEn), ''), ShiftTypes.TechShiftType, ShiftTypes.Key) || ' [' || ShiftTypes.Id || ']'
                );");
        }
    }
}
