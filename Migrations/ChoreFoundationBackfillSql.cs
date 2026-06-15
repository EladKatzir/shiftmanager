namespace ShiftManager.Migrations;

/// <summary>
/// Raw SQLite statements that seed the chore-parity foundation onto an existing database. Held as
/// shared constants so the <c>BackfillChoreFoundation</c> migration and its regression test run the
/// EXACT same SQL — no drift. Idempotent (safe to re-run): category creation is guarded by NOT EXISTS,
/// type assignment only touches NULLs, weight backfill only touches timed chores, participant marking
/// is a stable predicate.
/// </summary>
public static class ChoreFoundationBackfillSql
{
    /// <summary>1) One "General" category per molecule that has ≥1 chore type and no existing 'General'.</summary>
    public const string CreateGeneralCategories = @"
        INSERT INTO ChoreCategories (MoleculeId, Name, DisplayName, NameEn, NameHe, SortOrder, IsActive, CreatedAt)
        SELECT DISTINCT ct.MoleculeId, 'General', 'General', 'General', 'כללי', 0, 1,
               strftime('%Y-%m-%d %H:%M:%S', 'now')
        FROM ChoreTypes ct
        WHERE NOT EXISTS (
            SELECT 1 FROM ChoreCategories cc
            WHERE cc.MoleculeId = ct.MoleculeId AND cc.Name = 'General');";

    /// <summary>2) Assign each uncategorized chore type to its molecule's General category.</summary>
    public const string AssignTypesToGeneral = @"
        UPDATE ChoreTypes
        SET ChoreCategoryId = (
            SELECT cc.Id FROM ChoreCategories cc
            WHERE cc.MoleculeId = ChoreTypes.MoleculeId AND cc.Name = 'General')
        WHERE ChoreCategoryId IS NULL;";

    /// <summary>3) Freeze WeightMinutes from explicit times where both present (else keep the 480 default).
    /// Guards EndTime > StartTime so midnight-crossing chores (out of scope) keep the default.
    /// TimeOnly is stored as "HH:mm" → substr(t,1,2)=hours, substr(t,4,2)=minutes.</summary>
    public const string BackfillChoreWeights = @"
        UPDATE Chores
        SET WeightMinutes =
            ((CAST(substr(EndTime,1,2)   AS INTEGER) * 60 + CAST(substr(EndTime,4,2)   AS INTEGER))
           - (CAST(substr(StartTime,1,2) AS INTEGER) * 60 + CAST(substr(StartTime,4,2) AS INTEGER)))
        WHERE StartTime IS NOT NULL AND EndTime IS NOT NULL AND EndTime > StartTime;";

    /// <summary>4) Existing active Standard users become chore participants (preserves the roster under
    /// the new DoesChores predicate). AccountType 0 = Standard.</summary>
    public const string MarkChoreParticipants =
        "UPDATE Users SET DoesChores = 1 WHERE IsActive = 1 AND AccountType = 0;";

    /// <summary>The forward backfill, in order.</summary>
    public static readonly string[] Forward =
    {
        CreateGeneralCategories, AssignTypesToGeneral, BackfillChoreWeights, MarkChoreParticipants
    };
}
