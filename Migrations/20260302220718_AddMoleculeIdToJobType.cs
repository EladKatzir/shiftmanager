using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddMoleculeIdToJobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "UserJoinRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoleculeId",
                table: "JobTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserJoinRequests_DepartmentId",
                table: "UserJoinRequests",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_JobTypes_MoleculeId",
                table: "JobTypes",
                column: "MoleculeId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobTypes_Molecules_MoleculeId",
                table: "JobTypes",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserJoinRequests_Departments_DepartmentId",
                table: "UserJoinRequests",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobTypes_Molecules_MoleculeId",
                table: "JobTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserJoinRequests_Departments_DepartmentId",
                table: "UserJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_UserJoinRequests_DepartmentId",
                table: "UserJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_JobTypes_MoleculeId",
                table: "JobTypes");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "UserJoinRequests");

            migrationBuilder.DropColumn(
                name: "MoleculeId",
                table: "JobTypes");
        }
    }
}
