using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddJusticeTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JusticeTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpectedCount = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    PeriodKind = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JusticeTargets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_Date_CanceledAt",
                table: "OnDuties",
                columns: new[] { "Date", "CanceledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_Date_UserId",
                table: "OnDuties",
                columns: new[] { "Date", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_JusticeTargets_ScopeKind_ScopeId",
                table: "JusticeTargets",
                columns: new[] { "ScopeKind", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_JusticeTargets_WorkType_ScopeKind_ScopeId",
                table: "JusticeTargets",
                columns: new[] { "WorkType", "ScopeKind", "ScopeId" },
                unique: true);

            // Default global per-user targets: 3 chores/month/user, 2 on-duties/month/user.
            // Shifts are capacity-driven (no target row needed; expected = sum(StaffingRequired)).
            // CreatedByUserId = null marks these as system-seeded.
            // Enum values: WorkType (Chore=2, OnDuty=3), ScopeKind (Global=0), PeriodKind (PerMonth=1).
            var seedDate = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "JusticeTargets",
                columns: new[] { "CompanyId", "WorkType", "ScopeKind", "ScopeId", "ExpectedCount", "PeriodKind", "Note", "CreatedByUserId", "UpdatedAt" },
                values: new object[,]
                {
                    { null!, 2, 0, null!, 3.00m, 1, null!, null!, seedDate },
                    { null!, 3, 0, null!, 2.00m, 1, null!, null!, seedDate }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JusticeTargets");

            migrationBuilder.DropIndex(
                name: "IX_OnDuties_Date_CanceledAt",
                table: "OnDuties");

            migrationBuilder.DropIndex(
                name: "IX_OnDuties_Date_UserId",
                table: "OnDuties");
        }
    }
}
