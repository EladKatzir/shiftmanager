using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTimeOffRequestIdToShiftAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceTimeOffRequestId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_SourceTimeOffRequestId",
                table: "ShiftAssignments",
                column: "SourceTimeOffRequestId",
                filter: "[SourceTimeOffRequestId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_TimeOffRequests_SourceTimeOffRequestId",
                table: "ShiftAssignments",
                column: "SourceTimeOffRequestId",
                principalTable: "TimeOffRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_TimeOffRequests_SourceTimeOffRequestId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_SourceTimeOffRequestId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "SourceTimeOffRequestId",
                table: "ShiftAssignments");
        }
    }
}
