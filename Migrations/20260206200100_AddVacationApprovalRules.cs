using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddVacationApprovalRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VacationApprovalRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApproverUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApproverGrantKey = table.Column<string>(type: "TEXT", nullable: false),
                    MaxAutoApproveDays = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresSecondApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtendedLeaveDaysThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    SecondApproverGrantKey = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationApprovalRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacationApprovalRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VacationApprovalRules_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VacationApprovalRules_Users_ApproverUserId",
                        column: x => x.ApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalRules_CompanyId_JobTypeId_Priority",
                table: "VacationApprovalRules",
                columns: new[] { "CompanyId", "JobTypeId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalRules_ApproverUserId",
                table: "VacationApprovalRules",
                column: "ApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalRules_JobTypeId",
                table: "VacationApprovalRules",
                column: "JobTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VacationApprovalRules");
        }
    }
}
