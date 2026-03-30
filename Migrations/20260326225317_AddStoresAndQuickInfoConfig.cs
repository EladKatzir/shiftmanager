using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddStoresAndQuickInfoConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create tables if they don't exist (lost during git reset; exist on production
            // databases but not on fresh databases where all migrations run from scratch).

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""Stores"" (
    ""Id""        INTEGER NOT NULL CONSTRAINT ""PK_Stores"" PRIMARY KEY AUTOINCREMENT,
    ""AreaId""    INTEGER NOT NULL,
    ""NameEn""    TEXT    NOT NULL,
    ""NameHe""    TEXT    NULL,
    ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
    ""IsActive""  INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT ""FK_Stores_Areas_AreaId"" FOREIGN KEY (""AreaId"") REFERENCES ""Areas"" (""Id"") ON DELETE CASCADE
);");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Stores_AreaId\" ON \"Stores\" (\"AreaId\");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""StoreHoursEntries"" (
    ""Id""        INTEGER NOT NULL CONSTRAINT ""PK_StoreHoursEntries"" PRIMARY KEY AUTOINCREMENT,
    ""StoreId""   INTEGER NOT NULL,
    ""DayOfWeek"" INTEGER NOT NULL,
    ""OpenTime""  TEXT    NOT NULL,
    ""CloseTime"" TEXT    NOT NULL,
    ""IsActive""  INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT ""FK_StoreHoursEntries_Stores_StoreId"" FOREIGN KEY (""StoreId"") REFERENCES ""Stores"" (""Id"") ON DELETE CASCADE
);");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_StoreHoursEntries_StoreId\" ON \"StoreHoursEntries\" (\"StoreId\");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""QuickInfoConfigs"" (
    ""Id""           INTEGER NOT NULL CONSTRAINT ""PK_QuickInfoConfigs"" PRIMARY KEY AUTOINCREMENT,
    ""MoleculeId""   INTEGER NOT NULL,
    ""SectionType""  INTEGER NOT NULL,
    ""EntityId""     INTEGER NOT NULL,
    ""DisplayOrder"" INTEGER NOT NULL DEFAULT 0,
    ""IsEnabled""    INTEGER NOT NULL DEFAULT 1,
    ""CreatedBy""    INTEGER NOT NULL DEFAULT 0,
    ""CreatedAt""    TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
    CONSTRAINT ""FK_QuickInfoConfigs_Molecules_MoleculeId"" FOREIGN KEY (""MoleculeId"") REFERENCES ""Molecules"" (""Id"") ON DELETE CASCADE
);");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_QuickInfoConfigs_MoleculeId_IsEnabled\" ON \"QuickInfoConfigs\" (\"MoleculeId\", \"IsEnabled\");");

            // Original migration: change FK behavior from Cascade to Restrict
            migrationBuilder.DropForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores");

            migrationBuilder.AddForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores");

            migrationBuilder.AddForeignKey(
                name: "FK_QuickInfoConfigs_Molecules_MoleculeId",
                table: "QuickInfoConfigs",
                column: "MoleculeId",
                principalTable: "Molecules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Areas_AreaId",
                table: "Stores",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
