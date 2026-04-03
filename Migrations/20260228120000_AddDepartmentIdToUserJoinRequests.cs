using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentIdToUserJoinRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op. This backdated migration duplicated the DepartmentId column,
            // index, and FK on UserJoinRequests already added by
            // 20260302220718_AddMoleculeIdToJobType.
            // Running both caused "duplicate column name" on fresh databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — column is managed by the later migration.
        }
    }
}
