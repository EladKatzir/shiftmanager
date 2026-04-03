using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddVacationApprovalRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op. This backdated migration duplicated the VacationApprovalRules
            // table already created by 20260207171959_AddCompanyIdToDutyRotation.
            // Running both caused "table already exists" on fresh databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — table is managed by the later migration.
        }
    }
}
