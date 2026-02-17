using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicRoleTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoleTemplateId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedRoleTemplateId",
                table: "UserJoinRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanBeAssignedByDefault",
                table: "RoleTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DerivedUserRole",
                table: "RoleTemplates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameEN",
                table: "RoleTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameHE",
                table: "RoleTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleInSignup",
                table: "RoleTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "FromRoleTemplateId",
                table: "RoleAssignmentAudits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToRoleTemplateId",
                table: "RoleAssignmentAudits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoleTemplateJobTypeLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayNameEN = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayNameHE = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleTemplateJobTypeLabels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleTemplateJobTypeLabels_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleTemplateJobTypeLabels_RoleTemplates_RoleTemplateId",
                        column: x => x.RoleTemplateId,
                        principalTable: "RoleTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleTemplateId",
                table: "Users",
                column: "RoleTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_UserJoinRequests_RequestedRoleTemplateId",
                table: "UserJoinRequests",
                column: "RequestedRoleTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignmentAudits_FromRoleTemplateId",
                table: "RoleAssignmentAudits",
                column: "FromRoleTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignmentAudits_ToRoleTemplateId",
                table: "RoleAssignmentAudits",
                column: "ToRoleTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_GriffinConfigs_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs",
                column: "DefaultProvisionedRoleTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplateJobTypeLabels_JobTypeId",
                table: "RoleTemplateJobTypeLabels",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplateJobTypeLabels_RoleTemplateId_JobTypeId",
                table: "RoleTemplateJobTypeLabels",
                columns: new[] { "RoleTemplateId", "JobTypeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GriffinConfigs_RoleTemplates_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs",
                column: "DefaultProvisionedRoleTemplateId",
                principalTable: "RoleTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleAssignmentAudits_RoleTemplates_FromRoleTemplateId",
                table: "RoleAssignmentAudits",
                column: "FromRoleTemplateId",
                principalTable: "RoleTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleAssignmentAudits_RoleTemplates_ToRoleTemplateId",
                table: "RoleAssignmentAudits",
                column: "ToRoleTemplateId",
                principalTable: "RoleTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserJoinRequests_RoleTemplates_RequestedRoleTemplateId",
                table: "UserJoinRequests",
                column: "RequestedRoleTemplateId",
                principalTable: "RoleTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_RoleTemplates_RoleTemplateId",
                table: "Users",
                column: "RoleTemplateId",
                principalTable: "RoleTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill RoleTemplateId for existing users using same mapping as MapUserRoleToRoleTemplateKey
            migrationBuilder.Sql(@"
UPDATE Users SET RoleTemplateId = (
    SELECT rt.Id FROM RoleTemplates rt WHERE rt.Key = CASE
        WHEN Users.Role = 0 THEN 'Owner'
        WHEN Users.Role = 5 THEN 'Assigner'
        WHEN Users.Role = 6 THEN 'AreaAdmin'
        WHEN Users.Role = 2 THEN 'Employee'
        WHEN Users.Role = 4 THEN 'Trainee'
        WHEN Users.Role = 1 THEN (
            SELECT CASE jt.Name
                WHEN 'Alhut' THEN 'AlhutLead'
                WHEN 'Text' THEN 'TextLead'
                ELSE 'BRDirector'
            END FROM JobTypes jt WHERE jt.Id = Users.JobTypeId
        )
        WHEN Users.Role = 3 THEN (
            SELECT CASE jt.Name
                WHEN 'Alhut' THEN 'AlhutDirector'
                WHEN 'Text' THEN 'TextDirector'
                ELSE 'MoleculeAdmin'
            END FROM JobTypes jt WHERE jt.Id = Users.JobTypeId
        )
        ELSE 'Employee'
    END
)
WHERE RoleTemplateId IS NULL;
");

            // Fallback for users with NULL JobTypeId (Manager/Director without a job type)
            migrationBuilder.Sql(@"
UPDATE Users SET RoleTemplateId = (
    SELECT rt.Id FROM RoleTemplates rt WHERE rt.Key = CASE
        WHEN Users.Role = 1 THEN 'BRDirector'
        WHEN Users.Role = 3 THEN 'MoleculeAdmin'
    END
)
WHERE RoleTemplateId IS NULL AND Users.Role IN (1, 3);
");

            // Final catch-all: any remaining users without a RoleTemplateId get Employee template
            migrationBuilder.Sql(@"
UPDATE Users SET RoleTemplateId = (
    SELECT rt.Id FROM RoleTemplates rt WHERE rt.Key = 'Employee'
)
WHERE RoleTemplateId IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GriffinConfigs_RoleTemplates_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleAssignmentAudits_RoleTemplates_FromRoleTemplateId",
                table: "RoleAssignmentAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleAssignmentAudits_RoleTemplates_ToRoleTemplateId",
                table: "RoleAssignmentAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_UserJoinRequests_RoleTemplates_RequestedRoleTemplateId",
                table: "UserJoinRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_RoleTemplates_RoleTemplateId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "RoleTemplateJobTypeLabels");

            migrationBuilder.DropIndex(
                name: "IX_Users_RoleTemplateId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UserJoinRequests_RequestedRoleTemplateId",
                table: "UserJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignmentAudits_FromRoleTemplateId",
                table: "RoleAssignmentAudits");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignmentAudits_ToRoleTemplateId",
                table: "RoleAssignmentAudits");

            migrationBuilder.DropIndex(
                name: "IX_GriffinConfigs_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs");

            migrationBuilder.DropColumn(
                name: "RoleTemplateId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RequestedRoleTemplateId",
                table: "UserJoinRequests");

            migrationBuilder.DropColumn(
                name: "CanBeAssignedByDefault",
                table: "RoleTemplates");

            migrationBuilder.DropColumn(
                name: "DerivedUserRole",
                table: "RoleTemplates");

            migrationBuilder.DropColumn(
                name: "DisplayNameEN",
                table: "RoleTemplates");

            migrationBuilder.DropColumn(
                name: "DisplayNameHE",
                table: "RoleTemplates");

            migrationBuilder.DropColumn(
                name: "IsVisibleInSignup",
                table: "RoleTemplates");

            migrationBuilder.DropColumn(
                name: "FromRoleTemplateId",
                table: "RoleAssignmentAudits");

            migrationBuilder.DropColumn(
                name: "ToRoleTemplateId",
                table: "RoleAssignmentAudits");

            migrationBuilder.DropColumn(
                name: "DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs");
        }
    }
}
