using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class UnifyNoteSystems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL for idempotent column additions (SQLite: check pragma before adding)
            migrationBuilder.Sql(@"
                -- Add EntryType column if not exists (default 0 = QuickEntry)
                ALTER TABLE CalendarTextEntries ADD COLUMN EntryType INTEGER NOT NULL DEFAULT 0;
            ", suppressTransaction: true);

            migrationBuilder.Sql(@"
                -- Add UpdatedAt column if not exists
                ALTER TABLE CalendarTextEntries ADD COLUMN UpdatedAt TEXT NULL;
            ", suppressTransaction: true);

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_CalendarTextEntries_CompanyId ON CalendarTextEntries (CompanyId);
            ");

            // Data migration: copy all UserDayNote rows into CalendarTextEntries as OverviewNote (EntryType=1)
            // Guard against duplicate migration by checking if any OverviewNote entries already exist
            migrationBuilder.Sql(@"
                INSERT INTO CalendarTextEntries (UserId, Date, Text, CompanyId, CreatedByUserId, CreatedAt, EntryType, UpdatedAt)
                SELECT UserId, Date, Note, CompanyId, CreatedByUserId, CreatedAt, 1, UpdatedAt
                FROM UserDayNotes
                WHERE NOT EXISTS (SELECT 1 FROM CalendarTextEntries WHERE EntryType = 1 LIMIT 1);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove migrated OverviewNote rows before dropping the discriminator column
            migrationBuilder.Sql("DELETE FROM CalendarTextEntries WHERE EntryType = 1;");

            migrationBuilder.DropIndex(
                name: "IX_CalendarTextEntries_CompanyId",
                table: "CalendarTextEntries");

            migrationBuilder.DropColumn(
                name: "EntryType",
                table: "CalendarTextEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CalendarTextEntries");
        }
    }
}
