using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ExtendSwapRequestModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ToUserId",
                table: "SwapRequests",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "SwapRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FromUserId",
                table: "SwapRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "SwapRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "SwapRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedBy",
                table: "SwapRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewerId",
                table: "SwapRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToAssignmentId",
                table: "SwapRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SwapRequests_FromAssignmentId",
                table: "SwapRequests",
                column: "FromAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SwapRequests_FromUserId",
                table: "SwapRequests",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SwapRequests_ReviewerId",
                table: "SwapRequests",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_SwapRequests_ToAssignmentId",
                table: "SwapRequests",
                column: "ToAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SwapRequests_ToUserId",
                table: "SwapRequests",
                column: "ToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SwapRequests_Companies_CompanyId",
                table: "SwapRequests",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SwapRequests_ShiftAssignments_FromAssignmentId",
                table: "SwapRequests",
                column: "FromAssignmentId",
                principalTable: "ShiftAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SwapRequests_ShiftAssignments_ToAssignmentId",
                table: "SwapRequests",
                column: "ToAssignmentId",
                principalTable: "ShiftAssignments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SwapRequests_Users_FromUserId",
                table: "SwapRequests",
                column: "FromUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SwapRequests_Users_ReviewerId",
                table: "SwapRequests",
                column: "ReviewerId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SwapRequests_Users_ToUserId",
                table: "SwapRequests",
                column: "ToUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SwapRequests_Companies_CompanyId",
                table: "SwapRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SwapRequests_ShiftAssignments_FromAssignmentId",
                table: "SwapRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SwapRequests_ShiftAssignments_ToAssignmentId",
                table: "SwapRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SwapRequests_Users_FromUserId",
                table: "SwapRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SwapRequests_Users_ReviewerId",
                table: "SwapRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SwapRequests_Users_ToUserId",
                table: "SwapRequests");

            migrationBuilder.DropIndex(
                name: "IX_SwapRequests_FromAssignmentId",
                table: "SwapRequests");

            migrationBuilder.DropIndex(
                name: "IX_SwapRequests_FromUserId",
                table: "SwapRequests");

            migrationBuilder.DropIndex(
                name: "IX_SwapRequests_ReviewerId",
                table: "SwapRequests");

            migrationBuilder.DropIndex(
                name: "IX_SwapRequests_ToAssignmentId",
                table: "SwapRequests");

            migrationBuilder.DropIndex(
                name: "IX_SwapRequests_ToUserId",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "FromUserId",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "ReviewerId",
                table: "SwapRequests");

            migrationBuilder.DropColumn(
                name: "ToAssignmentId",
                table: "SwapRequests");

            migrationBuilder.AlterColumn<int>(
                name: "ToUserId",
                table: "SwapRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
