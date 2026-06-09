using Microsoft.EntityFrameworkCore.Migrations;
using ShiftManager.Data.SeedData;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCompanyMembershipsFromPrimary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: one IsPrimary CompanyMembership per existing user, copying the
            // per-company columns off Users. Runs after AddCompanyMembership (timestamp order).
            // SQL lives in CompanyMembershipBackfillSql so migration + regression test are identical.
            foreach (var sql in CompanyMembershipBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: remove only the system-created primary backfill rows.
            migrationBuilder.Sql("DELETE FROM CompanyMemberships WHERE GrantedBy = 0 AND IsPrimary = 1;");
        }
    }
}
