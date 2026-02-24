using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRoleTemplateGrantUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoleTemplateGrants_RoleTemplateId_GrantTypeId",
                table: "RoleTemplateGrants");

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplateGrants_RoleTemplateId_GrantTypeId_TargetJobTypeId",
                table: "RoleTemplateGrants",
                columns: new[] { "RoleTemplateId", "GrantTypeId", "TargetJobTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoleTemplateGrants_RoleTemplateId_GrantTypeId_TargetJobTypeId",
                table: "RoleTemplateGrants");

            migrationBuilder.CreateIndex(
                name: "IX_RoleTemplateGrants_RoleTemplateId_GrantTypeId",
                table: "RoleTemplateGrants",
                columns: new[] { "RoleTemplateId", "GrantTypeId" },
                unique: true);
        }
    }
}
