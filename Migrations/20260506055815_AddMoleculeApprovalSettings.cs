using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddMoleculeApprovalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoleculeApprovalSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MoleculeId = table.Column<int>(type: "INTEGER", nullable: false),
                    DualApprovalDayThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoleculeApprovalSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoleculeApprovalSettings_Molecules_MoleculeId",
                        column: x => x.MoleculeId,
                        principalTable: "Molecules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MoleculeApprovalSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoleculeApprovalSettings_MoleculeId",
                table: "MoleculeApprovalSettings",
                column: "MoleculeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoleculeApprovalSettings_UpdatedByUserId",
                table: "MoleculeApprovalSettings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoleculeApprovalSettings");
        }
    }
}
