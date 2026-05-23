using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributionLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DistributionLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributionLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistributionLists_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DistributionLists_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DistributionLists_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DistributionListMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DistributionListId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributionListMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistributionListMembers_DistributionLists_DistributionListId",
                        column: x => x.DistributionListId,
                        principalTable: "DistributionLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DistributionListMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DistributionListMembers_DistributionListId_UserId",
                table: "DistributionListMembers",
                columns: new[] { "DistributionListId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DistributionListMembers_UserId",
                table: "DistributionListMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionLists_CompanyId",
                table: "DistributionLists",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionLists_CreatedBy",
                table: "DistributionLists",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionLists_MoleculeId_Name",
                table: "DistributionLists",
                columns: new[] { "MoleculeId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DistributionListMembers");

            migrationBuilder.DropTable(
                name: "DistributionLists");
        }
    }
}
