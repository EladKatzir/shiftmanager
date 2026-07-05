using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftBlockingAndHourCounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true: every existing (and future) shift type blocks overlap/rest and counts
            // toward hour limits — byte-identical to pre-migration behaviour for normal shifts.
            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardHourLimits",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocking",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Presence statuses (Offline/Home) are intrinsically non-blocking and non-counting.
            // Backfill their columns so stored data agrees with the runtime invariant.
            migrationBuilder.Sql(
                "UPDATE ShiftTypes SET IsBlocking = 0, CountsTowardHourLimits = 0 " +
                "WHERE Key IN ('OFFLINE', 'HOME', 'HOME_PM', 'HOME_AM');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountsTowardHourLimits",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "IsBlocking",
                table: "ShiftTypes");
        }
    }
}
