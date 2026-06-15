using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillChoreFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: seed General categories, categorize existing types, freeze chore weights,
            // mark existing active Standard users as chore participants. Runs AFTER AddChoreFoundation so
            // the new columns/tables exist. SQL lives in ChoreFoundationBackfillSql so this migration and
            // its regression test run identical SQL.
            foreach (var sql in ChoreFoundationBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Users SET DoesChores = 0;");
            migrationBuilder.Sql("UPDATE Chores SET WeightMinutes = 480;");
            migrationBuilder.Sql("UPDATE ChoreTypes SET ChoreCategoryId = NULL;");
            migrationBuilder.Sql("DELETE FROM ChoreCategories WHERE Name = 'General';");
        }
    }
}
