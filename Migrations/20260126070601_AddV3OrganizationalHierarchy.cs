using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddV3OrganizationalHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Department",
                table: "Users",
                newName: "LegacyDepartment");

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JobTypeId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoleculeId",
                table: "Companies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    RestHoursOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    WeeklyCapOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanySettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanySettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GrantTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionKey = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultScope = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrantTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrantTypes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionKey = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserFriendships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    FriendId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFriendships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFriendships_Users_FriendId",
                        column: x => x.FriendId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserFriendships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Areas_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleTemplateGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    CanOwn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanGive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScopeMode = table.Column<int>(type: "INTEGER", nullable: false),
                    IsOverride = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleTemplateGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleTemplateGrants_GrantTypes_GrantTypeId",
                        column: x => x.GrantTypeId,
                        principalTable: "GrantTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleTemplateGrants_RoleTemplates_RoleTemplateId",
                        column: x => x.RoleTemplateId,
                        principalTable: "RoleTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AreaSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultRestHours = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultWeeklyCap = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AreaSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AreaSettings_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AreaSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobTypes_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Molecules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Molecules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Molecules_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MoleculeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    RestHoursOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    WeeklyCapOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoleculeSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoleculeSettings_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MoleculeSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SetupTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: true),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestedUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestionReason = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedToUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetupTasks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetupTasks_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetupTasks_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetupTasks_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetupTasks_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetupTasks_Users_SuggestedUserId",
                        column: x => x.SuggestedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftGroupings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftGroupings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftGroupings_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Grants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: true),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: true),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    CanOwn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanGive = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsAutoGrant = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Grants_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_GrantTypes_GrantTypeId",
                        column: x => x.GrantTypeId,
                        principalTable: "GrantTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Grants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: true),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: true),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_RoleTemplates_RoleTemplateId",
                        column: x => x.RoleTemplateId,
                        principalTable: "RoleTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftGroupingCompanies",
                columns: table => new
                {
                    ShiftGroupingId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftGroupingCompanies", x => new { x.ShiftGroupingId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_ShiftGroupingCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftGroupingCompanies_ShiftGroupings_ShiftGroupingId",
                        column: x => x.ShiftGroupingId,
                        principalTable: "ShiftGroupings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftGroupingJobTypes",
                columns: table => new
                {
                    ShiftGroupingId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobTypeId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftGroupingJobTypes", x => new { x.ShiftGroupingId, x.JobTypeId });
                    table.ForeignKey(
                        name: "FK_ShiftGroupingJobTypes_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftGroupingJobTypes_ShiftGroupings_ShiftGroupingId",
                        column: x => x.ShiftGroupingId,
                        principalTable: "ShiftGroupings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_JobTypeId",
                table: "Users",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_MoleculeId",
                table: "Companies",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_ProjectId",
                table: "Areas",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AreaSettings_AreaId",
                table: "AreaSettings",
                column: "AreaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AreaSettings_UpdatedByUserId",
                table: "AreaSettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_CompanyId",
                table: "CompanySettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_UpdatedByUserId",
                table: "CompanySettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_MoleculeId",
                table: "Departments",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_AreaId",
                table: "Grants",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_CompanyId",
                table: "Grants",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_DepartmentId",
                table: "Grants",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_GrantedByUserId",
                table: "Grants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_GrantTypeId",
                table: "Grants",
                column: "GrantTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_JobTypeId",
                table: "Grants",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_MoleculeId",
                table: "Grants",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_ProjectId",
                table: "Grants",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Grants_UserId_GrantTypeId",
                table: "Grants",
                columns: new[] { "UserId", "GrantTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_GrantTypes_CreatedByUserId",
                table: "GrantTypes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GrantTypes_Key",
                table: "GrantTypes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTypes_AreaId",
                table: "JobTypes",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Molecules_AreaId",
                table: "Molecules",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_MoleculeSettings_MoleculeId",
                table: "MoleculeSettings",
                column: "MoleculeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoleculeSettings_UpdatedByUserId",
                table: "MoleculeSettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplateGrants_GrantTypeId",
                table: "RoleTemplateGrants",
                column: "GrantTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplateGrants_RoleTemplateId_GrantTypeId",
                table: "RoleTemplateGrants",
                columns: new[] { "RoleTemplateId", "GrantTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplates_Key",
                table: "RoleTemplates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SetupTasks_AssignedToUserId_Status",
                table: "SetupTasks",
                columns: new[] { "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SetupTasks_CompanyId",
                table: "SetupTasks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SetupTasks_CompletedByUserId",
                table: "SetupTasks",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SetupTasks_JobTypeId",
                table: "SetupTasks",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_SetupTasks_MoleculeId_Status",
                table: "SetupTasks",
                columns: new[] { "MoleculeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SetupTasks_SuggestedUserId",
                table: "SetupTasks",
                column: "SuggestedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupingCompanies_CompanyId",
                table: "ShiftGroupingCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupingJobTypes_JobTypeId",
                table: "ShiftGroupingJobTypes",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroupings_MoleculeId",
                table: "ShiftGroupings",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFriendships_FriendId",
                table: "UserFriendships",
                column: "FriendId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFriendships_UserId_FriendId",
                table: "UserFriendships",
                columns: new[] { "UserId", "FriendId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_AreaId",
                table: "UserRoleAssignments",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_AssignedByUserId",
                table: "UserRoleAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_CompanyId",
                table: "UserRoleAssignments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_DepartmentId",
                table: "UserRoleAssignments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_JobTypeId",
                table: "UserRoleAssignments",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_MoleculeId",
                table: "UserRoleAssignments",
                column: "MoleculeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_RoleTemplateId",
                table: "UserRoleAssignments",
                column: "RoleTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_UserId_RoleTemplateId_IsActive",
                table: "UserRoleAssignments",
                columns: new[] { "UserId", "RoleTemplateId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Molecules_MoleculeId",
                table: "Companies",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_JobTypes_JobTypeId",
                table: "Users",
                column: "JobTypeId",
                principalTable: "JobTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Molecules_MoleculeId",
                table: "Companies");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_JobTypes_JobTypeId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "AreaSettings");

            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "Grants");

            migrationBuilder.DropTable(
                name: "MoleculeSettings");

            migrationBuilder.DropTable(
                name: "RoleTemplateGrants");

            migrationBuilder.DropTable(
                name: "SetupTasks");

            migrationBuilder.DropTable(
                name: "ShiftGroupingCompanies");

            migrationBuilder.DropTable(
                name: "ShiftGroupingJobTypes");

            migrationBuilder.DropTable(
                name: "UserFriendships");

            migrationBuilder.DropTable(
                name: "UserRoleAssignments");

            migrationBuilder.DropTable(
                name: "GrantTypes");

            migrationBuilder.DropTable(
                name: "ShiftGroupings");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "JobTypes");

            migrationBuilder.DropTable(
                name: "RoleTemplates");

            migrationBuilder.DropTable(
                name: "Molecules");

            migrationBuilder.DropTable(
                name: "Areas");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Users_DepartmentId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_JobTypeId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Companies_MoleculeId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MoleculeId",
                table: "Companies");

            migrationBuilder.RenameColumn(
                name: "LegacyDepartment",
                table: "Users",
                newName: "Department");
        }
    }
}
