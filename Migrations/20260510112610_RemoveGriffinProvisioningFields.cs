using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGriffinProvisioningFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GriffinConfigs_RoleTemplates_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs");

            migrationBuilder.DropIndex(
                name: "IX_GriffinConfigs_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs");

            migrationBuilder.DropColumn(
                name: "AutoProvisionUsers",
                table: "GriffinConfigs");

            migrationBuilder.DropColumn(
                name: "DefaultProvisionedRole",
                table: "GriffinConfigs");

            migrationBuilder.DropColumn(
                name: "DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoProvisionUsers",
                table: "GriffinConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DefaultProvisionedRole",
                table: "GriffinConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GriffinConfigs_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs",
                column: "DefaultProvisionedRoleTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_GriffinConfigs_RoleTemplates_DefaultProvisionedRoleTemplateId",
                table: "GriffinConfigs",
                column: "DefaultProvisionedRoleTemplateId",
                principalTable: "RoleTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
