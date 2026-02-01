using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddOnDutyAndAssignerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CanceledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CanceledBy = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnDuties_Users_CanceledBy",
                        column: x => x.CanceledBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnDuties_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnDuties_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_CanceledBy",
                table: "OnDuties",
                column: "CanceledBy");

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_CreatedBy",
                table: "OnDuties",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_Date",
                table: "OnDuties",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_Date_Type",
                table: "OnDuties",
                columns: new[] { "Date", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_UserId_Date",
                table: "OnDuties",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_OnDuties_UserId_Date_Type_CanceledAt",
                table: "OnDuties",
                columns: new[] { "UserId", "Date", "Type", "CanceledAt" },
                unique: true,
                filter: "[CanceledAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnDuties");
        }
    }
}
