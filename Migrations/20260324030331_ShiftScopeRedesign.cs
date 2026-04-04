using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class ShiftScopeRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core 9 SQLite: RenameColumn/AlterColumn/AddColumn/AddForeignKey/AddCheckConstraint
            // all trigger table rebuilds that conflict with each other when combined in one migration.
            // Fix: manual SQLite table rebuild via raw SQL, split into separate statements.

            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_CompanyId_Key\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_MoleculeId_JobTypeId_Key\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_JobTypeId\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_MoleculeId\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_ShiftGroupingId\";", suppressTransaction: true);

            // Guard against leftover temp table from EF Core's internal table rebuilds
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"ef_temp_ShiftTypes\";", suppressTransaction: true);

            // Create temp table with final schema
            migrationBuilder.Sql(@"
CREATE TABLE ""ef_temp_ShiftTypes"" (
    ""Id""                   INTEGER NOT NULL CONSTRAINT ""PK_ShiftTypes"" PRIMARY KEY AUTOINCREMENT,
    ""Key""                  TEXT    NOT NULL,
    ""Start""                TEXT    NOT NULL,
    ""End""                  TEXT    NOT NULL,
    ""CompanyId""            INTEGER NULL,
    ""NameEn""               TEXT    NULL,
    ""NameHe""               TEXT    NULL,
    ""NameKey""              TEXT    NULL,
    ""RowColor""             TEXT    NULL,
    ""AreaId""               INTEGER NULL,
    ""Scope""                INTEGER NOT NULL DEFAULT 1,
    ""JobTypeId""            INTEGER NULL,
    ""MoleculeId""           INTEGER NULL,
    ""ShiftGroupingId""      INTEGER NULL,
    ""TechShiftType""        TEXT    NULL,
    ""EligibleCompanyIds""   TEXT    NULL,
    ""RequiresOfficerRank""  INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT ""FK_ShiftTypes_Areas_AreaId""                       FOREIGN KEY (""AreaId"")          REFERENCES ""Areas""          (""Id""),
    CONSTRAINT ""FK_ShiftTypes_Companies_CompanyId""                FOREIGN KEY (""CompanyId"")       REFERENCES ""Companies""      (""Id""),
    CONSTRAINT ""FK_ShiftTypes_JobTypes_JobTypeId""                 FOREIGN KEY (""JobTypeId"")       REFERENCES ""JobTypes""       (""Id""),
    CONSTRAINT ""FK_ShiftTypes_Molecules_MoleculeId""               FOREIGN KEY (""MoleculeId"")      REFERENCES ""Molecules""      (""Id""),
    CONSTRAINT ""FK_ShiftTypes_ShiftGroupings_ShiftGroupingId""     FOREIGN KEY (""ShiftGroupingId"") REFERENCES ""ShiftGroupings"" (""Id""),
    CONSTRAINT ""CK_ShiftType_Area_Scope""     CHECK (""Scope"" != 2 OR ""AreaId"" IS NOT NULL),
    CONSTRAINT ""CK_ShiftType_Company_Scope""  CHECK (""Scope"" != 0 OR ""CompanyId"" IS NOT NULL),
    CONSTRAINT ""CK_ShiftType_Molecule_Scope"" CHECK (""Scope"" != 1 OR ""MoleculeId"" IS NOT NULL)
);", suppressTransaction: true);

            // Copy data: CustomName maps to NameEn, new columns (AreaId, NameHe) default to NULL.
            // Scope is derived from existing FK columns to satisfy CHECK constraints:
            //   Scope 1 (Molecule) if MoleculeId is set, else Scope 0 (Company).
            //   AreaId (Scope 2) doesn't exist in the source table — it's new in this migration.
            // NameKey added by OpsConsoleScheduler (20260109), RowColor by AddExcelCalendarTables (20260205),
            // EligibleCompanyIds+RequiresOfficerRank by TechMoleculeConvergence (20260314) — all via explicit AddColumn.
            migrationBuilder.Sql(@"
INSERT INTO ""ef_temp_ShiftTypes""
    (""Id"", ""Key"", ""Start"", ""End"", ""CompanyId"", ""NameEn"", ""NameKey"", ""RowColor"",
     ""Scope"", ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank"")
SELECT
     ""Id"", ""Key"", ""Start"", ""End"", ""CompanyId"", ""CustomName"", ""NameKey"", ""RowColor"",
     CASE WHEN ""MoleculeId"" IS NOT NULL THEN 1 ELSE 0 END,
     ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank""
FROM ""ShiftTypes"";", suppressTransaction: true);

            migrationBuilder.Sql("DROP TABLE \"ShiftTypes\";", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TABLE \"ef_temp_ShiftTypes\" RENAME TO \"ShiftTypes\";", suppressTransaction: true);

            // Recreate indexes
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_AreaId\" ON \"ShiftTypes\" (\"AreaId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_JobTypeId\" ON \"ShiftTypes\" (\"JobTypeId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_ShiftGroupingId\" ON \"ShiftTypes\" (\"ShiftGroupingId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_CompanyId_Key\" ON \"ShiftTypes\" (\"CompanyId\", \"Key\") WHERE \"CompanyId\" IS NOT NULL;", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_Scope_AreaId\" ON \"ShiftTypes\" (\"Scope\", \"AreaId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_Scope_MoleculeId\" ON \"ShiftTypes\" (\"Scope\", \"MoleculeId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ShiftTypes_MoleculeId_JobTypeId_Key\" ON \"ShiftTypes\" (\"MoleculeId\", \"JobTypeId\", \"Key\") WHERE \"MoleculeId\" IS NOT NULL;", suppressTransaction: true);

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down: reverse the table rebuild
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_AreaId\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_CompanyId_Key\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_MoleculeId_JobTypeId_Key\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_Scope_AreaId\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_Scope_MoleculeId\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_JobTypeId\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ShiftTypes_ShiftGroupingId\";", suppressTransaction: true);

            // Guard against leftover temp table from EF Core's internal table rebuilds
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"ef_temp_ShiftTypes\";", suppressTransaction: true);

            migrationBuilder.Sql(@"
CREATE TABLE ""ef_temp_ShiftTypes"" (
    ""Id""                   INTEGER NOT NULL CONSTRAINT ""PK_ShiftTypes"" PRIMARY KEY AUTOINCREMENT,
    ""Key""                  TEXT    NOT NULL,
    ""Start""                TEXT    NOT NULL,
    ""End""                  TEXT    NOT NULL,
    ""CompanyId""            INTEGER NOT NULL DEFAULT 0,
    ""CustomName""           TEXT    NULL,
    ""NameKey""              TEXT    NULL,
    ""RowColor""             TEXT    NULL,
    ""JobTypeId""            INTEGER NULL,
    ""MoleculeId""           INTEGER NULL,
    ""ShiftGroupingId""      INTEGER NULL,
    ""TechShiftType""        TEXT    NULL,
    ""EligibleCompanyIds""   TEXT    NULL,
    ""RequiresOfficerRank""  INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT ""FK_ShiftTypes_JobTypes_JobTypeId""             FOREIGN KEY (""JobTypeId"")       REFERENCES ""JobTypes""       (""Id""),
    CONSTRAINT ""FK_ShiftTypes_Molecules_MoleculeId""           FOREIGN KEY (""MoleculeId"")      REFERENCES ""Molecules""      (""Id""),
    CONSTRAINT ""FK_ShiftTypes_ShiftGroupings_ShiftGroupingId"" FOREIGN KEY (""ShiftGroupingId"") REFERENCES ""ShiftGroupings"" (""Id"")
);", suppressTransaction: true);

            migrationBuilder.Sql(@"
INSERT INTO ""ef_temp_ShiftTypes""
    (""Id"", ""Key"", ""Start"", ""End"", ""CompanyId"", ""CustomName"", ""NameKey"", ""RowColor"",
     ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank"")
SELECT
     ""Id"", ""Key"", ""Start"", ""End"", COALESCE(""CompanyId"", 0), ""NameEn"", ""NameKey"", ""RowColor"",
     ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank""
FROM ""ShiftTypes"";", suppressTransaction: true);

            migrationBuilder.Sql("DROP TABLE \"ShiftTypes\";", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TABLE \"ef_temp_ShiftTypes\" RENAME TO \"ShiftTypes\";", suppressTransaction: true);

            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_CompanyId_Key\" ON \"ShiftTypes\" (\"CompanyId\", \"Key\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_JobTypeId\" ON \"ShiftTypes\" (\"JobTypeId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_MoleculeId\" ON \"ShiftTypes\" (\"MoleculeId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_ShiftTypes_ShiftGroupingId\" ON \"ShiftTypes\" (\"ShiftGroupingId\");", suppressTransaction: true);
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ShiftTypes_MoleculeId_JobTypeId_Key\" ON \"ShiftTypes\" (\"MoleculeId\", \"JobTypeId\", \"Key\") WHERE \"MoleculeId\" IS NOT NULL AND \"JobTypeId\" IS NOT NULL;", suppressTransaction: true);

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }
    }
}
