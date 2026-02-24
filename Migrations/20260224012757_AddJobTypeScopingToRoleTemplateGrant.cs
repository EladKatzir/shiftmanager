using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTypeScopingToRoleTemplateGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetJobTypeId",
                table: "RoleTemplateGrants",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseOwnJobType",
                table: "RoleTemplateGrants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetJobTypeId",
                table: "RoleTemplateGrants");

            migrationBuilder.DropColumn(
                name: "UseOwnJobType",
                table: "RoleTemplateGrants");
        }
    }
}
