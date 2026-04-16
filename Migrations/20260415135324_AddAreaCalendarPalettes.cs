using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaCalendarPalettes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: ThemeColor/ThemeMode columns are added by migration 20260415084229_ThemePreferences
            // which runs before this one. This migration is now strictly about AreaCalendarPalettes.
            migrationBuilder.CreateTable(
                name: "AreaCalendarPalettes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftMorning = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    ShiftAfternoon = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    ShiftNight = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    ShiftHome = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    OnDuty = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    Chore = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    Vacation = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AreaCalendarPalettes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AreaCalendarPalettes_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AreaCalendarPalettes_AreaId",
                table: "AreaCalendarPalettes",
                column: "AreaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: ThemeColor/ThemeMode columns are owned by 20260415084229_ThemePreferences — don't drop them here.
            migrationBuilder.DropTable(
                name: "AreaCalendarPalettes");
        }
    }
}
