using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTypeIdToUserJoinRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "UserJoinRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserJoinRequests_JobTypeId",
                table: "UserJoinRequests",
                column: "JobTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserJoinRequests_JobTypes_JobTypeId",
                table: "UserJoinRequests",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserJoinRequests_JobTypes_JobTypeId",
                table: "UserJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_UserJoinRequests_JobTypeId",
                table: "UserJoinRequests");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "UserJoinRequests");
        }
    }
}
