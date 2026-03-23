using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeRotationSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeTypeId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HomeTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameHe = table.Column<string>(type: "TEXT", nullable: true),
                    PatternJson = table.Column<string>(type: "TEXT", nullable: true),
                    DerivedRule = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultStartTime = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultEndTime = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeTypes_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HomeTypes_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HomeTypeOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    OverridePatternJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeTypeOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeTypeOverrides_HomeTypes_HomeTypeId",
                        column: x => x.HomeTypeId,
                        principalTable: "HomeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HomeTypeOverrides_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HomeTypeOverrides_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_HomeTypeId",
                table: "Users",
                column: "HomeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeTypeOverrides_CreatedBy",
                table: "HomeTypeOverrides",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_HomeTypeOverrides_HomeTypeId_UserId",
                table: "HomeTypeOverrides",
                columns: new[] { "HomeTypeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HomeTypeOverrides_UserId",
                table: "HomeTypeOverrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeTypes_CreatedBy",
                table: "HomeTypes",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_HomeTypes_MoleculeId",
                table: "HomeTypes",
                column: "MoleculeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_HomeTypes_HomeTypeId",
                table: "Users",
                column: "HomeTypeId",
                principalTable: "HomeTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_HomeTypes_HomeTypeId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "HomeTypeOverrides");

            migrationBuilder.DropTable(
                name: "HomeTypes");

            migrationBuilder.DropIndex(
                name: "IX_Users_HomeTypeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HomeTypeId",
                table: "Users");
        }
    }
}
