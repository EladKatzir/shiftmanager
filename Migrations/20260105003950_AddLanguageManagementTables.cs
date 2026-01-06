using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageManagementTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyLanguageSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultCulture = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AlternateCulture = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLanguageSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyLanguageSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyLanguageSettings_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyLanguageSettings_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyLocalizationOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Culture = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ResourceKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OverrideValue = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLocalizationOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyLocalizationOverrides_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyLocalizationOverrides_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyLocalizationOverrides_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLanguageSettings_CompanyId",
                table: "CompanyLanguageSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLanguageSettings_CreatedBy",
                table: "CompanyLanguageSettings",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLanguageSettings_UpdatedBy",
                table: "CompanyLanguageSettings",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLocalizationOverrides_CompanyId_Culture",
                table: "CompanyLocalizationOverrides",
                columns: new[] { "CompanyId", "Culture" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLocalizationOverrides_CompanyId_Culture_ResourceKey",
                table: "CompanyLocalizationOverrides",
                columns: new[] { "CompanyId", "Culture", "ResourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLocalizationOverrides_CreatedBy",
                table: "CompanyLocalizationOverrides",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLocalizationOverrides_UpdatedBy",
                table: "CompanyLocalizationOverrides",
                column: "UpdatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyLanguageSettings");

            migrationBuilder.DropTable(
                name: "CompanyLocalizationOverrides");
        }
    }
}
