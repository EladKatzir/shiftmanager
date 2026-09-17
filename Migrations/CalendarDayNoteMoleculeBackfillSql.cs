namespace ShiftManager.Migrations;

/// <summary>
/// Data backfill for moving day notes from one-per-(desk, day) to one-per-(molecule, day).
/// Applied by the ScopeCalendarDayNotesToMolecule migration after the MoleculeId column and its
/// filtered unique index exist; pinned by CalendarDayNoteMoleculeBackfillTests.
///
/// For each (molecule, day) only ONE note can be keyed. The most recently written one wins —
/// COALESCE(UpdatedAt, CreatedAt) newest first, highest Id breaking ties — because that is the
/// text users most recently saw. Every other note keeps MoleculeId = NULL: it no longer shows on a
/// calendar, but it is NOT deleted, so the migration destroys nothing a user typed. Notes from desks
/// with no molecule stay un-keyed for the same reason.
///
/// Idempotent, and it never keys a legacy note onto a (molecule, day) that already holds a keyed
/// note — doing so would violate the unique index and abort the migration.
/// </summary>
public static class CalendarDayNoteMoleculeBackfillSql
{
    public const string KeyNewestNotePerMoleculeDay = """
        UPDATE CalendarDayNotes
        SET MoleculeId = (SELECT c.MoleculeId FROM Companies c WHERE c.Id = CalendarDayNotes.CompanyId)
        WHERE Id IN (
            SELECT ranked.Id FROM (
                SELECT n.Id,
                       ROW_NUMBER() OVER (
                           PARTITION BY c.MoleculeId, n.Date
                           ORDER BY COALESCE(n.UpdatedAt, n.CreatedAt) DESC, n.Id DESC
                       ) AS rn
                FROM CalendarDayNotes n
                JOIN Companies c ON c.Id = n.CompanyId
                WHERE n.MoleculeId IS NULL
                  AND c.MoleculeId IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM CalendarDayNotes keyed
                      WHERE keyed.MoleculeId = c.MoleculeId AND keyed.Date = n.Date
                  )
            ) ranked
            WHERE ranked.rn = 1
        );
        """;

    public static readonly string[] Forward = { KeyNewestNotePerMoleculeDay };
}
