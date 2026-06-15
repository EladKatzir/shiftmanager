using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddChoreFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DoesChores",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChoreCategoryId",
                table: "ChoreTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultWeightMinutes",
                table: "ChoreTypes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeightMinutes",
                table: "Chores",
                type: "INTEGER",
                nullable: false,
                defaultValue: 480);

            migrationBuilder.CreateTable(
                name: "ChoreCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: true),
                    NameHe = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreCategories_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChoreTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ChoreTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    DefaultTitle = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<string>(type: "TEXT", nullable: true),
                    EndTime = table.Column<string>(type: "TEXT", nullable: true),
                    WeightMinutesOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreTemplates_ChoreTypes_ChoreTypeId",
                        column: x => x.ChoreTypeId,
                        principalTable: "ChoreTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChoreTemplates_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChoreTemplates_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EligibilityRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChoreTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleKind = table.Column<int>(type: "INTEGER", nullable: false),
                    GenderValue = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EligibilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EligibilityRules_ChoreTypes_ChoreTypeId",
                        column: x => x.ChoreTypeId,
                        principalTable: "ChoreTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EligibilityRules_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserChoreExemptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChoreTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserChoreExemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserChoreExemptions_ChoreTypes_ChoreTypeId",
                        column: x => x.ChoreTypeId,
                        principalTable: "ChoreTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserChoreExemptions_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserChoreExemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserChoreCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChoreCategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserChoreCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserChoreCategories_ChoreCategories_ChoreCategoryId",
                        column: x => x.ChoreCategoryId,
                        principalTable: "ChoreCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserChoreCategories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTypes_ChoreCategoryId",
                table: "ChoreTypes",
                column: "ChoreCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreCategories_MoleculeId_Name",
                table: "ChoreCategories",
                columns: new[] { "MoleculeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTemplates_ChoreTypeId",
                table: "ChoreTemplates",
                column: "ChoreTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTemplates_CreatedBy",
                table: "ChoreTemplates",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreTemplates_MoleculeId_IsActive",
                table: "ChoreTemplates",
                columns: new[] { "MoleculeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EligibilityRules_ChoreTypeId",
                table: "EligibilityRules",
                column: "ChoreTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EligibilityRules_CreatedBy",
                table: "EligibilityRules",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserChoreCategories_ChoreCategoryId",
                table: "UserChoreCategories",
                column: "ChoreCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChoreCategories_UserId_ChoreCategoryId",
                table: "UserChoreCategories",
                columns: new[] { "UserId", "ChoreCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserChoreExemptions_ChoreTypeId",
                table: "UserChoreExemptions",
                column: "ChoreTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChoreExemptions_CreatedBy",
                table: "UserChoreExemptions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserChoreExemptions_UserId_ChoreTypeId",
                table: "UserChoreExemptions",
                columns: new[] { "UserId", "ChoreTypeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChoreTypes_ChoreCategories_ChoreCategoryId",
                table: "ChoreTypes",
                column: "ChoreCategoryId",
                principalTable: "ChoreCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChoreTypes_ChoreCategories_ChoreCategoryId",
                table: "ChoreTypes");

            migrationBuilder.DropTable(
                name: "ChoreTemplates");

            migrationBuilder.DropTable(
                name: "EligibilityRules");

            migrationBuilder.DropTable(
                name: "UserChoreCategories");

            migrationBuilder.DropTable(
                name: "UserChoreExemptions");

            migrationBuilder.DropTable(
                name: "ChoreCategories");

            migrationBuilder.DropIndex(
                name: "IX_ChoreTypes_ChoreCategoryId",
                table: "ChoreTypes");

            migrationBuilder.DropColumn(
                name: "DoesChores",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ChoreCategoryId",
                table: "ChoreTypes");

            migrationBuilder.DropColumn(
                name: "DefaultWeightMinutes",
                table: "ChoreTypes");

            migrationBuilder.DropColumn(
                name: "WeightMinutes",
                table: "Chores");
        }
    }
}
