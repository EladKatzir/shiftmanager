using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoleculeId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftGroupingId",
                table: "ShiftTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TechShiftType",
                table: "ShiftTypes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "ShiftPrograms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftGroupingId",
                table: "ShiftPrograms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TechShiftType",
                table: "ShiftPrograms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_JobTypeId",
                table: "ShiftTypes",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_MoleculeId",
                table: "ShiftTypes",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypes_ShiftGroupingId",
                table: "ShiftTypes",
                column: "ShiftGroupingId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPrograms_JobTypeId",
                table: "ShiftPrograms",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPrograms_ShiftGroupingId",
                table: "ShiftPrograms",
                column: "ShiftGroupingId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftPrograms_JobTypes_JobTypeId",
                table: "ShiftPrograms",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftPrograms_ShiftGroupings_ShiftGroupingId",
                table: "ShiftPrograms",
                column: "ShiftGroupingId",
                principalTable: "ShiftGroupings",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_JobTypes_JobTypeId",
                table: "ShiftTypes",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_Molecules_MoleculeId",
                table: "ShiftTypes",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTypes_ShiftGroupings_ShiftGroupingId",
                table: "ShiftTypes",
                column: "ShiftGroupingId",
                principalTable: "ShiftGroupings",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftPrograms_JobTypes_JobTypeId",
                table: "ShiftPrograms");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftPrograms_ShiftGroupings_ShiftGroupingId",
                table: "ShiftPrograms");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_JobTypes_JobTypeId",
                table: "ShiftTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_Molecules_MoleculeId",
                table: "ShiftTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTypes_ShiftGroupings_ShiftGroupingId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_JobTypeId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_MoleculeId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTypes_ShiftGroupingId",
                table: "ShiftTypes");

            migrationBuilder.DropIndex(
                name: "IX_ShiftPrograms_JobTypeId",
                table: "ShiftPrograms");

            migrationBuilder.DropIndex(
                name: "IX_ShiftPrograms_ShiftGroupingId",
                table: "ShiftPrograms");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "MoleculeId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "ShiftGroupingId",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "TechShiftType",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "ShiftPrograms");

            migrationBuilder.DropColumn(
                name: "ShiftGroupingId",
                table: "ShiftPrograms");

            migrationBuilder.DropColumn(
                name: "TechShiftType",
                table: "ShiftPrograms");
        }
    }
}
