using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// All inputs required to render the Justice analytics page.
/// Bound from the query string of /Admin/Analytics. Defaults filled in by the page model
/// when the user lands without parameters.
/// </summary>
public sealed record JusticeQuery(
    JusticeScope Scope,
    int? ScopeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    JusticeWorkType WorkType,
    bool ExcludeExemptShifts,
    JusticeLevel Level,
    FairnessBasis Basis = FairnessBasis.BySize,
    int? ShiftCategoryId = null,
    /// <summary>
    /// Optional ChoreCategory filter for Chore work-type queries. When set, chore actuals and the
    /// chore sparkline series are restricted to chores whose ChoreType.ChoreCategoryId matches.
    /// Null = all chore categories = pre-Phase-5 behavior. Ignored for Shift/OnDuty work types.
    /// </summary>
    int? ChoreCategoryId = null);

/// <summary>
/// Top-level result for the Justice page.
/// Spread index summarises the imbalance over the rendered rows; lower is fairer.
/// </summary>
public sealed record JusticeViewModel(
    JusticeQuery Query,
    decimal SpreadIndex,
    int SpreadSeverity,                 // 0..4 — number of filled circles in 5-circle severity glyph
    string SpreadLabelKey,              // localization key for the verdict label
    JusticeRow? MostOver,
    JusticeRow? MostUnder,
    List<JusticeRow> Rows,
    decimal MaxRibbonValue);            // largest Actual across rows; used to scale the equity ribbon

/// <summary>
/// One row in the Justice table — represents a user, company, or molecule depending on Level.
/// </summary>
/// <remarks>
/// Positional constructor (7 args) is the builder contract — all call sites use it.
/// The additional <c>init</c>-only properties (A4) carry both fairness bases so the
/// front-end can toggle without a round-trip.
/// </remarks>
public sealed record JusticeRow(
    int Id,
    string Name,
    string? AvatarUrl,                  // populated only for UsersInCompany level; null otherwise
    decimal Actual,
    decimal Expected,
    decimal? DeviationPercent,          // null when Expected == 0 (no target / no capacity); render "no target" pill
    DeviationBand Band)
{
    // A4: per-row share fields (basis-independent) ─────────────────────────────────
    /// <summary>Actual / Σ Actual across all rows in the view. Null when Σ Actual == 0.</summary>
    public decimal? ActualShare { get; init; }

    // A4: both fairness-basis values — always precomputed regardless of active basis ──
    /// <summary>Expected under the BySize basis (capacity / target-weighted).</summary>
    public decimal ExpectedBySize { get; init; }
    /// <summary>Expected under the EqualShare basis (= Σ Actual / N).</summary>
    public decimal ExpectedEqual { get; init; }

    /// <summary>ExpectedBySize / Σ ExpectedBySize across all rows. Null when Σ == 0.</summary>
    public decimal? ExpectedShareBySize { get; init; }
    /// <summary>= 1 / N (equal fraction per row).</summary>
    public decimal? ExpectedShareEqual { get; init; }

    /// <summary>Deviation % computed against ExpectedEqual. Null when ExpectedEqual == 0.</summary>
    public decimal? DeviationPercentEqual { get; init; }
    /// <summary>Deviation band computed against ExpectedEqual.</summary>
    public DeviationBand BandEqual { get; init; }

    // A5: comparison-tier visibility ──────────────────────────────────────────────────
    /// <summary>
    /// Whether the current user may DRILL into this row (navigate to <c>?scopeId={Id}</c>).
    /// Rows outside the user's own subtree are rendered for COMPARISON (read) but are not
    /// drillable. Defaults to <c>true</c> so the legacy 1-arg view path and the calendar
    /// drawers stay backward compatible (every row drillable when no cap is supplied).
    /// </summary>
    public bool IsDrillable { get; init; } = true;

    // A6: per-row sparkline series ────────────────────────────────────────────────────
    /// <summary>
    /// Monthly work-item counts for the 6 calendar months ending at the query's
    /// <c>PeriodEnd</c>, ordered oldest → newest. Populated only when the caller
    /// requests sparklines via <c>includeSparklines: true</c>; null otherwise.
    /// All-zeros list is used for rows that had no work in the span (never null when requested).
    /// </summary>
    public IReadOnlyList<decimal>? Sparkline { get; init; }

    // A7: A/B period comparison delta ─────────────────────────────────────────────────
    /// <summary>
    /// Difference between this row's Actual in the PRIMARY period and the same row's Actual
    /// in the COMPARE period: <c>Primary.Actual - Compare.Actual</c>.
    /// Populated only on the Primary rows returned by
    /// <see cref="IJusticeService.GetComparisonViewAsync"/>; null otherwise (including on
    /// every row in the Compare view).
    /// A missing row on either side is treated as Actual == 0.
    /// </summary>
    public decimal? DeltaVsCompare { get; init; }

    // A9: cross-company pool label ────────────────────────────────────────────────────
    /// <summary>
    /// Company name tag used in the <see cref="ShiftManager.Models.JusticeLevel.UsersInMolecule"/>
    /// view to identify which company each pooled user belongs to.
    /// Null for all other levels.
    /// </summary>
    public string? GroupLabel { get; init; }
}

// ===================================================================================
// A7: A/B period comparison view model.
// ===================================================================================

/// <summary>
/// Result of an A/B period comparison. Holds two full <see cref="JusticeViewModel"/>
/// instances plus a pre-computed delta dictionary for efficient rendering.
/// </summary>
/// <param name="Primary">The primary (period A) Justice view. Each row's
/// <see cref="JusticeRow.DeltaVsCompare"/> is populated.</param>
/// <param name="Compare">The compare (period B) Justice view. Rows are returned as-is;
/// <see cref="JusticeRow.DeltaVsCompare"/> is null on all rows.</param>
/// <param name="ActualDeltaByRowId">
/// <c>Primary.Actual - Compare.Actual</c> for every row id present in EITHER view.
/// A row absent from one view contributes 0 on that side.
/// </param>
public sealed record JusticeComparisonViewModel(
    JusticeViewModel Primary,
    JusticeViewModel Compare,
    IReadOnlyDictionary<int, decimal> ActualDeltaByRowId);

/// <summary>
/// Color/severity bucket for the deviation pill. Asymmetric on purpose:
/// under-loaded is "opportunity" (info-blue), over-loaded is "alarm" (danger-red).
/// </summary>
public enum DeviationBand
{
    NoTarget = -1,
    Under = 0,        // <= -25%
    SoftUnder = 1,    // -25% .. -10%
    Balanced = 2,     // -10% .. +10%
    SoftOver = 3,     // +10% .. +25%
    Over = 4          // >= +25%
}

// ===================================================================================
// Phase 2: in-context Justice for the calendar pages.
// Used by the slide-in drawer triggered by the [Justice] toolbar button on
// /Calendar/Shifts, /Calendar/Chores, /Calendar/OnCall.
// ===================================================================================

/// <summary>
/// Compact Justice payload for the calendar drawer. Same Justice math as the standalone page
/// but trimmed to what the drawer renders: verdict pieces + rows + a small "where to focus" list
/// of the next-N unfilled holes in the active scope.
/// </summary>
public sealed record InContextJusticeViewModel(
    JusticeQuery Query,
    decimal SpreadIndex,
    int SpreadSeverity,
    string SpreadLabelKey,
    JusticeRow? MostOver,
    JusticeRow? MostUnder,
    List<JusticeRow> Rows,
    decimal MaxRibbonValue,
    List<UnfilledHole> WhereToFocus,    // top-N upcoming unfilled cells in scope
    string? FullViewUrl);               // deep-link to /Admin/Analytics pre-scoped (or null)

/// <summary>
/// One unfilled cell on the calendar that the assigner could fix.
/// Renders in the "Where to focus" list and feeds the eligibility-ranking flow.
/// </summary>
public sealed record UnfilledHole(
    string Kind,                        // "shift" | "chore" | "onduty"
    int? ShiftInstanceId,               // populated for Kind="shift"
    DateOnly Date,
    string Label,                       // e.g., "Sat 18 — Night Shift"
    int Deficit,                        // StaffingRequired - FilledCount (or 1 for unassigned chore/onduty)
    int? CompanyId,                     // narrows the eligibility query
    int? JobTypeId,                     // narrows the eligibility query for shifts
    int? DutyTypeValue,                 // narrows for on-duty kinds
    int? UserId);                       // populated for Kind="chore"; lets the frontend build rowId="user-{UserId}"

// ===================================================================================
// Phase 2b: eligibility ranking + Phase 2c: what-if preview.
// ===================================================================================

/// <summary>
/// Identifies which hole the user wants to fill, plus the parent calendar's scope so the service
/// can rank candidates correctly.
/// </summary>
public sealed record HoleSelector(
    string Kind,                        // "shift" | "chore" | "onduty"
    int? ShiftInstanceId,
    DateOnly Date,
    int? CompanyId,
    int? JobTypeId,
    int? DutyTypeValue,
    int? MoleculeId,                    // required for chore/onduty BusyService validation; null for shifts (resolved via company join)
    JusticeQuery ParentScope);          // used to compute current Actual / Expected / spread

/// <summary>
/// Ranked candidate list returned by GetEligibleCandidatesAsync.
/// Candidates are ordered least-loaded-first (most negative deviation %).
/// HardBlocked candidates are returned in a separate list so the drawer can collapse them.
/// </summary>
public sealed record EligibleCandidatesViewModel(
    HoleSelector Hole,
    List<RankedCandidate> Candidates,           // visible, ranked best-fit first
    List<RankedCandidate> HardBlocked);         // collapsed at bottom; included for transparency

public sealed record RankedCandidate(
    int UserId,
    string DisplayName,
    string? AvatarUrl,
    decimal Actual,
    decimal Expected,
    decimal? DeviationPercent,
    DeviationBand Band,
    CandidateEligibility Eligibility);

/// <summary>
/// Per-candidate validation result. For shifts, comes from
/// <see cref="IShiftAssignmentService.ValidateShiftAssignmentAsync"/>.
/// For chores and on-duty (Phase 2d), comes from a degraded check that only verifies
/// vacation overlap and scope membership — the existing POST handlers re-validate the rest.
/// </summary>
public sealed record CandidateEligibility(
    bool IsHardBlocked,
    string? HardBlockReason,            // localization key OR a raw token already in the resx
    List<string> WarningKeys);          // localization keys for soft warnings (vacation overlap, etc.)

/// <summary>
/// Predicted impact of a hypothetical assignment, computed in-memory without writing.
/// </summary>
public sealed record SimulatedAssignment(
    int UserId,
    HoleSelector Hole);

public sealed record ImpactPreviewViewModel(
    decimal SpreadIndexBefore,
    int SpreadSeverityBefore,
    decimal SpreadIndexAfter,
    int SpreadSeverityAfter,
    decimal CandidateActualBefore,
    decimal CandidateActualAfter,
    decimal? CandidateDeviationBefore,
    decimal? CandidateDeviationAfter);
