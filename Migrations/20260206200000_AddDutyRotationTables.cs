using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyRotationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op. This backdated migration duplicated the DutyRotations,
            // DutyRotationEntries, and DutyRotationLogs tables already created
            // by 20260207171959_AddCompanyIdToDutyRotation.
            // Running both caused "table already exists" on fresh databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — tables are managed by the later migration.
        }
    }
}
