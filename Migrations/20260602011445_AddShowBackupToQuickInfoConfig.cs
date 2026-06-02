using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddShowBackupToQuickInfoConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowBackup",
                table: "QuickInfoConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Backfill: molecules that saved a Quick Info config BEFORE the primary-Hakam section
            // existed have no OnCallRole/EntityId=0 row, so their widget still hides the primary Hakam.
            // Insert one such section (sorted first, ShowBackup off) for each affected molecule so the
            // fix reaches already-configured molecules — not just freshly-defaulted ones.
            // Idempotent (the NOT EXISTS guard makes a re-run a no-op) and additive, so it is safe to
            // re-apply after a raw .db restore. SectionType 0 == OnCallRole; EntityId 0 == primary Hakam.
            migrationBuilder.Sql(@"
                INSERT INTO QuickInfoConfigs (MoleculeId, SectionType, EntityId, DisplayOrder, IsEnabled, ShowBackup, CreatedBy, CreatedAt)
                SELECT m.MoleculeId, 0, 0,
                       COALESCE((SELECT MIN(q2.DisplayOrder) FROM QuickInfoConfigs q2 WHERE q2.MoleculeId = m.MoleculeId), 0) - 1,
                       1, 0, 0, datetime('now')
                FROM (SELECT DISTINCT MoleculeId FROM QuickInfoConfigs) m
                WHERE NOT EXISTS (
                    SELECT 1 FROM QuickInfoConfigs q3
                    WHERE q3.MoleculeId = m.MoleculeId AND q3.SectionType = 0 AND q3.EntityId = 0
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowBackup",
                table: "QuickInfoConfigs");
        }
    }
}
