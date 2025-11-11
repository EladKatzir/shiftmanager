using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddOnDutyTypeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnDutyTypeConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeValue = table.Column<int>(type: "INTEGER", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    NameHe = table.Column<string>(type: "TEXT", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnDutyTypeConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnDutyTypeConfigs_TypeValue",
                table: "OnDutyTypeConfigs",
                column: "TypeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnDutyTypeConfigs_TypeValue_IsActive",
                table: "OnDutyTypeConfigs",
                columns: new[] { "TypeValue", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnDutyTypeConfigs");
        }
    }
}
