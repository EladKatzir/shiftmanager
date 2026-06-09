namespace ShiftManager.Migrations;

/// <summary>
/// The raw SQLite statements that migrate legacy <c>AppUser.PrimaryShiftTypeId</c> into the new
/// <c>DoesShifts</c> flag + <c>ShiftCategory</c> membership model. Held here as shared constants so the
/// <c>BackfillShiftCategoriesFromPrimaryShiftType</c> migration and its regression test execute the
/// EXACT same SQL — no drift between what ships and what is verified.
///
/// Safeguards baked into the SQL:
///   * Molecule resolved via COALESCE(st.MoleculeId, company.MoleculeId) so company-scoped primary
///     shift types still find a home molecule.
///   * Synthesized category Name embeds the source shift-type id ("DisplayName [id]") so the
///     (MoleculeId, Name) unique index can never collide; DisplayName stays clean for the UI.
///   * <see cref="EnforceParticipationInvariant"/> clears DoesShifts for any user whose primary type
///     had no resolvable molecule — enforcing: DoesShifts = true ⇒ user has ≥ 1 category.
/// </summary>
public static class ShiftCategoryBackfillSql
{
    /// <summary>1) Mark shift participants.</summary>
    public const string MarkParticipants =
        "UPDATE Users SET DoesShifts = 1 WHERE PrimaryShiftTypeId IS NOT NULL;";

    /// <summary>2) Create one category per distinct primary shift type that resolves to a molecule.</summary>
    public const string CreateCategories = @"
        INSERT INTO ShiftCategories (MoleculeId, Name, DisplayName, SortOrder, IsActive, Color, CreatedAt)
        SELECT
            COALESCE(st.MoleculeId, c.MoleculeId) AS MoleculeId,
            COALESCE(NULLIF(TRIM(st.NameEn), ''), st.TechShiftType, st.Key) || ' [' || st.Id || ']' AS Name,
            COALESCE(NULLIF(TRIM(st.NameEn), ''), st.TechShiftType, st.Key) AS DisplayName,
            0 AS SortOrder,
            1 AS IsActive,
            st.RowColor AS Color,
            strftime('%Y-%m-%d %H:%M:%S', 'now') AS CreatedAt
        FROM ShiftTypes st
        LEFT JOIN Companies c ON c.Id = st.CompanyId
        WHERE st.Id IN (SELECT DISTINCT PrimaryShiftTypeId FROM Users WHERE PrimaryShiftTypeId IS NOT NULL)
          AND COALESCE(st.MoleculeId, c.MoleculeId) IS NOT NULL;";

    /// <summary>3) Map each participating user to the category derived from their primary shift type.</summary>
    public const string MapUsers = @"
        INSERT INTO UserShiftCategories (UserId, ShiftCategoryId)
        SELECT u.Id, sc.Id
        FROM Users u
        JOIN ShiftTypes st ON st.Id = u.PrimaryShiftTypeId
        LEFT JOIN Companies c ON c.Id = st.CompanyId
        JOIN ShiftCategories sc
             ON sc.MoleculeId = COALESCE(st.MoleculeId, c.MoleculeId)
            AND sc.Name = COALESCE(NULLIF(TRIM(st.NameEn), ''), st.TechShiftType, st.Key) || ' [' || st.Id || ']'
        WHERE u.PrimaryShiftTypeId IS NOT NULL;";

    /// <summary>4) Enforce invariant: a user with no resolvable category is not marked as doing shifts.</summary>
    public const string EnforceParticipationInvariant = @"
        UPDATE Users SET DoesShifts = 0
        WHERE DoesShifts = 1
          AND Id NOT IN (SELECT UserId FROM UserShiftCategories);";

    /// <summary>The forward backfill, in order.</summary>
    public static readonly string[] Forward =
    {
        MarkParticipants, CreateCategories, MapUsers, EnforceParticipationInvariant
    };
}
