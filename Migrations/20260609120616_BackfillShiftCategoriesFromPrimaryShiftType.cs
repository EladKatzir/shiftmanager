using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillShiftCategoriesFromPrimaryShiftType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: legacy AppUser.PrimaryShiftTypeId → DoesShifts + ShiftCategory membership.
            // Runs AFTER AddShiftCategories so the ShiftTypes table rebuild (from the new CategoryId FK)
            // is fully settled before this data SQL reads from it. The statements (and their safeguards)
            // live in ShiftCategoryBackfillSql so this migration and its regression test run identical SQL.
            foreach (var sql in ShiftCategoryBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the data backfill (schema is dropped by AddShiftCategories.Down, which runs after this).
            migrationBuilder.Sql("DELETE FROM UserShiftCategories;");
            migrationBuilder.Sql("DELETE FROM ShiftCategories;");
            migrationBuilder.Sql("UPDATE Users SET DoesShifts = 0;");
        }
    }
}
