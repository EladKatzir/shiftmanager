namespace ShiftManager.Migrations;

/// <summary>
/// Data backfill for moving day notes from one-per-(molecule, day) to one-per-CALENDAR, where a
/// calendar is (MoleculeId, JobTypeId, TabId). Applied by the FanOutCalendarDayNotesByJobType
/// migration, after ScopeCalendarDayNotesToCalendar has added the columns and dropped the old unique
/// index; pinned by CalendarDayNoteJobTypeFanOutTests.
///
/// <para><b>Why fan out at all.</b> Before this change the read ignored job type entirely, so a
/// molecule note appeared on EVERY job-type calendar in that molecule. Keying each existing note to
/// a single job type would therefore make it vanish from the others. Copying it to each job type
/// preserves exactly where it is visible today, and it is the only option available: NULL cannot be
/// used as an "applies to every job type" sentinel, because a Tech molecule's calendar legitimately
/// runs with JobTypeId = NULL and already owns that value.</para>
///
/// <para><b>Why Tech molecules are excluded (m.Type &lt;&gt; 1).</b> This is a correctness
/// requirement, not a nicety. For a Tech molecule, JobTypeId IS NULL is the permanent, correct
/// steady state — not an "unkeyed" marker. Fanning those notes out to the area's job types makes
/// every copy unreachable, because a Tech calendar only ever asks for JobTypeId = NULL. The note
/// disappears with no error and no audit row. The same ambiguity would also break idempotency: a
/// Tech note written after the migration would be fanned out again on any re-run.</para>
///
/// <para><b>Order matters: INSERT then UPDATE.</b> Both statements select on
/// <c>JobTypeId IS NULL</c>. Running the UPDATE first consumes that marker, so the INSERT then
/// matches nothing, no copies are made, and the migration still reports success — a silent no-op.</para>
///
/// <para><b>The anchor.</b> The original row keeps its Id and takes the job type the calendar
/// DEFAULTS to, so the calendar the user opens shows the row they have been looking at rather than a
/// copy. That default is <c>ORDER BY SortOrder, Name LIMIT 1</c>, which must stay in step with
/// JobTypeService.GetJobTypesForMoleculeAsync's ordering and the Shifts page's
/// <c>AvailableJobTypes.FirstOrDefault()</c>.</para>
///
/// <para><b>Columns carried forward explicitly.</b> Raw SQL bypasses CompanyIdInterceptor, and
/// CompanyId is NOT NULL with no default — omitting it fails with a NOT NULL constraint error.
/// Carrying CompanyId and CreatedByUserId forward is also what keeps the copies valid under the
/// Company (Restrict) and CreatedByUser FKs: they point at rows that already exist.</para>
///
/// <para><b>The 0/1/2 literals are frozen enum values</b> (MoleculeType.Workforce / Tech / Helper).
/// Migration SQL cannot be re-derived if the enum is reordered, so
/// CalendarDayNoteJobTypeFanOutTests asserts those numeric values directly.</para>
///
/// <para>Rows that cannot be keyed — a molecule with no resolvable job types, or MoleculeId NULL —
/// are left exactly as they are. They do not show on a calendar, but nothing a user typed is
/// destroyed, matching the molecule backfill that preceded this one.</para>
/// </summary>
public static class CalendarDayNoteJobTypeFanOutSql
{
    /// <summary>
    /// One copy per resolvable job type EXCEPT the anchor's, which the original row keeps.
    /// The JOIN predicate reproduces JobTypeService.GetJobTypesForMoleculeAsync: active, same area,
    /// molecule-generic or this molecule, and workforce-only types only for Workforce/Helper molecules.
    /// </summary>
    public const string InsertCopiesPerJobType = """
        INSERT INTO CalendarDayNotes
              (Date, Text, CompanyId, MoleculeId, JobTypeId, TabId, CreatedByUserId, CreatedAt, UpdatedAt, UpdatedByUserId)
        SELECT n.Date, n.Text, n.CompanyId, n.MoleculeId, jt.Id, NULL,
               n.CreatedByUserId, n.CreatedAt, n.UpdatedAt, n.UpdatedByUserId
        FROM CalendarDayNotes n
        JOIN Molecules m ON m.Id = n.MoleculeId AND m.Type <> 1
        JOIN JobTypes jt ON jt.IsActive = 1
                        AND jt.AreaId = m.AreaId
                        AND (jt.MoleculeId IS NULL OR jt.MoleculeId = n.MoleculeId)
                        AND (m.Type IN (0,2) OR jt.IsWorkforceOnly = 0)
        WHERE n.MoleculeId IS NOT NULL AND n.JobTypeId IS NULL
          AND jt.Id <> (SELECT jt2.Id FROM JobTypes jt2
                        WHERE jt2.IsActive = 1 AND jt2.AreaId = m.AreaId
                          AND (jt2.MoleculeId IS NULL OR jt2.MoleculeId = n.MoleculeId)
                          AND (m.Type IN (0,2) OR jt2.IsWorkforceOnly = 0)
                        ORDER BY jt2.SortOrder, jt2.Name LIMIT 1);
        """;

    /// <summary>
    /// The original row becomes the anchor copy, keeping its Id so any external reference still
    /// resolves and the default calendar shows the row the user already knows.
    /// </summary>
    public const string KeyOriginalToAnchorJobType = """
        UPDATE CalendarDayNotes SET JobTypeId = (
          SELECT jt2.Id FROM JobTypes jt2 JOIN Molecules m ON m.Id = CalendarDayNotes.MoleculeId
          WHERE jt2.IsActive = 1 AND jt2.AreaId = m.AreaId AND m.Type <> 1
            AND (jt2.MoleculeId IS NULL OR jt2.MoleculeId = CalendarDayNotes.MoleculeId)
            AND (m.Type IN (0,2) OR jt2.IsWorkforceOnly = 0)
          ORDER BY jt2.SortOrder, jt2.Name LIMIT 1)
        WHERE MoleculeId IS NOT NULL AND JobTypeId IS NULL
          AND EXISTS (SELECT 1 FROM Molecules m WHERE m.Id = CalendarDayNotes.MoleculeId AND m.Type <> 1);
        """;

    /// <summary>
    /// Forward statements in the ONLY order that works. See the class remarks: reversing them turns
    /// the whole backfill into a silent no-op that still reports success.
    /// </summary>
    public static readonly string[] Forward =
    {
        InsertCopiesPerJobType,
        KeyOriginalToAnchorJobType
    };
}
