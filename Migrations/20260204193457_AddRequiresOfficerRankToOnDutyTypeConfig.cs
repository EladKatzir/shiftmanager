using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddRequiresOfficerRankToOnDutyTypeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresOfficerRank",
                table: "OnDutyTypeConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresOfficerRank",
                table: "OnDutyTypeConfigs");
        }
    }
}
