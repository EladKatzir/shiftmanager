using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class FixIndexFiltersAndRemoveOrphanedShadowFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChoreTypes_Molecules_MoleculeId1",
                table: "ChoreTypes");

            migrationBuilder.DropIndex(
                name: "IX_TeamCalendars_CompanyId_OwnerId_Name",
                table: "TeamCalendars");

            migrationBuilder.DropIndex(
                name: "IX_ShiftGroupings_MoleculeId_JobTypeId_Name",
                table: "ShiftGroupings");

            migrationBuilder.DropIndex(
                name: "IX_OnDutyRoleSubscriptions_CompanyId_UserId_OnDutyTypeValue",
                table: "OnDutyRoleSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_OnDuties_UserId_Date_Type_CanceledAt",
                table: "OnDuties");

            migrationBuilder.DropIndex(
                name: "IX_DirectorCompanies_UserId_CompanyId",
                table: "DirectorCompanies");

            migrationBuilder.DropIndex(
                name: "IX_DailyNotificationPreferences_CompanyId_UserId",
                table: "DailyNotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_ChoreTypes_MoleculeId1",
                table: "ChoreTypes");

            migrationBuilder.DropIndex(
                name: "IX_Chores_CompanyId_UserId_Date_CanceledAt",
                table: "Chores");

            migrationBuilder.DropColumn(
                name: "MoleculeId1",
                table: "ChoreTypes");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendars_CompanyId_OwnerId_Name",
                table: "TeamCalendars",
                columns: new[] { "CompanyId", "OwnerId", "Name" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupings_MoleculeId_JobTypeId_Name",
                table: "ShiftGroupings",
                columns: new[] { "MoleculeId", "JobTypeId", "Name" },
                unique: true,
                filter: "JobTypeId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OnDutyRoleSubscriptions_CompanyId_UserId_OnDutyTypeValue",
                table: "OnDutyRoleSubscriptions",
                columns: new[] { "CompanyId", "UserId", "OnDutyTypeValue" },
                unique: true,
                filter: "IsActive = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_UserId_Date_Type_CanceledAt",
                table: "OnDuties",
                columns: new[] { "UserId", "Date", "Type", "CanceledAt" },
                unique: true,
                filter: "CanceledAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DirectorCompanies_UserId_CompanyId",
                table: "DirectorCompanies",
                columns: new[] { "UserId", "CompanyId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DailyNotificationPreferences_CompanyId_UserId",
                table: "DailyNotificationPreferences",
                columns: new[] { "CompanyId", "UserId" },
                unique: true,
                filter: "IsActive = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Chores_CompanyId_UserId_Date_CanceledAt",
                table: "Chores",
                columns: new[] { "CompanyId", "UserId", "Date", "CanceledAt" },
                unique: true,
                filter: "CanceledAt IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamCalendars_CompanyId_OwnerId_Name",
                table: "TeamCalendars");

            migrationBuilder.DropIndex(
                name: "IX_ShiftGroupings_MoleculeId_JobTypeId_Name",
                table: "ShiftGroupings");

            migrationBuilder.DropIndex(
                name: "IX_OnDutyRoleSubscriptions_CompanyId_UserId_OnDutyTypeValue",
                table: "OnDutyRoleSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_OnDuties_UserId_Date_Type_CanceledAt",
                table: "OnDuties");

            migrationBuilder.DropIndex(
                name: "IX_DirectorCompanies_UserId_CompanyId",
                table: "DirectorCompanies");

            migrationBuilder.DropIndex(
                name: "IX_DailyNotificationPreferences_CompanyId_UserId",
                table: "DailyNotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_Chores_CompanyId_UserId_Date_CanceledAt",
                table: "Chores");

            migrationBuilder.AddColumn<int>(
                name: "MoleculeId1",
                table: "ChoreTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamCalendars_CompanyId_OwnerId_Name",
                table: "TeamCalendars",
                columns: new[] { "CompanyId", "OwnerId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupings_MoleculeId_JobTypeId_Name",
                table: "ShiftGroupings",
                columns: new[] { "MoleculeId", "JobTypeId", "Name" },
                unique: true,
                filter: "[JobTypeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OnDutyRoleSubscriptions_CompanyId_UserId_OnDutyTypeValue",
                table: "OnDutyRoleSubscriptions",
                columns: new[] { "CompanyId", "UserId", "OnDutyTypeValue" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_UserId_Date_Type_CanceledAt",
                table: "OnDuties",
                columns: new[] { "UserId", "Date", "Type", "CanceledAt" },
                unique: true,
                filter: "[CanceledAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DirectorCompanies_UserId_CompanyId",
                table: "DirectorCompanies",
                columns: new[] { "UserId", "CompanyId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DailyNotificationPreferences_CompanyId_UserId",
                table: "DailyNotificationPreferences",
                columns: new[] { "CompanyId", "UserId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTypes_MoleculeId1",
                table: "ChoreTypes",
                column: "MoleculeId1");

            migrationBuilder.CreateIndex(
                name: "IX_Chores_CompanyId_UserId_Date_CanceledAt",
                table: "Chores",
                columns: new[] { "CompanyId", "UserId", "Date", "CanceledAt" },
                unique: true,
                filter: "[CanceledAt] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreTypes_Molecules_MoleculeId1",
                table: "ChoreTypes",
                column: "MoleculeId1",
                principalTable: "Molecules",
                principalColumn: "Id");
        }
    }
}
