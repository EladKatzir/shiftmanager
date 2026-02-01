using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupHakamOnDutyType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ✅ PHASE 18: Add "Backup-hakam" OnDuty type (TypeValue=2, first custom type)
            migrationBuilder.Sql(@"
                INSERT INTO OnDutyTypeConfigs (TypeValue, NameEn, NameHe, Icon, Color, IsActive, CreatedAt, CreatedBy)
                SELECT 2, 'Backup-hakam', 'חק""מ רזרבה', '🛡️', '#10b981', 1, datetime('now'), 0
                WHERE NOT EXISTS (
                    SELECT 1 FROM OnDutyTypeConfigs WHERE TypeValue = 2
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the Backup-hakam type if rolling back
            migrationBuilder.Sql(@"
                DELETE FROM OnDutyTypeConfigs WHERE TypeValue = 2 AND NameEn = 'Backup-hakam';
            ");
        }
    }
}
