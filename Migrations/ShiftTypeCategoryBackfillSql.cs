namespace ShiftManager.Migrations;

/// <summary>
/// Idempotent backfill that stamps <c>ShiftType.CategoryId</c> from the categories the original
/// <see cref="ShiftCategoryBackfillSql"/> synthesized. That backfill created one ShiftCategory per
/// distinct *primary* shift type, stamping its Name as "&lt;DisplayName&gt; [&lt;sourceShiftTypeId&gt;]" —
/// a deterministic, collision-free tag. We reverse that EXACT tag to point each shift type at its own
/// category, WITHOUT inventing new categories.
///
/// Safeguards:
///   * Only NULL CategoryId rows are touched (idempotent; never clobbers an admin's manual choice).
///   * Molecule resolved via COALESCE(st.MoleculeId, company.MoleculeId), matching the forward SQL.
///   * The join uses the IDENTICAL Name expression the forward backfill used (see
///     <see cref="ShiftCategoryBackfillSql.MapUsers"/>), so a type maps to its own category and nothing
///     else. Types that were never anyone's primary (incl. shared HOME/OFFLINE) have no matching
///     category and stay NULL — they resolve via the runtime D1 fallback in ShiftCandidateService.
/// </summary>
public static class ShiftTypeCategoryBackfillSql
{
    /// <summary>Stamp CategoryId from the "[id]"-tagged category synthesized by the user backfill.</summary>
    public const string StampCategoryId = @"
        UPDATE ShiftTypes
        SET CategoryId = (
            SELECT sc.Id
            FROM ShiftCategories sc
            LEFT JOIN Companies c ON c.Id = ShiftTypes.CompanyId
            WHERE sc.MoleculeId = COALESCE(ShiftTypes.MoleculeId, c.MoleculeId)
              AND sc.Name = COALESCE(NULLIF(TRIM(ShiftTypes.NameEn), ''), ShiftTypes.TechShiftType, ShiftTypes.Key) || ' [' || ShiftTypes.Id || ']'
        )
        WHERE ShiftTypes.CategoryId IS NULL
          AND EXISTS (
            SELECT 1
            FROM ShiftCategories sc
            LEFT JOIN Companies c ON c.Id = ShiftTypes.CompanyId
            WHERE sc.MoleculeId = COALESCE(ShiftTypes.MoleculeId, c.MoleculeId)
              AND sc.Name = COALESCE(NULLIF(TRIM(ShiftTypes.NameEn), ''), ShiftTypes.TechShiftType, ShiftTypes.Key) || ' [' || ShiftTypes.Id || ']'
          );";

    /// <summary>The forward backfill, in order.</summary>
    public static readonly string[] Forward = { StampCategoryId };
}
