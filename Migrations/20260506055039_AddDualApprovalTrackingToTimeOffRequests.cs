using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDualApprovalTrackingToTimeOffRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstApprovalActedAt",
                table: "TimeOffRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirstApprovalActorId",
                table: "TimeOffRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SecondApprovalActedAt",
                table: "TimeOffRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondApprovalActorId",
                table: "TimeOffRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_FirstApprovalActorId",
                table: "TimeOffRequests",
                column: "FirstApprovalActorId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_SecondApprovalActorId",
                table: "TimeOffRequests",
                column: "SecondApprovalActorId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeOffRequests_Users_FirstApprovalActorId",
                table: "TimeOffRequests",
                column: "FirstApprovalActorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeOffRequests_Users_SecondApprovalActorId",
                table: "TimeOffRequests",
                column: "SecondApprovalActorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeOffRequests_Users_FirstApprovalActorId",
                table: "TimeOffRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeOffRequests_Users_SecondApprovalActorId",
                table: "TimeOffRequests");

            migrationBuilder.DropIndex(
                name: "IX_TimeOffRequests_FirstApprovalActorId",
                table: "TimeOffRequests");

            migrationBuilder.DropIndex(
                name: "IX_TimeOffRequests_SecondApprovalActorId",
                table: "TimeOffRequests");

            migrationBuilder.DropColumn(
                name: "FirstApprovalActedAt",
                table: "TimeOffRequests");

            migrationBuilder.DropColumn(
                name: "FirstApprovalActorId",
                table: "TimeOffRequests");

            migrationBuilder.DropColumn(
                name: "SecondApprovalActedAt",
                table: "TimeOffRequests");

            migrationBuilder.DropColumn(
                name: "SecondApprovalActorId",
                table: "TimeOffRequests");
        }
    }
}
