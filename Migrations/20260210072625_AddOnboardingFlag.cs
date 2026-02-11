using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasCompletedOnboarding",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Mark all existing users as having completed onboarding
            // (only new users created after this migration should see the wizard)
            migrationBuilder.Sql("UPDATE Users SET HasCompletedOnboarding = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasCompletedOnboarding",
                table: "Users");
        }
    }
}
