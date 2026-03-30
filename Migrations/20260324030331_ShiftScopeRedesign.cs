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
            // EF Core 9 pre-processes ALL operations, does the rebuild first (which handles everything),
            // then the explicit operations fail on the already-modified table.
            //
            // Fix: bypass EF Core's migration builder entirely and do a manual SQLite table rebuild.
            // This is the standard SQLite pattern: create temp → copy → drop → rename.

            migrationBuilder.Sql(@"
                PRAGMA foreign_keys = 0;

                -- Drop old indexes (IF EXISTS for safety — prior table rebuilds may have dropped them)
                DROP INDEX IF EXISTS ""IX_ShiftTypes_CompanyId_Key"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_MoleculeId_JobTypeId_Key"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_JobTypeId"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_MoleculeId"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_ShiftGroupingId"";

                -- Create temp table with final schema (new columns, renamed column, nullable CompanyId, FKs, constraints)
                CREATE TABLE ""ef_temp_ShiftTypes"" (
                    ""Id""                   INTEGER NOT NULL CONSTRAINT ""PK_ShiftTypes"" PRIMARY KEY AUTOINCREMENT,
                    ""Key""                  TEXT    NOT NULL,
                    ""Start""               TEXT    NOT NULL,
                    ""End""                 TEXT    NOT NULL,
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
                );

                -- Copy data: CustomName → NameEn, Scope defaults to 1 (Molecule), new columns default to NULL
                INSERT INTO ""ef_temp_ShiftTypes""
                    (""Id"", ""Key"", ""Start"", ""End"", ""CompanyId"", ""NameEn"", ""NameKey"", ""RowColor"",
                     ""Scope"", ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
                     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank"")
                SELECT
                     ""Id"", ""Key"", ""Start"", ""End"", ""CompanyId"", ""CustomName"", ""NameKey"", ""RowColor"",
                     1, ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
                     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank""
                FROM ""ShiftTypes"";

                -- Swap tables
                DROP TABLE ""ShiftTypes"";
                ALTER TABLE ""ef_temp_ShiftTypes"" RENAME TO ""ShiftTypes"";

                -- Recreate indexes
                CREATE INDEX ""IX_ShiftTypes_AreaId""              ON ""ShiftTypes"" (""AreaId"");
                CREATE INDEX ""IX_ShiftTypes_JobTypeId""           ON ""ShiftTypes"" (""JobTypeId"");
                CREATE INDEX ""IX_ShiftTypes_ShiftGroupingId""     ON ""ShiftTypes"" (""ShiftGroupingId"");
                CREATE INDEX ""IX_ShiftTypes_CompanyId_Key""       ON ""ShiftTypes"" (""CompanyId"", ""Key"")                        WHERE ""CompanyId"" IS NOT NULL;
                CREATE INDEX ""IX_ShiftTypes_Scope_AreaId""        ON ""ShiftTypes"" (""Scope"", ""AreaId"");
                CREATE INDEX ""IX_ShiftTypes_Scope_MoleculeId""    ON ""ShiftTypes"" (""Scope"", ""MoleculeId"");
                CREATE UNIQUE INDEX ""IX_ShiftTypes_MoleculeId_JobTypeId_Key"" ON ""ShiftTypes"" (""MoleculeId"", ""JobTypeId"", ""Key"") WHERE ""MoleculeId"" IS NOT NULL;

                PRAGMA foreign_keys = 1;
            ", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down: reverse the table rebuild — restore CustomName, NOT NULL CompanyId, remove new columns/FKs/constraints
            migrationBuilder.Sql(@"
                PRAGMA foreign_keys = 0;

                DROP INDEX IF EXISTS ""IX_ShiftTypes_AreaId"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_CompanyId_Key"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_MoleculeId_JobTypeId_Key"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_Scope_AreaId"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_Scope_MoleculeId"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_JobTypeId"";
                DROP INDEX IF EXISTS ""IX_ShiftTypes_ShiftGroupingId"";

                CREATE TABLE ""ef_temp_ShiftTypes"" (
                    ""Id""                   INTEGER NOT NULL CONSTRAINT ""PK_ShiftTypes"" PRIMARY KEY AUTOINCREMENT,
                    ""Key""                  TEXT    NOT NULL,
                    ""Start""               TEXT    NOT NULL,
                    ""End""                 TEXT    NOT NULL,
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
                );

                INSERT INTO ""ef_temp_ShiftTypes""
                    (""Id"", ""Key"", ""Start"", ""End"", ""CompanyId"", ""CustomName"", ""NameKey"", ""RowColor"",
                     ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
                     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank"")
                SELECT
                     ""Id"", ""Key"", ""Start"", ""End"", COALESCE(""CompanyId"", 0), ""NameEn"", ""NameKey"", ""RowColor"",
                     ""JobTypeId"", ""MoleculeId"", ""ShiftGroupingId"",
                     ""TechShiftType"", ""EligibleCompanyIds"", ""RequiresOfficerRank""
                FROM ""ShiftTypes"";

                DROP TABLE ""ShiftTypes"";
                ALTER TABLE ""ef_temp_ShiftTypes"" RENAME TO ""ShiftTypes"";

                CREATE INDEX ""IX_ShiftTypes_CompanyId_Key""                   ON ""ShiftTypes"" (""CompanyId"", ""Key"");
                CREATE INDEX ""IX_ShiftTypes_JobTypeId""                       ON ""ShiftTypes"" (""JobTypeId"");
                CREATE INDEX ""IX_ShiftTypes_MoleculeId""                      ON ""ShiftTypes"" (""MoleculeId"");
                CREATE INDEX ""IX_ShiftTypes_ShiftGroupingId""                 ON ""ShiftTypes"" (""ShiftGroupingId"");
                CREATE UNIQUE INDEX ""IX_ShiftTypes_MoleculeId_JobTypeId_Key"" ON ""ShiftTypes"" (""MoleculeId"", ""JobTypeId"", ""Key"") WHERE ""MoleculeId"" IS NOT NULL AND ""JobTypeId"" IS NOT NULL;

                PRAGMA foreign_keys = 1;
            ", suppressTransaction: true);
        }
    }
}
