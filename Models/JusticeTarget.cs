namespace ShiftManager.Models;

/// <summary>
/// Configurable expected workload per (work-type, scope) combination, used by the Justice Analytics page.
///
/// Resolution algorithm (executed at query time, never stored):
///   1. WorkType=Shift  -> ignore this table; expected = sum(ShiftInstance.StaffingRequired) over scope+period.
///   2. Else: look for an explicit row matching (WorkType, ScopeKind, ScopeId).
///   3. If found, expected = ExpectedCount * period-duration-in-PeriodKind-units.
///   4. Otherwise fall through to the Global row (ScopeKind=Global, ScopeId=null) and multiply by
///      the active-user headcount in the requested scope.
///
/// Tenant isolation: deliberately NOT IBelongsToCompany.
///   - Global / Area / Molecule rows are conceptually tenant-agnostic (CompanyId = null).
///   - Company-scoped overrides carry CompanyId so the tenant filter in AppDbContext
///     (e.CompanyId == null || e.CompanyId == tenantId) still allows them through correctly.
///   - Same pattern as <see cref="EmailConfig"/>.
/// </summary>
public class JusticeTarget
{
    public int Id { get; set; }

    /// <summary>
    /// Null for Global / Area / Molecule rows.
    /// Set explicitly by the upsert handler when ScopeKind == Company.
    /// CompanyIdInterceptor does NOT touch this entity (no IBelongsToCompany).
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>
    /// Which work-type the target applies to. Shift rows are accepted but ignored at query time
    /// (capacity-driven). Stored only so the settings UI can show "AUTO from calendar capacity"
    /// for the Shift row consistently.
    /// </summary>
    public JusticeWorkType WorkType { get; set; }

    /// <summary>
    /// Hierarchy level this row applies to. Combined with ScopeId to identify the exact scope.
    /// </summary>
    public JusticeScope ScopeKind { get; set; }

    /// <summary>
    /// Null only when ScopeKind == Global. Otherwise references the Project/Area/Molecule/Company id.
    /// </summary>
    public int? ScopeId { get; set; }

    /// <summary>
    /// Expected count per PeriodKind unit.
    /// For Global rows this is per-user (e.g., 3 chores per user per month).
    /// For Company / Molecule / Area rows this is the absolute expected total at that scope per period.
    /// decimal(7,2) to allow fractional targets like "2.5 on-duties / month".
    /// </summary>
    public decimal ExpectedCount { get; set; }

    /// <summary>
    /// Bucket the ExpectedCount is denominated in.
    /// </summary>
    public PeriodKind PeriodKind { get; set; }

    /// <summary>
    /// Optional admin note explaining the override (e.g., "specialty unit, fewer chores").
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Null for system-seeded global defaults. Set to AppUser.Id when an admin saves an override.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
