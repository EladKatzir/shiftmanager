using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class IndexTimeOffLeaveGroupId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_LeaveGroupId",
                table: "TimeOffRequests",
                column: "LeaveGroupId",
                filter: "\"LeaveGroupId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeOffRequests_LeaveGroupId",
                table: "TimeOffRequests");
        }
    }
}
