using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomNameToShiftType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op. This backdated migration duplicated the CustomName column
            // already added by 20251101145541_AddCustomNameAndSortOrderToShiftType.
            // Running both caused "duplicate column name" on fresh databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — column is managed by the later migration.
        }
    }
}
