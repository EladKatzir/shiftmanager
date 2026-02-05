using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ChangeShiftCapacityOverrideDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftCapacityOverrides_ShiftTypes_ShiftTypeId",
                table: "ShiftCapacityOverrides");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftCapacityOverrides_ShiftTypes_ShiftTypeId",
                table: "ShiftCapacityOverrides",
                column: "ShiftTypeId",
                principalTable: "ShiftTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftCapacityOverrides_ShiftTypes_ShiftTypeId",
                table: "ShiftCapacityOverrides");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftCapacityOverrides_ShiftTypes_ShiftTypeId",
                table: "ShiftCapacityOverrides",
                column: "ShiftTypeId",
                principalTable: "ShiftTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
